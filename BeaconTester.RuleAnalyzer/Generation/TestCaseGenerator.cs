using BeaconTester.Core.Models;
using BeaconTester.Core.Validation;
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
        private readonly ExpressionEvaluator _expressionEvaluator;

        /// <summary>
        /// Creates a new test case generator
        /// </summary>
        public TestCaseGenerator(ILogger logger)
        {
            _logger = logger.ForContext<TestCaseGenerator>();
            _conditionAnalyzer = new ConditionAnalyzer(logger);
            _valueGenerator = new ValueGenerator(logger);
            _expressionEvaluator = new ExpressionEvaluator(logger);
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
                            // Pass the generated inputs to the output value determination
                            // so expressions can be evaluated with the actual test values
                            var value = DetermineOutputValue(setValueAction, testCase.Inputs);
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

                // For negative tests we do not set any expectations
                // This is because in a rule system with latching behavior:
                // 1. Previous values may persist when a rule doesn't execute
                // 2. We cannot reliably determine from rule inspection alone how outputs should behave
                //    when a rule doesn't run (depends on architecture and implementation choices)
                // 3. To test latching behavior properly, we should use explicit preSetOutputs in scenarios
                
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
        /// Evaluates a string template expression directly
        /// </summary>
        private string? EvaluateStringTemplate(string expression, Dictionary<string, object?> inputs)
        {
            try
            {
                string result = "";
                var parts = expression.Split('+');
                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();
                    
                    // Handle string literals
                    if (trimmedPart.StartsWith("\"") && trimmedPart.EndsWith("\""))
                    {
                        result += trimmedPart.Substring(1, trimmedPart.Length - 2);
                    }
                    // Handle input references
                    else if (trimmedPart.StartsWith("input:"))
                    {
                        var key = trimmedPart.Trim();
                        if (inputs.TryGetValue(key, out var value))
                        {
                            result += value?.ToString() ?? "null";
                        }
                        else
                        {
                            // Use a default value based on the sensor name
                            if (key == "input:temperature")
                                result += "35";
                            else if (key == "input:humidity") 
                                result += "75";
                            else
                                result += "42"; // Generic default
                        }
                    }
                    else
                    {
                        result += trimmedPart;
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error evaluating string template: {Expression}", expression);
                return null;
            }
        }
        
        /// <summary>
        /// Determines the expected output value from an action
        /// </summary>
        private object DetermineOutputValue(SetValueAction action, Dictionary<string, object>? inputValues = null)
        {
            // If a static value is provided, use that
            if (action.Value != null)
            {
                // Ensure the value is of the correct type - true should be boolean, not string
                if (action.Value is string valueStr)
                {
                    if (valueStr.Equals("true", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (valueStr.Equals("false", StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (double.TryParse(valueStr, out double numVal))
                        return numVal;
                }
                return action.Value;
            }

            // If a value expression is provided, try to evaluate it
            if (!string.IsNullOrEmpty(action.ValueExpression))
            {
                var expression = action.ValueExpression;

                // Handle simple expressions directly
                if (expression.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (expression.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (expression.Trim().Equals("now()", StringComparison.OrdinalIgnoreCase))
                    return DateTime.UtcNow.ToString("o");

                try
                {
                    // Set up test input values to use with our expression evaluator
                    var inputs = new Dictionary<string, object?>();
                    
                    // Use provided input values if available
                    if (inputValues != null)
                    {
                        foreach (var kvp in inputValues)
                        {
                            inputs[kvp.Key] = kvp.Value;
                        }
                    }
                    else
                    {
                        // If no inputs provided, use sensible defaults for known key patterns
                        // These are based on common sensor names and ranges
                        inputs["input:temperature"] = 25.0;  // Room temperature
                        inputs["input:humidity"] = 50.0;     // Medium humidity
                        inputs["input:pressure"] = 1013.0;   // Standard atmospheric pressure
                        inputs["input:battery"] = 80.0;      // Good battery level
                        inputs["input:light"] = 500.0;       // Medium light level
                        inputs["input:motion"] = true;       // Motion detected
                        inputs["input:status"] = "active";   // Active status
                        inputs["input:level"] = 75.0;        // Level at 75%
                        inputs["input:count"] = 5;           // Count of 5 items
                    }

                    // Check if it's a string template expression and handle specially
                    if (expression.Contains("\"") && expression.Contains("+"))
                    {
                        // Make sure we're using the input values from the test case if available
                        Dictionary<string, object?> templateInputs = new Dictionary<string, object?>(inputs);
                        if (inputValues != null)
                        {
                            foreach (var kvp in inputValues)
                            {
                                templateInputs[kvp.Key] = kvp.Value;
                            }
                        }
                        
                        var stringResult = EvaluateStringTemplate(expression, templateInputs);
                        if (stringResult != null)
                        {
                            _logger.Debug("Successfully evaluated string template '{Expression}' to {Result}", expression, stringResult);
                            return stringResult;
                        }
                    }
                    else
                    {
                        // For non-string templates, use the expression evaluator
                        var result = _expressionEvaluator.EvaluateAsync(expression, inputs).GetAwaiter().GetResult();
                        if (result != null)
                        {
                            _logger.Debug("Successfully evaluated expression '{Expression}' to {Result}", expression, result);
                            return result;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error evaluating expression {Expression}, falling back to defaults", expression);
                }

                // If we couldn't evaluate the expression, fall back to reasonable defaults
                // based on the expression pattern
                
                // For expressions that look like math operations
                if (expression.Contains("+") || expression.Contains("-") || 
                    expression.Contains("*") || expression.Contains("/"))
                {
                    return 42.0; // A more interesting default than 10.0
                }
            }

            // For boolean output keys, default to true
            if (action.Key.EndsWith("_enabled") || action.Key.EndsWith("_status") || 
                action.Key.EndsWith("_active") || action.Key.EndsWith("_alert") ||
                action.Key.EndsWith("_alarm") || action.Key.EndsWith("_normal"))
            {
                return true;
            }

            // Default to a reasonable numeric value for generic outputs
            return 50.0;
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