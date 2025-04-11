namespace BeaconTester.Core.Models
{
    /// <summary>
    /// Represents a single test step with inputs and expectations
    /// </summary>
    public class TestStep
    {
        /// <summary>
        /// Name of the test step
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this step does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The inputs to send to Redis
        /// </summary>
        public List<TestInput> Inputs { get; set; } = new List<TestInput>();

        /// <summary>
        /// Delay in milliseconds after sending inputs before checking expectations.
        /// If specified, this takes precedence over DelayMultiplier.
        /// </summary>
        public int Delay { get; set; } = 0;

        /// <summary>
        /// Number of Beacon cycles to wait after sending inputs before checking expectations.
        /// This is used only if Delay is 0, and provides a more cycle-aware alternative.
        /// </summary>
        public int? DelayMultiplier { get; set; } = null;

        /// <summary>
        /// The expected outcomes to verify
        /// </summary>
        public List<TestExpectation> Expectations { get; set; } = new List<TestExpectation>();

        /// <summary>
        /// Result of executing this step
        /// </summary>
        public StepResult? Result { get; set; }
    }

    /// <summary>
    /// Result of executing a single test step
    /// </summary>
    public class StepResult
    {
        /// <summary>
        /// Whether the step passed (all expectations met)
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Time taken to execute the step
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Results of individual expectations
        /// </summary>
        public List<ExpectationResult> ExpectationResults { get; set; } =
            new List<ExpectationResult>();

        /// <summary>
        /// Any error message if step execution failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Result of evaluating a single expectation
    /// </summary>
    public class ExpectationResult
    {
        /// <summary>
        /// The key that was checked
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Whether the expectation was met
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The expected value
        /// </summary>
        public object? Expected { get; set; }

        /// <summary>
        /// The actual value received
        /// </summary>
        public object? Actual { get; set; }

        /// <summary>
        /// Detailed comparison information
        /// </summary>
        public string? Details { get; set; }
    }
}
