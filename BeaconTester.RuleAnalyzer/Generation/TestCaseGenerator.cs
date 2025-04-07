using BeaconTester.Core.Models;
using BeaconTester.RuleAnalyzer.Analysis;
using BeaconTester.RuleAnalyzer.Parsing;
using Serilog;

namespace BeaconTester.RuleAnalyzer.Generation
{
    /// <summary>
    /// Generates test cases from rule definitions
    /// </summary>
    public class TestCaseGenerator
    {
        private readonly ILogger _logger;
        private readonly ConditionAnalyzer _conditionAnalyzer;
        private readonly ValueGenerator _valueGenerator;

        /// <summary>
        /// Creates a new test case generator
        /// </summary>
        public TestCaseGenerator(ILogger logger)
        {
            _logger = logger.ForContext<TestCaseGenerator>();
            _conditionAnalyzer = new ConditionAnalyzer(logger);
            _valueGenerator = new ValueGenerator(logger);
        }

        /// <summary>
        /// Generates a basic test case for a rule
        /// </summary>
        public TestCase GenerateBasicTestCase(RuleDefinition rule)
        {
            _logger.Debug("Generating basic test case for rule: {RuleName}", rule.Name);

            var testCase = new TestCase();

            try
            {
                if (rule.Conditions == null)
                {
                    _logger.Warning("Rule {RuleName} has no conditions", rule.Name);
                    return testCase;
                }

                // Extract all sensors from conditions
                var sensors = _conditionAnalyzer.ExtractSensors(rule.Conditions);

                // Generate appropriate input values for each sensor
                foreach (var sensor in sensors)
                {
                    if (sensor.StartsWith("input:"))
                    {
                        var value = _valueGenerator.GenerateValueForSensor(
                            rule,
                            sensor,
                            ValueTarget.Positive
                        );
                        testCase.Inputs[sensor] = value;
                    }
                }

                // Extract expected outputs from actions
                foreach (var action in rule.Actions)
                {
                    if (action is SetValueAction setValueAction)
                    {
                        if (setValueAction.Key.StartsWith("output:"))
                        {
                            var value = DetermineOutputValue(setValueAction);
                            testCase.Outputs[setValueAction.Key] = value;
                        }
                    }
                }

                return testCase;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Error generating basic test case for rule {RuleName}",
                    rule.Name
                );
                throw;
            }
        }
        
        /// <summary>
        /// Generates a test value for a sensor based on a condition
        /// </summary>
        /// <param name="sensorName">Name of the sensor to generate a value for</param>
        /// <param name="condition">The condition to use for value generation</param>
        /// <param name="target">Whether to generate a value that satisfies or fails the condition</param>
        /// <returns>An appropriate value for the sensor</returns>
        public object GenerateValueForSensor(string sensorName, ConditionDefinition condition, ValueTarget target)
        {
            try
            {
                if (condition is ComparisonCondition comparison && comparison.Sensor == sensorName)
                {
                    // Direct match - use the value generator to create an appropriate value based on the condition
                    return _valueGenerator.GenerateValueForCondition(comparison, target);
                }
                else if (condition is ThresholdOverTimeCondition temporal && temporal.Sensor == sensorName)
                {
                    // Temporal condition - use the value generator to create an appropriate value
                    return _valueGenerator.GenerateValueForTemporalCondition(temporal, 0, 1, target);
                }
                else if (condition is ConditionGroup group)
                {
                    // For condition groups, recursively search for a matching condition
                    var result = FindConditionForSensor(group, sensorName);
                    if (result != null)
                    {
                        if (result is ComparisonCondition foundComparison)
                        {
                            return _valueGenerator.GenerateValueForCondition(foundComparison, target);
                        }
                        else if (result is ThresholdOverTimeCondition foundTemporal)
                        {
                            return _valueGenerator.GenerateValueForTemporalCondition(foundTemporal, 0, 1, target);
                        }
                    }
                }
                
                // If we can't find a specific condition for this sensor or can't determine a good value,
                // use a generic value that's unlikely to trigger edge conditions
                _logger.Debug("Could not find a specific condition for sensor {Sensor}, using generic value", sensorName);
                return target == ValueTarget.Positive ? 50.0 : 0.0;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error generating value for sensor {Sensor}, using fallback value", sensorName);
                return target == ValueTarget.Positive ? 50.0 : 0.0;
            }
        }
        
