using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BeaconTester.Core.Models
{
    /// <summary>
    /// Represents a complete test scenario with multiple steps
    /// </summary>
    public class TestScenario
    {
        /// <summary>
        /// Name of the test scenario
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what the test scenario is verifying
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Optional outputs to preset before running the test (for testing dependent rules)
        /// </summary>
        [JsonPropertyName("preSetOutputs")]
        public Dictionary<string, object>? PreSetOutputs { get; set; }
        
        /// <summary>
        /// Whether to clear existing output values before running the test
        /// Default is true for backwards compatibility
        /// </summary>
        [JsonPropertyName("clearOutputs")]
        public bool ClearOutputs { get; set; } = true;

        /// <summary>
        /// The steps to execute in sequence
        /// </summary>
        public List<TestStep> Steps { get; set; } = new List<TestStep>();

        /// <summary>
        /// Optional single set of inputs to apply (convenience for simple tests)
        /// </summary>
        public Dictionary<string, object>? Inputs { get; set; }

        /// <summary>
        /// Optional sequence of inputs to apply with delays (for testing temporal rules)
        /// </summary>
        [JsonPropertyName("inputSequence")]
        public List<SequenceInput>? InputSequence { get; set; }

        /// <summary>
        /// Optional outputs expected after all inputs are processed
        /// </summary>
        [JsonPropertyName("expectedOutputs")]
        public Dictionary<string, object>? ExpectedOutputs { get; set; }

        /// <summary>
        /// Optional tolerance for numeric comparisons
        /// </summary>
        public double? Tolerance { get; set; }
        
        /// <summary>
        /// Global timeout multiplier for all expectations in this scenario
        /// Useful for running tests on slower systems
        /// </summary>
        [JsonPropertyName("timeoutMultiplier")]
        public double TimeoutMultiplier { get; set; } = 1.0;

        /// <summary>
        /// The results of executing this test
        /// </summary>
        [JsonIgnore]
        public TestResult? Result { get; set; }

        /// <summary>
        /// Generates test steps from simplified inputs if needed
        /// </summary>
        public void NormalizeScenario()
        {
            // If we have simplified inputs and no steps, convert to a single step test
            if (
                (Inputs != null || InputSequence != null)
                && Steps.Count == 0
                && ExpectedOutputs != null
            )
            {
                if (Inputs != null)
                {
                    // Create a single step with the inputs and expectations
                    var step = new TestStep
                    {
                        Name = "Auto-generated step",
                        Inputs = Inputs
                            .Select(i => new TestInput { Key = i.Key, Value = i.Value })
                            .ToList(),
                        Expectations = ExpectedOutputs
                            .Select(e => new TestExpectation
                            {
                                Key = e.Key,
                                Expected = e.Value,
                                Tolerance = Tolerance,
                            })
                            .ToList(),
                        Delay =
                            500 // Default delay
                        ,
                    };
                    Steps.Add(step);
                }
                else if (InputSequence != null)
                {
                    // Create steps from the sequence
                    int stepCounter = 1;
                    foreach (var seqInput in InputSequence)
                    {
                        var step = new TestStep
                        {
                            Name = $"Auto-generated sequence step {stepCounter++}",
                            Inputs = seqInput
                                .Inputs.Select(i => new TestInput { Key = i.Key, Value = i.Value })
                                .ToList(),
                            Delay = seqInput.DelayMs,
                        };

                        // Only add expectations to the final step
                        if (stepCounter > InputSequence.Count)
                        {
                            step.Expectations = ExpectedOutputs
                                .Select(e => new TestExpectation
                                {
                                    Key = e.Key,
                                    Expected = e.Value,
                                    Tolerance = Tolerance,
                                })
                                .ToList();
                        }

                        Steps.Add(step);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Input for a sequence step
    /// </summary>
    public class SequenceInput
    {
        /// <summary>
        /// The inputs for this step in the sequence
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, object> Inputs { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Delay in milliseconds before the next step
        /// </summary>
        [JsonPropertyName("delayMs")]
        public int DelayMs { get; set; } = 100;

        /// <summary>
        /// Allows additional properties to be added dynamically
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object> AdditionalInputs { get; set; } =
            new Dictionary<string, object>();

        /// <summary>
        /// Maps additional properties to inputs
        /// </summary>
        [OnDeserialized]
        internal void OnDeserialized(StreamingContext context)
        {
            foreach (var kvp in AdditionalInputs)
            {
                if (!kvp.Key.Equals("delayMs", StringComparison.OrdinalIgnoreCase))
                {
                    Inputs[kvp.Key] = kvp.Value;
                }
            }
        }
    }
}
