using System.Text.RegularExpressions;
using BeaconTester.RuleAnalyzer.Parsing;
using Serilog;

namespace BeaconTester.RuleAnalyzer.Analysis
{
    /// <summary>
    /// Analyzes rule conditions to extract information
    /// </summary>
    public class ConditionAnalyzer
    {
        private readonly ILogger _logger;

        // Regular expression for finding sensors in expressions
        private static readonly Regex SensorRegex = new Regex(
            @"input:[a-zA-Z0-9_]+|output:[a-zA-Z0-9_]+|buffer:[a-zA-Z0-9_]+",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Creates a new condition analyzer
        /// </summary>
        public ConditionAnalyzer(ILogger logger)
        {
            _logger = logger.ForContext<ConditionAnalyzer>();
        }

        /// <summary>
        /// Extracts all sensors used in conditions
        /// </summary>
        public HashSet<string> ExtractSensors(ConditionDefinition condition)
        {
            var sensors = new HashSet<string>();

            if (condition is ConditionGroup group)
            {
                // Process 'all' conditions
                foreach (var childCondition in group.All)
                {
                    var childSensors = ExtractSensors(childCondition);
                    sensors.UnionWith(childSensors);
                }

                // Process 'any' conditions
                foreach (var childCondition in group.Any)
                {
                    var childSensors = ExtractSensors(childCondition);
                    sensors.UnionWith(childSensors);
                }
            }
            else if (condition is ComparisonCondition comparison)
            {
                // Add the sensor directly
                if (!string.IsNullOrEmpty(comparison.Sensor))
                {
                    sensors.Add(comparison.Sensor);
                }

                // Check value expression for sensors
                if (!string.IsNullOrEmpty(comparison.ValueExpression))
                {
                    var expressionSensors = ExtractSensorsFromExpression(
                        comparison.ValueExpression
                    );
                    sensors.UnionWith(expressionSensors);
                }
            }
            else if (condition is ExpressionCondition expression)
            {
                // Extract sensors from the expression
                if (!string.IsNullOrEmpty(expression.Expression))
                {
                    var expressionSensors = ExtractSensorsFromExpression(expression.Expression);
                    sensors.UnionWith(expressionSensors);
                }
            }
            else if (condition is ThresholdOverTimeCondition temporal)
            {
                // Add the sensor directly
                if (!string.IsNullOrEmpty(temporal.Sensor))
                {
                    sensors.Add(temporal.Sensor);
                }
            }

            return sensors;
        }

        /// <summary>
        /// Extracts sensors from an expression string
        /// </summary>
        public HashSet<string> ExtractSensorsFromExpression(string expression)
        {
            var sensors = new HashSet<string>();

            if (string.IsNullOrEmpty(expression))
                return sensors;

            // Find all matches
            var matches = SensorRegex.Matches(expression);

            foreach (Match match in matches.Cast<Match>())
            {
                sensors.Add(match.Value);
            }

            return sensors;
        }

        /// <summary>
        /// Checks if a condition has temporal components
        /// </summary>
        public bool HasTemporalCondition(ConditionDefinition condition)
        {
            if (condition is ThresholdOverTimeCondition)
                return true;

            if (condition is ConditionGroup group)
            {
                // Check 'all' conditions
                foreach (var childCondition in group.All)
                {
                    if (HasTemporalCondition(childCondition))
                        return true;
                }

                // Check 'any' conditions
                foreach (var childCondition in group.Any)
                {
                    if (HasTemporalCondition(childCondition))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets boundary values for numeric conditions
        /// </summary>
        public (double Min, double Max) GetNumericBoundaries(ComparisonCondition condition)
        {
            double value = 0;

            // Try to get the numeric value
            if (condition.Value is double doubleValue)
            {
                value = doubleValue;
            }
            else if (condition.Value is int intValue)
            {
                value = intValue;
            }
            else if (
                condition.Value != null
                && double.TryParse(condition.Value.ToString(), out double parsedValue)
            )
            {
                value = parsedValue;
            }

            // Determine boundaries based on operator
            switch (condition.Operator)
            {
                case ">":
                    return (value * 0.5, value * 1.5); // Below and above

                case ">=":
                    return (value * 0.5, value * 1.5); // Below and above

                case "<":
                    return (value * 0.5, value * 1.5); // Below and above

                case "<=":
                    return (value * 0.5, value * 1.5); // Below and above

                case "==":
                case "=":
                    return (value, value); // Exact value

                case "!=":
                    return (value * 0.5, value * 1.5); // Different values

                default:
                    return (0, 100); // Default range
            }
        }
    }
}
