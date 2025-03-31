using System.CommandLine;
using System.Text.Json;
using BeaconTester.Core.Models;
using BeaconTester.RuleAnalyzer.Generation;
using BeaconTester.RuleAnalyzer.Parsing;
using Serilog;

namespace BeaconTester.Runner.Commands
{
    /// <summary>
    /// Command to generate test scenarios from rules
    /// </summary>
    public class GenerateCommand
    {
        /// <summary>
        /// Creates the generate command
        /// </summary>
        public Command Create()
        {
            var command = new Command("generate", "Generate test scenarios from rule definitions");

            // Add options
            var rulesOption = new Option<string>(
                name: "--rules",
                description: "Path to the rules YAML file"
            )
            {
                IsRequired = true,
            };

            var outputOption = new Option<string>(
                name: "--output",
                description: "Path to output the generated test scenarios"
            )
            {
                IsRequired = true,
            };

            command.AddOption(rulesOption);
            command.AddOption(outputOption);

            // Set handler
            command.SetHandler(
                (rulesPath, outputPath) => HandleGenerateCommand(rulesPath, outputPath),
                rulesOption,
                outputOption
            );

            return command;
        }

        /// <summary>
        /// Handles the generate command
        /// </summary>
        private async Task<int> HandleGenerateCommand(string rulesPath, string outputPath)
        {
            var logger = Log.Logger.ForContext<GenerateCommand>();

            try
            {
                logger.Information("Generating test scenarios from {RulesPath}", rulesPath);

                // Check if rules file exists
                if (!File.Exists(rulesPath))
                {
                    logger.Error("Rules file not found: {RulesPath}", rulesPath);
                    return 1;
                }

                // Create directory for output if it doesn't exist
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Parse rules
                var ruleParser = new RuleParser(logger);
                var rules = ruleParser.ParseRulesFromFile(rulesPath);

                // Generate test scenarios
                var generator = new TestScenarioGenerator(logger);
                var scenarios = generator.GenerateScenarios(rules);

                // Save test scenarios
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                };

                var scenariosWrapper = new { Scenarios = scenarios };
                string json = JsonSerializer.Serialize(scenariosWrapper, options);

                await File.WriteAllTextAsync(outputPath, json);

                logger.Information(
                    "Generated {ScenarioCount} test scenarios and saved to {OutputPath}",
                    scenarios.Count,
                    outputPath
                );

                return 0;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error generating test scenarios");
                return 1;
            }
        }
    }
}
