using BeaconTester.Core.Models;
using BeaconTester.Core.Redis;
using Serilog;

namespace BeaconTester.Core
{
    /// <summary>
    /// Executes test scenarios against a Beacon instance
    /// </summary>
    public class TestRunner : IDisposable
    {
        private readonly ILogger _logger;
        private readonly RedisAdapter _redis;
        private readonly RedisMonitor? _monitor;
        private readonly TestConfig _config;

        /// <summary>
        /// Creates a new test runner with Redis connection
        /// </summary>
        public TestRunner(
            RedisConfiguration redisConfig,
            ILogger logger,
            TestConfig? config = null,
            bool enableMonitoring = false
        )
        {
            _logger = logger.ForContext<TestRunner>();
            _redis = new RedisAdapter(redisConfig, logger);
            
            // Initialize default config or use provided one
            _config = config ?? new TestConfig();
            
            // Try to read configuration from environment variables
            ReadConfigFromEnvironment();
            
            _logger.Information(
                "TestRunner initialized with BeaconCycleTime: {CycleTime}ms, StepDelayMultiplier: {DelayMultiplier}, TimeoutMultiplier: {TimeoutMultiplier}, GlobalTimeoutMultiplier: {GlobalMultiplier}",
                _config.BeaconCycleTimeMs, _config.DefaultStepDelayMultiplier, _config.DefaultTimeoutMultiplier, _config.GlobalTimeoutMultiplier);

            if (enableMonitoring)
            {
                _monitor = new RedisMonitor(redisConfig, logger);
                _monitor.StartMonitoring("*");
                _logger.Information("Redis monitoring enabled");
            }
        }