        /// <summary>
        /// Finds a condition that references a specific sensor in a condition group
        /// </summary>
        private ConditionDefinition? FindConditionForSensor(ConditionGroup group, string sensorName)
        {
            // First check 'all' conditions
            foreach (var wrapper in group.All)
            {
                if (wrapper.Condition == null)
                    continue;
                    
                if (wrapper.Condition is ComparisonCondition comparison && comparison.Sensor == sensorName)
                {
                    return comparison;
                }
                else if (wrapper.Condition is ThresholdOverTimeCondition temporal && temporal.Sensor == sensorName)
                {
                    return temporal;
                }
                else if (wrapper.Condition is ConditionGroup nestedGroup)
                {
                    var result = FindConditionForSensor(nestedGroup, sensorName);
                    if (result != null)
                        return result;
                }
            }
            
            // Then check 'any' conditions
            foreach (var wrapper in group.Any)
            {
                if (wrapper.Condition == null)
                    continue;
                    
                if (wrapper.Condition is ComparisonCondition comparison && comparison.Sensor == sensorName)
                {
                    return comparison;
                }
                else if (wrapper.Condition is ThresholdOverTimeCondition temporal && temporal.Sensor == sensorName)
                {
                    return temporal;
                }
                else if (wrapper.Condition is ConditionGroup nestedGroup)
                {
                    var result = FindConditionForSensor(nestedGroup, sensorName);
                    if (result != null)
                        return result;
                }
            }
            
            return null;
        }

