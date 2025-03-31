using System.Text.Json.Serialization;

namespace BeaconTester.Core.Models
{
    /// <summary>
    /// Complete result of a test scenario execution
    /// </summary>
    public class TestResult
    {
        /// <summary>
        /// Name of the test scenario that was executed
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Whether the test passed (all steps successful)
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Total time taken to execute all steps
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Results of individual steps
        /// </summary>
        public List<StepResult> StepResults { get; set; } = new List<StepResult>();

        /// <summary>
        /// Any error that occurred during test execution
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Start time of the test
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// End time of the test
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Reference to the original test scenario
        /// </summary>
        [JsonIgnore]
        public TestScenario? Scenario { get; set; }

        /// <summary>
        /// Creates a summary of the test result
        /// </summary>
        public TestResultSummary CreateSummary()
        {
            return new TestResultSummary
            {
                Name = Name,
                Success = Success,
                Duration = Duration,
                ExpectationCount = StepResults.Sum(s => s.ExpectationResults.Count),
                StepCount = StepResults.Count,
                FailedExpectationCount = StepResults.Sum(s =>
                    s.ExpectationResults.Count(e => !e.Success)
                ),
                StartTime = StartTime,
                EndTime = EndTime,
                ErrorMessage = ErrorMessage,
            };
        }
    }

    /// <summary>
    /// Summarized test result for reporting
    /// </summary>
    public class TestResultSummary
    {
        /// <summary>
        /// Name of the test scenario
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Whether the test passed
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Time taken to execute the test
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Total number of expectations checked
        /// </summary>
        public int ExpectationCount { get; set; }

        /// <summary>
        /// Number of steps executed
        /// </summary>
        public int StepCount { get; set; }

        /// <summary>
        /// Number of expectations that failed
        /// </summary>
        public int FailedExpectationCount { get; set; }

        /// <summary>
        /// Start time of the test
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// End time of the test
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Any error message
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
