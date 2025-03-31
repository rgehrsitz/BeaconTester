using BeaconTester.RuleAnalyzer.Parsing;
using Serilog;

namespace BeaconTester.RuleAnalyzer.Analysis
{
    /// <summary>
    /// Analyzes dependencies between rules
    /// </summary>
    public class DependencyAnalyzer
    {
        private readonly ILogger _logger;
        private readonly ConditionAnalyzer _conditionAnalyzer;

        /// <summary>
        /// Creates a new dependency analyzer
        /// </summary>
        public DependencyAnalyzer(ILogger logger)
        {
            _logger = logger.ForContext<DependencyAnalyzer>();
            _conditionAnalyzer = new ConditionAnalyzer(logger);
        }

        /// <summary>
        /// Analyzes dependencies between rules
        /// </summary>
        public List<RuleDependency> AnalyzeDependencies(List<RuleDefinition> rules)
        {
            _logger.Debug("Analyzing dependencies between {RuleCount} rules", rules.Count);
            var dependencies = new List<RuleDependency>();

            // Create a lookup of rule outputs
            var ruleOutputs = CreateRuleOutputsLookup(rules);

            // Identify dependencies
            foreach (var rule in rules)
            {
                if (rule.Conditions == null)
                    continue;

                // Get all sensors used in the rule's conditions
                var sensors = _conditionAnalyzer.ExtractSensors(rule.Conditions);

                // Find which outputs from other rules are used in this rule's conditions
                foreach (var sensor in sensors)
                {
                    if (sensor.StartsWith("output:"))
                    {
                        // This sensor is an output from another rule
                        if (ruleOutputs.TryGetValue(sensor, out var sourceRules))
                        {
                            foreach (var sourceRule in sourceRules)
                            {
                                // Check for self-dependency
                                if (sourceRule.Name == rule.Name)
                                    continue;

                                // Add the dependency
                                var dependency = new RuleDependency
                                {
                                    SourceRule = sourceRule,
                                    TargetRule = rule,
                                    DependencyType = DependencyType.Output,
                                    Key = sensor,
                                };

                                dependencies.Add(dependency);
                                _logger.Debug(
                                    "Found dependency: {TargetRule} depends on {SourceRule} via {Key}",
                                    rule.Name,
                                    sourceRule.Name,
                                    sensor
                                );
                            }
                        }
                    }
                }
            }

            _logger.Information("Found {DependencyCount} dependencies", dependencies.Count);
            return dependencies;
        }

        /// <summary>
        /// Creates a lookup of outputs to rules that set them
        /// </summary>
        private Dictionary<string, List<RuleDefinition>> CreateRuleOutputsLookup(
            List<RuleDefinition> rules
        )
        {
            var outputs = new Dictionary<string, List<RuleDefinition>>();

            foreach (var rule in rules)
            {
                foreach (var action in rule.Actions)
                {
                    if (action is SetValueAction setValueAction)
                    {
                        string key = setValueAction.Key;

                        if (!outputs.ContainsKey(key))
                        {
                            outputs[key] = new List<RuleDefinition>();
                        }

                        outputs[key].Add(rule);
                    }
                }
            }

            return outputs;
        }
    }

    /// <summary>
    /// Represents a dependency between rules
    /// </summary>
    public class RuleDependency
    {
        /// <summary>
        /// The rule that produces the dependency
        /// </summary>
        public RuleDefinition SourceRule { get; set; } = null!;

        /// <summary>
        /// The rule that consumes the dependency
        /// </summary>
        public RuleDefinition TargetRule { get; set; } = null!;

        /// <summary>
        /// The type of dependency
        /// </summary>
        public DependencyType DependencyType { get; set; }

        /// <summary>
        /// The key that creates the dependency
        /// </summary>
        public string Key { get; set; } = string.Empty;
    }

    /// <summary>
    /// Types of rule dependencies
    /// </summary>
    public enum DependencyType
    {
        /// <summary>
        /// Dependency via output key
        /// </summary>
        Output,

        /// <summary>
        /// Dependency via buffer
        /// </summary>
        Buffer,
    }
}
