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
                // Clear any existing outputs to ensure a clean test
                await _redis.ClearKeysAsync($"{RedisAdapter.OUTPUT_PREFIX}*");

                // Set any pre-test outputs if defined
                if (scenario.PreSetOutputs != null && scenario.PreSetOutputs.Count > 0)
                {
                    await _redis.SetPreTestOutputsAsync(scenario.PreSetOutputs);
                }

                // Run each step in sequence
                foreach (var step in scenario.Steps)
                {
                    var stepResult = await RunTestStepAsync(step);
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
        private async Task<StepResult> RunTestStepAsync(TestStep step)
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
                    await Task.Delay(step.Delay);
                }

                // Check all expectations
                if (step.Expectations.Count > 0)
                {
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

            return results;
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
