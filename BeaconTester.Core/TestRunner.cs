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

        /// <summary>
        /// Creates a new test runner with Redis connection
        /// </summary>
        public TestRunner(
            RedisConfiguration redisConfig,
            ILogger logger,
            bool enableMonitoring = false
        )
        {
            _logger = logger.ForContext<TestRunner>();
            _redis = new RedisAdapter(redisConfig, logger);

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
        public async Task<TestResult> RunTestAsync(TestScenario scenario)
        {
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
                    var stepResult = await RunTestStepAsync(step, scenario.TimeoutMultiplier);
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

                // Wait for rules to process, if a delay is specified
                if (step.Delay > 0)
                {
                    int adjustedDelay = (int)(step.Delay * timeoutMultiplier);
                    _logger.Debug("Waiting for {Delay}ms (original: {OriginalDelay}ms, multiplier: {Multiplier})", 
                        adjustedDelay, step.Delay, timeoutMultiplier);
                    await Task.Delay(adjustedDelay);
                }

                // Check all expectations
                if (step.Expectations.Count > 0)
                {
                    // Apply timeout multiplier to each expectation
                    foreach (var expectation in step.Expectations)
                    {
                        // If timeout isn't set, calculate a reasonable default based on the Beacon cycle time
                        // For most test cases, 3 cycle times should be sufficient (data in, processing, data out)
                        if (!expectation.TimeoutMs.HasValue)
                        {
                            // Default to 3 cycle times (300ms for default 100ms cycle) plus a small buffer
                            expectation.TimeoutMs = 3 * 100 + 50; // 350ms default
                            _logger.Debug("Set default timeout for {Key} to {Timeout}ms (3 cycles + 50ms buffer)",
                                expectation.Key, expectation.TimeoutMs.Value);
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
                            int originalInterval = expectation.PollingIntervalMs.Value;
                            expectation.PollingIntervalMs = Math.Max(50, (int)(originalInterval * timeoutMultiplier));
                        }
                        else
                        {
                            expectation.PollingIntervalMs = 100;
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
        /// Disposes of resources
        /// </summary>
        public void Dispose()
        {
            _redis.Dispose();
            _monitor?.Dispose();
        }
    }
}