        /// <summary>
        /// Generates a negative test case for a rule
        /// </summary>
        public TestCase GenerateNegativeTestCase(RuleDefinition rule)
        {
            _logger.Debug("Generating negative test case for rule: {RuleName}", rule.Name);

            var testCase = new TestCase();

            try
            {
                if (rule.Conditions == null)
                {
                    _logger.Warning("Rule {RuleName} has no conditions", rule.Name);
                    return testCase;
                }

                // Extract all sensors from conditions
                var sensors = _conditionAnalyzer.ExtractSensors(rule.Conditions);

                // Generate input values that won't satisfy the conditions
                foreach (var sensor in sensors)
                {
                    if (sensor.StartsWith("input:"))
                    {
                        var value = _valueGenerator.GenerateValueForSensor(
                            rule,
                            sensor,
                            ValueTarget.Negative
                        );
                        testCase.Inputs[sensor] = value;
                    }
                }

                // For expected outputs, the rule shouldn't trigger
                foreach (var action in rule.Actions)
                {
                    if (action is SetValueAction setValueAction)
                    {
                        if (setValueAction.Key.StartsWith("output:"))
                        {
                            // For boolean outputs, expect the opposite of the positive case
                            var positiveValue = DetermineOutputValue(setValueAction);

                            if (positiveValue is bool boolValue)
                            {
                                testCase.Outputs[setValueAction.Key] = !boolValue;
                            }
                            else
                            {
                                // For non-boolean outputs, we need a sensible negative expectation
                                // For key types we know about, we can make better guesses
                                if (setValueAction.Key.Contains("high") || 
                                    setValueAction.Key.Contains("normal") ||
                                    setValueAction.Key.Contains("alert") ||
                                    setValueAction.Key.Contains("alarm") ||
                                    setValueAction.Key.Contains("enabled") ||
                                    setValueAction.Key.Contains("active") ||
                                    setValueAction.Key.Contains("detected"))
                                {
                                    // For boolean-looking outputs, expect false in negative case
                                    testCase.Outputs[setValueAction.Key] = false;
                                }
                                else
                                {
                                    // Otherwise use null (not set)
                                    testCase.Outputs[setValueAction.Key] = null;
                                }
                            }
                        }
                    }
                }

                return testCase;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Error generating negative test case for rule {RuleName}",
                    rule.Name
                );
                throw;
            }
        }

        /// <summary>
        /// Generates a temporal test case for a rule with temporal conditions
        /// </summary>
        public TestScenario? GenerateTemporalTestCase(RuleDefinition rule)
        {
            _logger.Debug("Generating temporal test case for rule: {RuleName}", rule.Name);

            try
            {
                if (rule.Conditions == null)
                {
                    _logger.Warning("Rule {RuleName} has no conditions", rule.Name);
                    return null;
                }

                // Find temporal conditions
                var temporalConditions = FindTemporalConditions(rule.Conditions);

                if (temporalConditions.Count == 0)
                {
                    _logger.Warning("Rule {RuleName} has no temporal conditions", rule.Name);
                    return null;
                }

                // Create a new test scenario
                var scenario = new TestScenario
                {
                    Name = $"{rule.Name}TemporalTest",
                    Description = $"Temporal test for rule {rule.Name}: {rule.Description}",
                };

                // Generate a sequence of inputs for each temporal condition
                var inputSequence = new List<SequenceInput>();

                foreach (var condition in temporalConditions)
                {
                    if (condition is ThresholdOverTimeCondition temporal)
                    {
                        // Get appropriate values for this condition
                        var sensor = temporal.Sensor;
                        var threshold = temporal.Threshold;
                        var duration = temporal.Duration;
                        var steps = Math.Max(3, duration / 500); // At least 3 steps, or more for longer durations

                        // Generate values that will satisfy the condition
                        for (int i = 0; i < steps; i++)
                        {
                            var value = _valueGenerator.GenerateValueForTemporalCondition(
                                temporal,
                                i,
                                steps,
                                ValueTarget.Positive
                            );

                            var sequenceInput = new SequenceInput { DelayMs = duration / steps };

                            sequenceInput.Inputs[sensor] = value;
                            inputSequence.Add(sequenceInput);
                        }
                    }
                }

                // Add the sequence to the scenario
                scenario.InputSequence = inputSequence;

                // Add expected outputs
                var expectedOutputs = new Dictionary<string, object>();

                foreach (var action in rule.Actions)
                {
                    if (action is SetValueAction setValueAction)
                    {
                        if (setValueAction.Key.StartsWith("output:"))
                        {
                            var value = DetermineOutputValue(setValueAction);
                            expectedOutputs[setValueAction.Key] = value;
                        }
                    }
                }

                scenario.ExpectedOutputs = expectedOutputs;

                return scenario;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Error generating temporal test case for rule {RuleName}",
                    rule.Name
                );
                return null;
            }
        }

        /// <summary>
        /// Finds all temporal conditions in a rule
        /// </summary>
        private List<ThresholdOverTimeCondition> FindTemporalConditions(
            ConditionDefinition condition
        )
        {
            var temporalConditions = new List<ThresholdOverTimeCondition>();

            if (condition is ThresholdOverTimeCondition temporal)
            {
                temporalConditions.Add(temporal);
            }
            else if (condition is ConditionGroup group)
            {
                // Process 'all' conditions
                foreach (var wrapper in group.All)
                {
                    if (wrapper.Condition != null)
                    {
                        temporalConditions.AddRange(FindTemporalConditions(wrapper.Condition));
                    }
                }

                // Process 'any' conditions
                foreach (var wrapper in group.Any)
                {
                    if (wrapper.Condition != null)
                    {
                        temporalConditions.AddRange(FindTemporalConditions(wrapper.Condition));
                    }
                }
            }

            return temporalConditions;
        }

        /// <summary>
        /// Determines the expected output value from an action
        /// </summary>
        private object DetermineOutputValue(SetValueAction action)
        {
            // If a static value is provided, use that
            if (action.Value != null)
            {
                return action.Value;
            }

            // If a value expression is provided, try to evaluate it
            if (!string.IsNullOrEmpty(action.ValueExpression))
            {
                var expression = action.ValueExpression.ToLowerInvariant();

                // Handle simple expressions
                if (expression == "true")
                    return true;
                if (expression == "false")
                    return false;
                if (expression == "now()")
                    return DateTime.UtcNow.ToString("o");

                // For more complex expressions, make a reasonable guess
                if (expression.Contains("input:"))
                {
                    // If it's directly setting the value of an input, use a default value
                    if (expression.Trim() == "input:temperature")
                        return 25.0;
                    if (expression.Trim() == "input:humidity")
                        return 50.0;
                    if (expression.Trim() == "input:pressure")
                        return 1013.0;
                }

                // For mathematical expressions involving standard inputs, make a realistic estimation
                if (
                    expression.Contains("+")
                    || expression.Contains("-")
                    || expression.Contains("*")
                    || expression.Contains("/")
                )
                {
                    // We shouldn't try to parse complex expressions here - that would require a
                    // full expression evaluator. Instead, return a reasonable numeric value
                    // that's closer to what most formulas might generate.
                    return 10.0; // More reasonable default than 42.0
                }
            }

            // Default to true for outputs with certain names
            if (
                action.Key.Contains("alert")
                || action.Key.Contains("alarm")
                || action.Key.Contains("enabled")
                || action.Key.Contains("active")
                || action.Key.Contains("detected")
            )
            {
                return true;
            }

            // Default to a string value
            return "test_value";
        }
    }

    /// <summary>
    /// Represents a generated test case
    /// </summary>
    public class TestCase
    {
        /// <summary>
        /// Input values to set
        /// </summary>
        public Dictionary<string, object> Inputs { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Expected output values
        /// </summary>
        public Dictionary<string, object?> Outputs { get; set; } =
            new Dictionary<string, object?>();
    }
}
