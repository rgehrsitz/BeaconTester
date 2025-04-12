using BeaconTester.RuleAnalyzer.Analysis;
using BeaconTester.RuleAnalyzer.Parsing;
using Serilog;

namespace BeaconTester.RuleAnalyzer.Generation
{
    /// <summary>
    /// Target for value generation
    /// </summary>
    public enum ValueTarget
    {
        /// <summary>
        /// Generate values that satisfy conditions
        /// </summary>
        Positive,

        /// <summary>
        /// Generate values that don't satisfy conditions
        /// </summary>
        Negative,
    }

    /// <summary>
    /// Generates test values for conditions
    /// </summary>
    public class ValueGenerator
    {
        private readonly ILogger _logger;
        private readonly Random _random = new Random();

        /// <summary>
        /// Creates a new value generator
        /// </summary>
        public ValueGenerator(ILogger logger)
        {
            _logger = logger.ForContext<ValueGenerator>();
        }

        /// <summary>
        /// Generates a value for a sensor based on rule conditions
        /// </summary>
        public object GenerateValueForSensor(RuleDefinition rule, string sensor, ValueTarget target)
        {
            // Find all conditions that use this sensor
            var conditions =
                rule.Conditions != null
                    ? FindConditionsForSensor(rule.Conditions, sensor)
                    : new List<ConditionDefinition>();

            if (conditions.Count == 0)
            {
                _logger.Debug(
                    "No conditions found for sensor {Sensor} in rule {RuleName}",
                    sensor,
                    rule.Name
                );

                // Generate a default value based on sensor name
                return GenerateDefaultValue(sensor);
            }

            // For multiple conditions, we need a value that satisfies all of them
            // (or none of them for negative tests)

            if (conditions.Count == 1)
            {
                // Simple case - just one condition
                return GenerateValueForCondition(conditions[0], target);
            }

            // For multiple conditions, it gets more complex
            // We'll first try to find numerical conditions with compatible ranges
            var numericConditions = conditions.OfType<ComparisonCondition>().ToList();

            if (numericConditions.Count > 0)
            {
                return GenerateValueForMultipleConditions(numericConditions, target);
            }

            // If we can't handle the conditions easily, just use the first one
            return GenerateValueForCondition(conditions[0], target);
        }

        /// <summary>
        /// Generates a value for a temporal condition
        /// </summary>
        public object GenerateValueForTemporalCondition(
            ThresholdOverTimeCondition condition,
            int step,
            int totalSteps,
            ValueTarget target
        )
        {
            var threshold = condition.Threshold;
            var comparisonOperator = condition.Operator?.ToLowerInvariant() ?? ">";
            
            // Normalize operator
            if (comparisonOperator == "greater_than") comparisonOperator = ">";
            else if (comparisonOperator == "less_than") comparisonOperator = "<";
            else if (comparisonOperator == "greater_than_or_equal_to") comparisonOperator = ">=";
            else if (comparisonOperator == "less_than_or_equal_to") comparisonOperator = "<=";
            else if (comparisonOperator == "equal_to") comparisonOperator = "==";
            else if (comparisonOperator == "not_equal_to") comparisonOperator = "!=";

            _logger.Debug("Generating temporal value for step {Step}/{TotalSteps}, target: {Target}, operator: {Operator}, threshold: {Threshold}",
                step, totalSteps, target, comparisonOperator, threshold);

            if (target == ValueTarget.Positive)
            {
                // For positive cases (should trigger), we need to exceed/meet the threshold
                // consistently throughout the time window
                switch (comparisonOperator)
                {
                    case ">":
                    case "gt":
                        // Ensure ALL values in sequence exceed threshold by an increasing margin
                        // First value should already be above threshold by enough to matter
                        double margin = Math.Max(threshold * 0.1, 5); // At least 5 or 10% of threshold
                        return threshold + margin + (step * 3); // Increasing trend

                    case ">=":
                    case "gte":
                        // Ensure ALL values in sequence are at least the threshold
                        // Start exactly at threshold, then increase
                        return threshold + (step * 2);

                    case "<":
                    case "lt":
                        // Ensure ALL values in sequence are below threshold by a decreasing margin
                        margin = Math.Max(threshold * 0.1, 5);
                        return threshold - margin - (step * 3); // Decreasing trend

                    case "<=":
                    case "lte":
                        // Ensure ALL values in sequence are at most the threshold
                        // Start exactly at threshold, then decrease
                        return threshold - (step * 2);

                    case "==":
                    case "eq":
                        // All values must equal threshold for exact match
                        return threshold;

                    case "!=":
                    case "ne":
                    case "neq":
                        // All values must not equal threshold
                        return threshold + 10 + (step * 2);

                    default:
                        // Default to increasing above threshold
                        return threshold + 10 + (step * 2);
                }
            }
            else
            {
                // For negative cases (shouldn't trigger), we need at least some values
                // that FAIL to meet the condition during the time window
                // For simplicity, we'll make values oscillate above/below threshold
                bool shouldBeAbove = (step % 2 == 0);
                
                switch (comparisonOperator)
                {
                    case ">":
                    case "gt":
                        // Need some values below threshold to prevent triggering
                        return shouldBeAbove ? threshold + 5 : threshold - 5;

                    case ">=":
                    case "gte":
                        // Need some values below threshold to prevent triggering
                        return shouldBeAbove ? threshold + 3 : threshold - 3;

                    case "<":
                    case "lt":
                        // Need some values above threshold to prevent triggering
                        return shouldBeAbove ? threshold + 5 : threshold - 5;

                    case "<=":
                    case "lte":
                        // Need some values above threshold to prevent triggering
                        return shouldBeAbove ? threshold + 3 : threshold - 3;

                    case "==":
                    case "eq":
                        // Need some values not equal to threshold
                        return shouldBeAbove ? threshold + 5 : threshold - 5;

                    case "!=":
                    case "ne":
                    case "neq":
                        // Need some values equal to threshold
                        return shouldBeAbove ? threshold + 5 : threshold;

                    default:
                        // Default to oscillating around threshold
                        return shouldBeAbove ? threshold + 5 : threshold - 5;
                }
            }
        }

