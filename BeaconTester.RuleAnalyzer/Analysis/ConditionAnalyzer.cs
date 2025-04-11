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
                foreach (var wrapper in group.All)
                {
                    if (wrapper.Condition != null)
                    {
                        var childSensors = ExtractSensors(wrapper.Condition);
                        sensors.UnionWith(childSensors);
                    }
                }

                // Process 'any' conditions
                foreach (var wrapper in group.Any)
                {
                    if (wrapper.Condition != null)
                    {
                        var childSensors = ExtractSensors(wrapper.Condition);
                        sensors.UnionWith(childSensors);
                    }
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
                foreach (var wrapper in group.All)
                {
                    if (wrapper.Condition != null && HasTemporalCondition(wrapper.Condition))
                        return true;
                }

                // Check 'any' conditions
                foreach (var wrapper in group.Any)
                {
                    if (wrapper.Condition != null && HasTemporalCondition(wrapper.Condition))
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
        
        /// <summary>
        /// Normalizes a value to its proper type, especially handling string representations of boolean values
        /// </summary>
        public object NormalizeValue(object? value)
        {
            if (value == null)
                return false;
                
            // Handle string booleans
            if (value is string strValue)
            {
                if (strValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (strValue.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (double.TryParse(strValue, out double numVal))
                    return numVal;
                    
                // Return the string if it's not a boolean or number
                return strValue;
            }
            
            return value;
        }
        
        /// <summary>
        /// Analyzes a condition to determine what value a sensor should have to satisfy it
        /// </summary>
        public Dictionary<string, object> AnalyzeConditionRequirements(ConditionDefinition condition)
        {
            var requirements = new Dictionary<string, object>();
            
            if (condition is ConditionGroup group)
            {
                // For 'all' conditions (AND), we need to satisfy all
                foreach (var wrapper in group.All)
                {
                    if (wrapper.Condition != null)
                    {
                        var subRequirements = AnalyzeConditionRequirements(wrapper.Condition);
                        foreach (var item in subRequirements)
                        {
                            requirements[item.Key] = item.Value;
                        }
                    }
                }
                
                // For 'any' conditions (OR), satisfying one is enough, but to simplify
                // we'll try to satisfy all of them too
                foreach (var wrapper in group.Any)
                {
                    if (wrapper.Condition != null)
                    {
                        var subRequirements = AnalyzeConditionRequirements(wrapper.Condition);
                        foreach (var item in subRequirements)
                        {
                            requirements[item.Key] = item.Value;
                        }
                    }
                }
            }
            else if (condition is ComparisonCondition comparison)
            {
                // For direct comparison conditions, determine what value would satisfy it
                var sensor = comparison.Sensor;
                var op = comparison.Operator?.ToLowerInvariant() ?? "equal_to";
                var value = comparison.Value;
                
                // Normalize the operator
                string normalizedOp = NormalizeOperator(op);
                
                // Determine what value this sensor needs to have
                switch (normalizedOp)
                {
                    case ">":
                        // For greater than, need a value higher than the comparison
                        if (value is double d1) requirements[sensor] = d1 + 10;
                        else if (value is int i1) requirements[sensor] = i1 + 10;
                        else if (value is bool b1) requirements[sensor] = true;
                        else if (value is string s1 && bool.TryParse(s1, out bool bv1)) requirements[sensor] = true;
                        else requirements[sensor] = value != null ? value : true;
                        break;
                        
                    case ">=":
                        // For greater than or equal, can use exact value
                        if (value is double d2) requirements[sensor] = d2;
                        else if (value is int i2) requirements[sensor] = i2;
                        else if (value is bool b2) requirements[sensor] = true;
                        else if (value is string s2 && bool.TryParse(s2, out bool bv2)) requirements[sensor] = true;
                        else requirements[sensor] = value != null ? value : true;
                        break;
                        
                    case "<":
                        // For less than, need a value lower than the comparison
                        if (value is double d3) requirements[sensor] = d3 - 10;
                        else if (value is int i3) requirements[sensor] = i3 - 10;
                        else if (value is bool b3) requirements[sensor] = false;
                        else if (value is string s3 && bool.TryParse(s3, out bool bv3)) requirements[sensor] = false;
                        else requirements[sensor] = value != null ? value : false;
                        break;
                        
                    case "<=":
                        // For less than or equal, can use exact value
                        if (value is double d4) requirements[sensor] = d4;
                        else if (value is int i4) requirements[sensor] = i4;
                        else if (value is bool b4) requirements[sensor] = false;
                        else if (value is string s4 && bool.TryParse(s4, out bool bv4)) requirements[sensor] = false;
                        else requirements[sensor] = value != null ? value : false;
                        break;
                        
                    case "==":
                        // For equal, use exact value
                        requirements[sensor] = value ?? true;
                        break;
                        
                    case "!=":
                        // For not equal, use opposite value
                        if (value is bool b) requirements[sensor] = !b;
                        else if (value is string s && bool.TryParse(s, out bool bv)) requirements[sensor] = !bv;
                        else if (value is double d5) requirements[sensor] = d5 + 10;
                        else if (value is int i5) requirements[sensor] = i5 + 10;
                        else requirements[sensor] = value != null ? !Equals(value, true) : false;
                        break;
                        
                    default:
                        // Default to using the value as-is
                        requirements[sensor] = value ?? true;
                        break;
                }
                
                _logger.Debug("Condition {Condition} requires {Sensor}={Value}", 
                    $"{sensor} {normalizedOp} {value}", sensor, requirements[sensor]);
            }
            else if (condition is ThresholdOverTimeCondition temporal)
            {
                // For temporal conditions, basic test is not appropriate
                // We'll just log this but not add specific requirements
                _logger.Debug("Temporal condition for {Sensor} will not be satisfied in basic test", 
                    temporal.Sensor);
            }
            
            return requirements;
        }
        
        /// <summary>
        /// Normalizes various operator representations to standard form
        /// </summary>
        private string NormalizeOperator(string op)
        {
            return op.ToLowerInvariant() switch
            {
                "greater_than" => ">",
                "gt" => ">",
                "less_than" => "<",
                "lt" => "<",
                "greater_than_or_equal_to" => ">=",
                "gte" => ">=",
                "less_than_or_equal_to" => "<=",
                "lte" => "<=",
                "equal_to" => "==",
                "eq" => "==",
                "=" => "==",
                "not_equal_to" => "!=",
                "ne" => "!=",
                "neq" => "!=",
                _ => op
            };
        }
    }
}
