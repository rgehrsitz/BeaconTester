using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using BeaconTester.Core;
using BeaconTester.Core.Models;
using BeaconTester.Core.Redis;
using Serilog;

namespace BeaconTester.Runner.Commands
{
    /// <summary>
    /// Command to run test scenarios
    /// </summary>
    public class RunCommand
    {
        /// <summary>
        /// Creates the run command
        /// </summary>
        public Command Create()
        {
            var command = new Command("run", "Run test scenarios against a Beacon instance");

            // Add required options
            var scenariosOption = new Option<string>(
                name: "--scenarios",
                description: "Path to the test scenarios JSON file"
            )
            {
                IsRequired = true,
            };

            var outputOption = new Option<string>(
                name: "--output",
                description: "Path to output the test results"
            );

            var redisHostOption = new Option<string>(
                name: "--redis-host",
                description: "Redis host (default: localhost)"
            )
            {
                IsRequired = false,
            };

            var redisPortOption = new Option<int>(
                name: "--redis-port",
                description: "Redis port (default: 6379)"
            )
            {
                IsRequired = false,
            };

            var monitorOption = new Option<bool>(
                name: "--monitor",
                description: "Enable Redis monitoring (default: false)"
            );

            // Note: Cycle time configuration is now handled via environment variables:
            // - BEACON_CYCLE_TIME: Beacon's cycle time in milliseconds
            // - STEP_DELAY_MULTIPLIER: Default multiplier for step delay times
            // - TIMEOUT_MULTIPLIER: Default multiplier for expectation timeouts
            // - GLOBAL_TIMEOUT_MULTIPLIER: Global multiplier applied to all timeouts

            command.AddOption(scenariosOption);
            command.AddOption(outputOption);
            command.AddOption(redisHostOption);
            command.AddOption(redisPortOption);
            command.AddOption(monitorOption);

            // Define the handler method
            command.SetHandler(RunHandler, scenariosOption, outputOption, redisHostOption, redisPortOption, monitorOption);

            return command;
        }

        /// <summary>
        /// Handler method for the run command
        /// </summary>
        private async Task<int> RunHandler(string scenariosPath, string? outputPath, string? redisHost, int redisPort, bool monitor)
        {
            // Create empty options object (config will be read from environment variables)
            var testConfigOptions = new TestConfigOptions();
            
            // Execute the command logic and return the result code
            return await HandleRunCommand(scenariosPath, outputPath, redisHost, redisPort, monitor, testConfigOptions);
        }
        
        /// <summary>
        /// Handles the run command
        /// </summary>

        
        /// <summary>
        /// Options class for TestConfig parameters to work around parameter count limitations
        /// </summary>
        private class TestConfigOptions
        {
            public int BeaconCycleTime { get; set; }
            public int StepDelayMultiplier { get; set; }
            public int TimeoutMultiplier { get; set; }
            public double GlobalTimeoutMultiplier { get; set; }
        }

        private async Task<int> HandleRunCommand(
            string scenariosPath,
            string? outputPath,
            string? redisHost,
            int redisPort,
            bool monitor,
            TestConfigOptions configOptions
        )
        {
            var logger = Log.Logger.ForContext<RunCommand>();

            try
            {
                logger.Information("Running test scenarios from {ScenariosPath}", scenariosPath);

                // Check if scenarios file exists
                if (!File.Exists(scenariosPath))
                {
                    logger.Error("Scenarios file not found: {ScenariosPath}", scenariosPath);
                    return 1;
                }

                // Load test scenarios
                string json = await File.ReadAllTextAsync(scenariosPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                };

                var scenariosDocument = JsonSerializer.Deserialize<ScenariosDocument>(
                    json,
                    options
                );

                if (
                    scenariosDocument == null
                    || scenariosDocument.Scenarios == null
                    || scenariosDocument.Scenarios.Count == 0
                )
                {
                    logger.Error("No test scenarios found in {ScenariosPath}", scenariosPath);
                    return 1;
                }

                // Configure Redis
                var redisConfig = new RedisConfiguration();

                if (!string.IsNullOrEmpty(redisHost))
                {
                    redisConfig.Endpoints.Clear();
                    redisConfig.Endpoints.Add($"{redisHost}:{(redisPort > 0 ? redisPort : 6379)}");
                }

                // Create test config with cycle time settings
                var testConfig = new TestConfig();
                
                // Apply command line overrides if specified
                if (configOptions.BeaconCycleTime > 0)
                {
                    testConfig.BeaconCycleTimeMs = configOptions.BeaconCycleTime;
                    logger.Information("Using Beacon cycle time: {CycleTime}ms", testConfig.BeaconCycleTimeMs);
                }
                
                if (configOptions.StepDelayMultiplier > 0)
                {
                    testConfig.DefaultStepDelayMultiplier = configOptions.StepDelayMultiplier;
                    logger.Information("Using step delay multiplier: {Multiplier}", testConfig.DefaultStepDelayMultiplier);
                }
                
                if (configOptions.TimeoutMultiplier > 0)
                {
                    testConfig.DefaultTimeoutMultiplier = configOptions.TimeoutMultiplier;
                    logger.Information("Using timeout multiplier: {Multiplier}", testConfig.DefaultTimeoutMultiplier);
                }
                
                if (configOptions.GlobalTimeoutMultiplier > 0)
                {
                    testConfig.GlobalTimeoutMultiplier = configOptions.GlobalTimeoutMultiplier;
                    logger.Information("Using global timeout multiplier: {Multiplier}", testConfig.GlobalTimeoutMultiplier);
                }
                
                // Run tests
                using var testRunner = new TestRunner(redisConfig, logger, testConfig, monitor);
                var results = await testRunner.RunTestBatchAsync(scenariosDocument.Scenarios);

                // Save results if output path is specified
                if (!string.IsNullOrEmpty(outputPath))
                {
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    var resultsWrapper = new { Results = results };
                    string resultsJson = JsonSerializer.Serialize(
                        resultsWrapper,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        }
                    );

                    await File.WriteAllTextAsync(outputPath, resultsJson);
                    logger.Information("Test results saved to {OutputPath}", outputPath);
                }

                // Print summary
                var successCount = results.Count(r => r.Success);
                var failureCount = results.Count - successCount;

                Console.WriteLine();
                Console.WriteLine($"Test Summary: {successCount} passed, {failureCount} failed");

                if (failureCount > 0)
                {
                    Console.WriteLine("\nFailed tests:");
                    foreach (var result in results.Where(r => !r.Success))
                    {
                        Console.WriteLine(
                            $"  - {result.Name}: {result.ErrorMessage ?? "Test assertions failed"}"
                        );
                    }
                }

                return failureCount > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error running test scenarios");
                return 1;
            }
        }

        /// <summary>
        /// Document wrapper for scenarios
        /// </summary>
        private class ScenariosDocument
        {
            /// <summary>
            /// The test scenarios
            /// </summary>
            public List<TestScenario> Scenarios { get; set; } = new List<TestScenario>();
        }
    }
}