        /// <summary>
        /// Finds all conditions that reference a specific sensor
        /// </summary>
        private List<ConditionDefinition> FindConditionsForSensor(
            ConditionDefinition condition,
            string sensor
        )
        {
            var matchingConditions = new List<ConditionDefinition>();

            if (condition is ComparisonCondition comparison)
            {
                if (comparison.Sensor == sensor)
                {
                    matchingConditions.Add(comparison);
                }
            }
            else if (condition is ThresholdOverTimeCondition temporal)
            {
                if (temporal.Sensor == sensor)
                {
                    matchingConditions.Add(temporal);
                }
            }
            else if (condition is ConditionGroup group)
            {
                // Process 'all' conditions
                foreach (var wrapper in group.All)
                {
                    if (wrapper.Condition != null)
                    {
                        matchingConditions.AddRange(FindConditionsForSensor(wrapper.Condition, sensor));
                    }
                }

                // Process 'any' conditions
                foreach (var wrapper in group.Any)
                {
                    if (wrapper.Condition != null)
                    {
                        matchingConditions.AddRange(FindConditionsForSensor(wrapper.Condition, sensor));
                    }
                }
            }

            return matchingConditions;
        }

        /// <summary>
        /// Generates a value for a specific condition
        /// </summary>
        public object GenerateValueForCondition(ConditionDefinition condition, ValueTarget target)
        {
            if (condition is ComparisonCondition comparison)
            {
                return GenerateValueForComparisonCondition(comparison, target);
            }
            else if (condition is ThresholdOverTimeCondition temporal)
            {
                return GenerateValueForTemporalCondition(temporal, 0, 1, target);
            }
            else if (condition is ExpressionCondition expression)
            {
                // For expression conditions, default to true for positive, false for negative
                return target == ValueTarget.Positive;
            }
            else
            {
                // Default values based on target
                return target == ValueTarget.Positive ? 42 : 0;
            }
        }

        /// <summary>
        /// Generates a value for a comparison condition
        /// </summary>
        private object GenerateValueForComparisonCondition(
            ComparisonCondition condition,
            ValueTarget target
        )
        {
            var valueObj = condition.Value;
            var comparisonOperator = condition.Operator.ToLowerInvariant();

            // Handle different value types
            if (valueObj is double doubleValue)
            {
                return GenerateNumericValue(doubleValue, comparisonOperator, target);
            }
            else if (valueObj is int intValue)
            {
                return GenerateNumericValue(intValue, comparisonOperator, target);
            }
            else if (valueObj is bool boolValue)
            {
                return target == ValueTarget.Positive ? boolValue : !boolValue;
            }
            else if (valueObj is string stringValue)
            {
                // Try to parse as number
                if (double.TryParse(stringValue, out double parsedValue))
                {
                    return GenerateNumericValue(parsedValue, comparisonOperator, target);
                }

                // Handle as string
                return target == ValueTarget.Positive ? stringValue : $"not_{stringValue}";
            }

            // Default case
            return target == ValueTarget.Positive ? 42 : 0;
        }

