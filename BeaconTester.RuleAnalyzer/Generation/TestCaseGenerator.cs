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
        
        // Store all comparison and threshold conditions from parsed rules to derive appropriate test values
        private List<ComparisonCondition> _comparisonConditions = new List<ComparisonCondition>();
        private List<ThresholdOverTimeCondition> _thresholdConditions = new List<ThresholdOverTimeCondition>();

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
        /// Collect all conditions from a rule for domain-agnostic test value generation
        /// </summary>
        private void CollectConditionsFromRule(RuleDefinition rule)
        {
            if (rule.Conditions == null) return;
            
            // Process the condition hierarchy recursively
            ProcessConditionDefinition(rule.Conditions);
            
            _logger.Debug("Collected {ComparisonCount} comparison conditions and {ThresholdCount} threshold conditions from rule {RuleName}",
                _comparisonConditions.Count, _thresholdConditions.Count, rule.Name);
        }
        
        /// <summary>
        /// Process a condition definition recursively to collect all conditions
        /// </summary>
        private void ProcessConditionDefinition(ConditionDefinition conditionDef)
        {
            // Handle different types of conditions
            if (conditionDef is ComparisonCondition comparison)
            {
                // Collect comparison condition
                _comparisonConditions.Add(comparison);
            }
            else if (conditionDef is ThresholdOverTimeCondition threshold)
            {
                // Collect threshold condition
                _thresholdConditions.Add(threshold);
            }
            else if (conditionDef is ConditionGroup group)
            {
                // Process all conditions in 'all' list
                foreach (var wrapper in group.All)
                {
                    if (wrapper.Condition != null)
                    {
                        ProcessConditionDefinition(wrapper.Condition);
                    }
                }
                
                // Process all conditions in 'any' list
                foreach (var wrapper in group.Any)
                {
                    if (wrapper.Condition != null)
                    {
                        ProcessConditionDefinition(wrapper.Condition);
                    }
                }
            }
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
                
                // Collect conditions for domain-agnostic test value derivation
                CollectConditionsFromRule(rule);

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
        /// <param name="condition">The condition to use for value generation. Can be null if no specific condition is known.</param>
        /// <param name="target">Whether to generate a value that satisfies or fails the condition</param>
        /// <returns>An appropriate value for the sensor</returns>
        public object GenerateValueForSensor(string sensorName, ConditionDefinition? condition, ValueTarget target)
        {
            try
            {
                // Handle null condition
                if (condition == null)
                {
                    // If no condition is provided, delegate to the ValueGenerator
                    // with a reasonable default that should work in most cases
                    // This is a fallback only when we can't find relevant conditions in the rules
                    return 42.0; // A reasonable default value that's easy to identify
                }
                
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
                        // For threshold_over_time with operator >, generate increasing values
                        // that cross the threshold and stay above it
                        double baseValue = temporal.Threshold;
                        string comparisonOperator = temporal.Operator ?? ">";
                        
                        for (int i = 0; i < steps; i++)
                        {
                            var value = _valueGenerator.GenerateValueForTemporalCondition(
                                temporal,
                                i,
                                steps,
                                ValueTarget.Positive
                            );
                            
                            // Make sure values consistently satisfy the condition over time
                            // by ensuring a proper trend (rising for '>' operators, etc.)
                            switch (comparisonOperator.ToLowerInvariant())
                            {
                                case "greater_than":
                                case ">":
                                    // For '>' operator, ensure values rise and stay above threshold
                                    value = baseValue + 5 + (i * 2);
                                    break;
                                case "less_than":
                                case "<":
                                    // For '<' operator, ensure values decrease and stay below threshold
                                    value = baseValue - 5 - (i * 2);
                                    break;
                                default:
                                    // For other operators, ensure consistent values that satisfy the condition
                                    if (i == 0) value = baseValue;
                                    break;
                            }

                            var sequenceInput = new SequenceInput { DelayMs = duration / steps };
                            sequenceInput.Inputs[sensor] = value;
                            inputSequence.Add(sequenceInput);
                            
                            _logger.Debug("Generated temporal step {Step}/{TotalSteps} with value {Value} for condition {Condition}",
                                i+1, steps, value, $"{sensor} {comparisonOperator} {baseValue}");
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
        /// <param name="action">The action to determine the output value for</param>
        /// <param name="inputValues">Dictionary of input values to use when evaluating expressions</param>
        /// <returns>The expected output value</returns>
        public object DetermineOutputValue(SetValueAction action, Dictionary<string, object>? inputValues = null)
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
                        // Extract needed inputs from the expression in a domain-agnostic way
                        var neededInputs = new HashSet<string>();
                        
                        // Find potential input:xxx patterns in the expression
                        if (!string.IsNullOrEmpty(expression))
                        {
                            var matches = System.Text.RegularExpressions.Regex.Matches(
                                expression, "input:[a-zA-Z0-9_]+");
                            
                            foreach (System.Text.RegularExpressions.Match match in matches)
                            {
                                neededInputs.Add(match.Value);
                            }
                        }
                        
                        // Look up sensible values based on actual rule conditions
                        foreach (var sensorName in neededInputs)
                        {
                            // Find the condition that uses this sensor (if any)
                            // Try comparison conditions first, then threshold conditions
                            ComparisonCondition? comparisonCondition = _comparisonConditions
                                .FirstOrDefault(c => c.Sensor == sensorName);
                            
                            ThresholdOverTimeCondition? thresholdCondition = _thresholdConditions
                                .FirstOrDefault(c => c.Sensor == sensorName);
                            
                            // Use the appropriate condition to derive test values in a domain-agnostic way
                            if (comparisonCondition != null)
                            {
                                // For comparison conditions, derive value based on operator and threshold
                                if (comparisonCondition.Value != null)
                                {
                                    // Extract type information to determine how to generate values
                                    var type = comparisonCondition.Value.GetType();
                                    var isNumeric = type == typeof(int) || type == typeof(double) || type == typeof(float);
                                    var isBoolean = type == typeof(bool);
                                    var isString = type == typeof(string);
                                    
                                    if (comparisonCondition.Operator == "greater_than" && isNumeric)
                                    {
                                        // Use value slightly above threshold for 'greater than'
                                        inputs[sensorName] = Convert.ToDouble(comparisonCondition.Value) + 1;
                                    }
                                    else if (comparisonCondition.Operator == "less_than" && isNumeric)
                                    {
                                        // Use value slightly below threshold for 'less than'
                                        inputs[sensorName] = Math.Max(0, Convert.ToDouble(comparisonCondition.Value) - 1);
                                    }
                                    else if (comparisonCondition.Operator == "equal_to")
                                    {
                                        // For equality, use exact match with correct type preservation
                                        inputs[sensorName] = comparisonCondition.Value;
                                    }
                                    else if (comparisonCondition.Operator == "not_equal_to" && isBoolean)
                                    {
                                        // For boolean not equal, use opposite value
                                        inputs[sensorName] = !(bool)comparisonCondition.Value;
                                    }
                                    else
                                    {
                                        // Default fallback - use the condition value itself
                                        inputs[sensorName] = comparisonCondition.Value;
                                    }
                                }
                            }
                            else if (thresholdCondition != null)
                            {
                                // For temporal conditions, use value that satisfies the threshold
                                if (thresholdCondition.Threshold != null)
                                {
                                    // Extract threshold value in a domain-agnostic way
                                    // For rate/threshold conditions, we need a value that exceeds the threshold
                                    double thresholdValue = 0;
                                    
                                    // Handle any threshold type by using string conversion as an intermediary
                                    string thresholdStr = thresholdCondition.Threshold.ToString();
                                    if (double.TryParse(thresholdStr, out double parsed))
                                    {
                                        thresholdValue = parsed;
                                    }
                                    
                                    // Add a buffer to the threshold to ensure the condition triggers
                                    inputs[sensorName] = thresholdValue + 1;
                                }
                            }
                            else
                            {
                                // No condition found for this input
                                // Use a conservative default - ONLY as a last resort
                                // Type is inferred from expression context
                                if (expression.Contains(sensorName + " == true") || 
                                    expression.Contains(sensorName + " && "))
                                {
                                    inputs[sensorName] = true;  // Boolean context
                                }
                                else if (expression.Contains(sensorName + " + ") || 
                                         expression.Contains(sensorName + " - ") || 
                                         expression.Contains(sensorName + " * ") || 
                                         expression.Contains(sensorName + " / "))
                                {
                                    inputs[sensorName] = 10.0;  // Numeric context
                                }
                                else
                                {
                                    // String context likely
                                    inputs[sensorName] = "value";
                                }
                            }
                        }
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