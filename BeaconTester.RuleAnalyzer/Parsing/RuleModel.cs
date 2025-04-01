using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeaconTester.RuleAnalyzer.Parsing
{
    /// <summary>
    /// Represents a YAML rule definition
    /// </summary>
    public class RuleDefinition
    {
        /// <summary>
        /// Name of the rule
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of the rule's purpose
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Conditions that trigger the rule
        /// </summary>
        public ConditionGroup? Conditions { get; set; }

        /// <summary>
        /// Actions to perform when conditions are met
        /// </summary>
        public List<ActionDefinition> Actions { get; set; } = new List<ActionDefinition>();

        /// <summary>
        /// Source file containing this rule
        /// </summary>
        [YamlIgnore]
        public string SourceFile { get; set; } = string.Empty;

        /// <summary>
        /// Line number in source file
        /// </summary>
        [YamlIgnore]
        public int LineNumber { get; set; }
    }

    /// <summary>
    /// Group of rule definitions
    /// </summary>
    public class RuleGroup
    {
        /// <summary>
        /// The rules in this group
        /// </summary>
        public List<RuleDefinition> Rules { get; set; } = new List<RuleDefinition>();
    }

    /// <summary>
    /// Base class for rule conditions
    /// </summary>
    public abstract class ConditionDefinition
    {
        /// <summary>
        /// Type of condition
        /// </summary>
        public string Type { get; set; } = string.Empty;
    }

    /// <summary>
    /// Group of conditions (all/any)
    /// </summary>
    public class ConditionGroup : ConditionDefinition
    {
        /// <summary>
        /// Conditions that must all be true
        /// </summary>
        public List<ConditionWrapper> All { get; set; } = new List<ConditionWrapper>();

        /// <summary>
        /// Conditions where any can be true
        /// </summary>
        public List<ConditionWrapper> Any { get; set; } = new List<ConditionWrapper>();
    }
    
    /// <summary>
    /// Wrapper for condition to match Pulsar YAML format
    /// </summary>
    public class ConditionWrapper
    {
        /// <summary>
        /// The wrapped condition
        /// </summary>
        [YamlMember(Alias = "condition")]
        public ConditionDefinition Condition { get; set; }
    }

    /// <summary>
    /// Comparison condition (sensor > value)
    /// </summary>
    public class ComparisonCondition : ConditionDefinition
    {
        /// <summary>
        /// Sensor or input to evaluate
        /// </summary>
        public string Sensor { get; set; } = string.Empty;

        /// <summary>
        /// Comparison operator (>, <, ==, etc.)
        /// </summary>
        public string Operator { get; set; } = string.Empty;

        /// <summary>
        /// Value to compare against
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// Expression to evaluate for the value
        /// </summary>
        public string? ValueExpression { get; set; }
    }

    /// <summary>
    /// Expression condition (evaluated as boolean)
    /// </summary>
    public class ExpressionCondition : ConditionDefinition
    {
        /// <summary>
        /// Expression to evaluate
        /// </summary>
        public string Expression { get; set; } = string.Empty;
    }

    /// <summary>
    /// Threshold over time condition (temporal)
    /// </summary>
    public class ThresholdOverTimeCondition : ConditionDefinition
    {
        /// <summary>
        /// Sensor to monitor
        /// </summary>
        public string Sensor { get; set; } = string.Empty;

        /// <summary>
        /// Threshold value
        /// </summary>
        public double Threshold { get; set; }

        /// <summary>
        /// Duration in milliseconds
        /// </summary>
        public int Duration { get; set; }

        /// <summary>
        /// Comparison operator
        /// </summary>
        public string Operator { get; set; } = ">";

        /// <summary>
        /// Mode for evaluating missing data points
        /// </summary>
        public string? Mode { get; set; }
    }

    /// <summary>
    /// Base class for rule actions
    /// </summary>
    public abstract class ActionDefinition
    {
        /// <summary>
        /// Type of action
        /// </summary>
        public string Type { get; set; } = string.Empty;
    }

    /// <summary>
    /// Set value action
    /// </summary>
    public class SetValueAction : ActionDefinition
    {
        /// <summary>
        /// Key to set
        /// </summary>
        [YamlMember(Alias = "key")]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Static value to set
        /// </summary>
        [YamlMember(Alias = "value")]
        public object? Value { get; set; }

        /// <summary>
        /// Expression to evaluate for the value
        /// </summary>
        [YamlMember(Alias = "value_expression")]
        public string? ValueExpression { get; set; }
    }

    /// <summary>
    /// Send message action
    /// </summary>
    public class SendMessageAction : ActionDefinition
    {
        /// <summary>
        /// Channel to send message to
        /// </summary>
        [YamlMember(Alias = "channel")]
        public string Channel { get; set; } = string.Empty;

        /// <summary>
        /// Static message to send
        /// </summary>
        [YamlMember(Alias = "message")]
        public string? Message { get; set; }

        /// <summary>
        /// Expression to evaluate for the message
        /// </summary>
        [YamlMember(Alias = "message_expression")]
        public string? MessageExpression { get; set; }
    }

}