        /// <summary>
        /// Generates a numeric value for a condition
        /// </summary>
        private double GenerateNumericValue(
            double threshold,
            string comparisonOperator,
            ValueTarget target
        )
        {
            if (target == ValueTarget.Positive)
            {
                // Generate value that satisfies the condition
                switch (comparisonOperator)
                {
                    case ">":
                    case "gt":
                        // Use a value that's clearly above threshold to avoid boundary issues
                        return threshold + Math.Max(10, threshold * 0.1); // Add at least 10 or 10% of the threshold

                    case ">=":
                    case "gte":
                        return threshold;

                    case "<":
                    case "lt":
                        // Use a value that's clearly below threshold to avoid boundary issues
                        return threshold - Math.Max(10, threshold * 0.1); // Subtract at least 10 or 10% of the threshold

                    case "<=":
                    case "lte":
                        return threshold;

                    case "==":
                    case "=":
                    case "eq":
                        return threshold;

                    case "!=":
                    case "ne":
                    case "neq":
                        return threshold + 10;

                    default:
                        return threshold + 10;
                }
            }
            else
            {
                // Generate value that doesn't satisfy the condition
                switch (comparisonOperator)
                {
                    case ">":
                    case "gt":
                        return threshold - 1;

                    case ">=":
                    case "gte":
                        return threshold - 1;

                    case "<":
                    case "lt":
                        return threshold + 1;

                    case "<=":
                    case "lte":
                        return threshold + 1;

                    case "==":
                    case "=":
                    case "eq":
                        return threshold + 1;

                    case "!=":
                    case "ne":
                    case "neq":
                        return threshold;

                    default:
                        return threshold - 10;
                }
            }
        }

        /// <summary>
        /// Generates a value that satisfies multiple conditions
        /// </summary>
        private object GenerateValueForMultipleConditions(
            List<ComparisonCondition> conditions,
            ValueTarget target
        )
        {
            // For positive tests, we need a value that satisfies ALL conditions
            // For negative tests, we need a value that fails at least one condition

            if (target == ValueTarget.Positive)
            {
                // Find the most restrictive bounds
                double? lowerBound = null;
                double? upperBound = null;

                foreach (var condition in conditions)
                {
                    var valueObj = condition.Value;
                    if (valueObj == null)
                        continue;

                    double value;
                    if (valueObj is double d)
                        value = d;
                    else if (valueObj is int i)
                        value = i;
                    else if (double.TryParse(valueObj.ToString(), out double parsed))
                        value = parsed;
                    else
                        continue;

                    string op = condition.Operator.ToLowerInvariant();

                    switch (op)
                    {
                        case ">":
                        case "gt":
                            if (lowerBound == null || value > lowerBound)
                                lowerBound = value;
                            break;

                        case ">=":
                        case "gte":
                            if (lowerBound == null || value >= lowerBound)
                                lowerBound = value;
                            break;

                        case "<":
                        case "lt":
                            if (upperBound == null || value < upperBound)
                                upperBound = value;
                            break;

                        case "<=":
                        case "lte":
                            if (upperBound == null || value <= upperBound)
                                upperBound = value;
                            break;

                        case "==":
                        case "=":
                        case "eq":
                            // Exact match required
                            return value;

                        case "!=":
                        case "ne":
                        case "neq":
                            // Any other value works
                            break;
                    }
                }

                // Generate a value within the bounds
                if (lowerBound != null && upperBound != null)
                {
                    if (lowerBound < upperBound)
                    {
                        // We have a valid range
                        return lowerBound + (upperBound - lowerBound) / 2;
                    }
                    else
                    {
                        // No valid value satisfies all conditions
                        return lowerBound;
                    }
                }
                else if (lowerBound != null)
                {
                    return lowerBound + 10;
                }
                else if (upperBound != null)
                {
                    return upperBound - 10;
                }
            }
            else
            {
                // For negative tests, just pick a condition and fail it
                var condition = conditions.First();
                return GenerateValueForCondition(condition, ValueTarget.Negative);
            }

            // Default value
            return target == ValueTarget.Positive ? 42 : 0;
        }

        /// <summary>
        /// Generates a default value based on sensor name
        /// </summary>
        private object GenerateDefaultValue(string sensor)
        {
            sensor = sensor.ToLowerInvariant();

            // Boolean values
            if (
                sensor.Contains("enabled")
                || sensor.Contains("active")
                || sensor.Contains("on")
                || sensor.Contains("status")
            )
            {
                return true;
            }

            // Temperature values
            if (sensor.Contains("temperature"))
            {
                return 25.0;
            }

            // Humidity values
            if (sensor.Contains("humidity") || sensor.Contains("moisture"))
            {
                return 50.0;
            }

            // Pressure values
            if (sensor.Contains("pressure"))
            {
                return 1013.0;
            }

            // Level values
            if (sensor.Contains("level") || sensor.Contains("percent"))
            {
                return 75.0;
            }

            // Count values
            if (sensor.Contains("count") || sensor.Contains("number"))
            {
                return 5;
            }

            // Default to a random number
            return _random.Next(1, 100);
        }
    }
}