        /// <summary>
        /// Runs a single test scenario
        /// </summary>
        public async Task<TestResult> RunTestAsync(TestScenario scenario, double? overrideTimeoutMultiplier = null)
        {
            // Apply the global timeout multiplier or override if provided
            double timeoutMultiplier = overrideTimeoutMultiplier ?? _config.GlobalTimeoutMultiplier;
            
            _logger.Information("Running test scenario: {TestName}", scenario.Name);

            // Ensure the scenario is in the correct format
            scenario.NormalizeScenario();

            var result = new TestResult
            {
                Name = scenario.Name,
                StartTime = DateTime.UtcNow,
                Scenario = scenario,
            };

            try
            {
                // Clear existing outputs only if specified by the scenario
                if (scenario.ClearOutputs)
                {
                    _logger.Debug("Clearing output keys for scenario: {TestName}", scenario.Name);
                    await _redis.ClearKeysAsync($"{RedisAdapter.OUTPUT_PREFIX}*");
                }
                else
                {
                    _logger.Debug("Skipping output key clearing for scenario: {TestName}", scenario.Name);
                }

                // Set any pre-test outputs if defined
                if (scenario.PreSetOutputs != null && scenario.PreSetOutputs.Count > 0)
                {
                    await _redis.SetPreTestOutputsAsync(scenario.PreSetOutputs);
                }

                // Run each step in sequence
                foreach (var step in scenario.Steps)
                {
                    // Combine the scenario timeout multiplier with the global one
                    double combinedMultiplier = scenario.TimeoutMultiplier * timeoutMultiplier;
                    var stepResult = await RunTestStepAsync(step, combinedMultiplier);
                    result.StepResults.Add(stepResult);

                    // If a step fails and it has expectations, stop the test
                    if (!stepResult.Success && step.Expectations.Count > 0)
                    {
                        _logger.Warning("Test step '{StepName}' failed, stopping test", step.Name);
                        result.Success = false;
                        break;
                    }
                }

                // If all steps pass, the test passes
                if (result.StepResults.All(s => s.Success))
                {
                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.Error(ex, "Error running test scenario {TestName}", scenario.Name);
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;

            if (result.Success)
            {
                _logger.Information(
                    "Test scenario '{TestName}' completed successfully in {Duration}ms",
                    scenario.Name,
                    result.Duration.TotalMilliseconds
                );
            }
            else
            {
                _logger.Warning(
                    "Test scenario '{TestName}' failed in {Duration}ms",
                    scenario.Name,
                    result.Duration.TotalMilliseconds
                );
            }

            return result;
        }

        /// <summary>
        /// Runs a single test step
        /// </summary>
        /// <param name="step">The test step to run</param>
        /// <param name="timeoutMultiplier">Multiplier for all timeouts in this step</param>
        private async Task<StepResult> RunTestStepAsync(TestStep step, double timeoutMultiplier = 1.0)
        {
            _logger.Debug("Running test step: {StepName}", step.Name);
            DateTime startTime = DateTime.UtcNow;

            var result = new StepResult
            {
                Success =
                    true // Assume success until proven otherwise
                ,
            };

            try
            {
                // Send all inputs to Redis
                if (step.Inputs.Count > 0)
                {
                    await _redis.SendInputsAsync(step.Inputs);
                }

                // Calculate cycle-aware delay for rule processing
                int delayMs;
                if (step.Delay > 0)
                {
                    // If explicit delay is specified, apply timeout multiplier
                    delayMs = (int)(step.Delay * timeoutMultiplier);
                }
                else
                {
                    // Otherwise calculate based on Beacon cycle time
                    int delayMultiplier = step.DelayMultiplier ?? _config.DefaultStepDelayMultiplier;
                    delayMs = _config.CalculateStepWaitTimeMs(delayMultiplier);
                }
                
                _logger.Debug("Waiting for {Delay}ms (cycle time: {CycleTime}ms, multiplier: {Multiplier})", 
                    delayMs, _config.BeaconCycleTimeMs, timeoutMultiplier);
                await Task.Delay(delayMs);

                // Check all expectations
                if (step.Expectations.Count > 0)
                {
                    // Apply timeout multiplier to each expectation
                    foreach (var expectation in step.Expectations)
                    {
                        // If timeout isn't set, calculate based on configured Beacon cycle time
                        if (!expectation.TimeoutMs.HasValue)
                        {
                            int cycleMultiplier = expectation.TimeoutMultiplier ?? _config.DefaultTimeoutMultiplier;
                            expectation.TimeoutMs = _config.CalculateTimeoutMs(cycleMultiplier);
                            
                            _logger.Debug("Set default timeout for {Key} to {Timeout}ms ({Multiplier} cycles + {Buffer}ms buffer)",
                                expectation.Key, expectation.TimeoutMs.Value, cycleMultiplier, _config.TimeoutBufferMs);
                        }

                        // Apply the multiplier
                        if (expectation.TimeoutMs.HasValue)
                        {
                            int originalTimeout = expectation.TimeoutMs.Value;
                            expectation.TimeoutMs = (int)(originalTimeout * timeoutMultiplier);
                            
                            if (expectation.TimeoutMs.Value != originalTimeout)
                            {
                                _logger.Debug("Adjusted timeout for {Key} from {Original}ms to {Adjusted}ms",
                                    expectation.Key, originalTimeout, expectation.TimeoutMs.Value);
                            }
                        }
                        
                        if (expectation.PollingIntervalMs.HasValue)
                        {
                            // Apply multiplier to explicit polling interval
                            int originalInterval = expectation.PollingIntervalMs.Value;
                            expectation.PollingIntervalMs = Math.Max(50, (int)(originalInterval * timeoutMultiplier));
                        }
                        else
                        {
                            // Calculate polling interval based on Beacon cycle time
                            double pollingFactor = expectation.PollingIntervalFactor ?? _config.PollingIntervalFactor;
                            expectation.PollingIntervalMs = _config.CalculatePollingIntervalMs(pollingFactor);
                            
                            _logger.Debug("Set polling interval for {Key} to {Interval}ms ({Factor}x cycle time)",
                                expectation.Key, expectation.PollingIntervalMs.Value, pollingFactor);
                        }
                    }
                    
                    var expectationResults = await _redis.CheckExpectationsAsync(step.Expectations);
                    result.ExpectationResults = expectationResults;

                    // If any expectation fails, the step fails
                    result.Success = expectationResults.All(e => e.Success);

                    if (!result.Success)
                    {
                        var failedExpectations = expectationResults.Where(e => !e.Success).ToList();
                        _logger.Warning(
                            "Failed expectations in step '{StepName}': {FailedCount}",
                            step.Name,
                            failedExpectations.Count
                        );

                        foreach (var failed in failedExpectations)
                        {
                            _logger.Debug(
                                "Failed expectation {Key}: {Details}",
                                failed.Key,
                                failed.Details
                            );
                        }
                    }
                }

                result.Duration = DateTime.UtcNow - startTime;
                _logger.Debug(
                    "Step '{StepName}' completed in {Duration}ms",
                    step.Name,
                    result.Duration.TotalMilliseconds
                );
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Duration = DateTime.UtcNow - startTime;
                _logger.Error(ex, "Error running test step {StepName}", step.Name);
            }

            return result;
        }

        /// <summary>
        /// Runs a batch of test scenarios
        /// </summary>
        public async Task<List<TestResult>> RunTestBatchAsync(List<TestScenario> scenarios)
        {
            _logger.Information("Running {TestCount} test scenarios", scenarios.Count);
            var results = new List<TestResult>();

            foreach (var scenario in scenarios)
            {
                var result = await RunTestAsync(scenario);
                results.Add(result);
            }

            var successCount = results.Count(r => r.Success);
            _logger.Information(
                "Completed {TestCount} tests with {SuccessCount} successes and {FailureCount} failures",
                results.Count,
                successCount,
                results.Count - successCount
            );
            
            // Generate detailed validation summary if there are failures
            if (results.Count - successCount > 0)
            {
                GenerateValidationSummary(results);
            }

            return results;
        }

        /// <summary>
        /// Generates a detailed validation summary report for test results
        /// </summary>
        private void GenerateValidationSummary(List<TestResult> results)
        {
            _logger.Information("========== VALIDATION SUMMARY ==========");
            
            foreach (var result in results.Where(r => !r.Success))
            {
                _logger.Information("Test: {TestName} - FAILED", result.Name);
                _logger.Information("  - Duration: {Duration}ms", result.Duration.TotalMilliseconds);
                
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    _logger.Error("  - Error: {ErrorMessage}", result.ErrorMessage);
                }
                
                int stepIndex = 0;
                foreach (var stepResult in result.StepResults.Where(s => !s.Success))
                {
                    var stepName = result.Scenario?.Steps.Count > stepIndex ? 
                        result.Scenario.Steps[stepIndex].Name : $"Step {stepIndex + 1}";
                    
                    _logger.Information("  - Failed Step: {StepName}", stepName);
                    stepIndex++;
                    
                    if (!string.IsNullOrEmpty(stepResult.ErrorMessage))
                    {
                        _logger.Error("    - Error: {ErrorMessage}", stepResult.ErrorMessage);
                    }
                    
                    foreach (var expectResult in stepResult.ExpectationResults.Where(e => !e.Success))
                    {
                        _logger.Warning("    - Failed Expectation: {Key}", expectResult.Key);
                        _logger.Warning("      Expected: {ExpectedType} {ExpectedValue}", 
                            expectResult.Expected?.GetType().Name ?? "null", 
                            expectResult.Expected);
                        _logger.Warning("      Actual:   {ActualType} {ActualValue}", 
                            expectResult.Actual?.GetType().Name ?? "null", 
                            expectResult.Actual);
                        _logger.Warning("      Details:  {Details}", 
                            expectResult.Details ?? "No details available");
                    }
                }
                
                _logger.Information("---------------------------------------");
            }
            
            _logger.Information("=========================================");
        }

        /// <summary>
        /// Reads test configuration from environment variables
        /// </summary>
        private void ReadConfigFromEnvironment()
        {
            // Read Beacon cycle time from environment variable
            var cycleTimeStr = Environment.GetEnvironmentVariable("BEACON_CYCLE_TIME");
            if (!string.IsNullOrEmpty(cycleTimeStr) && int.TryParse(cycleTimeStr, out int cycleTime) && cycleTime > 0)
            {
                _config.BeaconCycleTimeMs = cycleTime;
                _logger.Debug("Set BeaconCycleTimeMs to {CycleTime}ms from environment variable", cycleTime);
            }
            
            // Read step delay multiplier from environment variable
            var stepDelayStr = Environment.GetEnvironmentVariable("STEP_DELAY_MULTIPLIER");
            if (!string.IsNullOrEmpty(stepDelayStr) && int.TryParse(stepDelayStr, out int stepDelay) && stepDelay > 0)
            {
                _config.DefaultStepDelayMultiplier = stepDelay;
                _logger.Debug("Set DefaultStepDelayMultiplier to {Multiplier} from environment variable", stepDelay);
            }
            
            // Read timeout multiplier from environment variable
            var timeoutMultiplierStr = Environment.GetEnvironmentVariable("TIMEOUT_MULTIPLIER");
            if (!string.IsNullOrEmpty(timeoutMultiplierStr) && int.TryParse(timeoutMultiplierStr, out int timeoutMultiplier) && timeoutMultiplier > 0)
            {
                _config.DefaultTimeoutMultiplier = timeoutMultiplier;
                _logger.Debug("Set DefaultTimeoutMultiplier to {Multiplier} from environment variable", timeoutMultiplier);
            }
            
            // Read global timeout multiplier from environment variable
            var globalMultiplierStr = Environment.GetEnvironmentVariable("GLOBAL_TIMEOUT_MULTIPLIER");
            if (!string.IsNullOrEmpty(globalMultiplierStr) && double.TryParse(globalMultiplierStr, out double globalMultiplier) && globalMultiplier > 0)
            {
                _config.GlobalTimeoutMultiplier = globalMultiplier;
                _logger.Debug("Set GlobalTimeoutMultiplier to {Multiplier} from environment variable", globalMultiplier);
            }
        }
        
        /// <summary>
        /// Disposes of resources
        /// </summary>
        public void Dispose()
        {
            _redis.Dispose();
            _monitor?.Dispose();
        }
    }
}
