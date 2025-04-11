using BeaconTester.RuleAnalyzer.Parsing;
using Serilog;

namespace BeaconTester.RuleAnalyzer.Analysis
{
    /// <summary>
    /// Analyzes rule structure and dependencies
    /// </summary>
    public class RuleAnalyzer
    {
        private readonly ILogger _logger;
        /// <summary>
        /// The condition analyzer used by this rule analyzer
        /// </summary>
        public ConditionAnalyzer ConditionAnalyzer => _conditionAnalyzer;
        
        private readonly ConditionAnalyzer _conditionAnalyzer;
        private readonly DependencyAnalyzer _dependencyAnalyzer;

        /// <summary>
        /// Creates a new rule analyzer
        /// </summary>
        public RuleAnalyzer(ILogger logger)
        {
            _logger = logger.ForContext<RuleAnalyzer>();
            _conditionAnalyzer = new ConditionAnalyzer(logger);
            _dependencyAnalyzer = new DependencyAnalyzer(logger);
        }

        /// <summary>
        /// Analyzes a set of rules
        /// </summary>
        public RuleAnalysisResult AnalyzeRules(List<RuleDefinition> rules)
        {
            _logger.Information("Analyzing {RuleCount} rules", rules.Count);

            var result = new RuleAnalysisResult { Rules = rules };

            try
            {
                // Extract input sensors from all rules
                result.InputSensors = ExtractInputSensors(rules);
                _logger.Debug("Found {SensorCount} input sensors", result.InputSensors.Count);

                // Extract output keys from all rules
                result.OutputKeys = ExtractOutputKeys(rules);
                _logger.Debug("Found {OutputCount} output keys", result.OutputKeys.Count);

                // Analyze temporal conditions
                result.TemporalRules = rules.Where(r => HasTemporalCondition(r)).ToList();
                _logger.Debug(
                    "Found {TemporalCount} rules with temporal conditions",
                    result.TemporalRules.Count
                );

                // Analyze rule dependencies
                result.Dependencies = _dependencyAnalyzer.AnalyzeDependencies(rules);
                _logger.Debug(
                    "Found {DependencyCount} rule dependencies",
                    result.Dependencies.Count
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing rules");
                throw;
            }
        }

        /// <summary>
        /// Extracts all input sensors from rules
        /// </summary>
        private HashSet<string> ExtractInputSensors(List<RuleDefinition> rules)
        {
            var sensors = new HashSet<string>();

            foreach (var rule in rules)
            {
                if (rule.Conditions != null)
                {
                    var ruleSensors = _conditionAnalyzer.ExtractSensors(rule.Conditions);
                    foreach (var sensor in ruleSensors)
                    {
                        if (sensor.StartsWith("input:"))
                        {
                            sensors.Add(sensor);
                        }
                    }
                }
            }

            return sensors;
        }

        /// <summary>
        /// Extracts all output keys from rules
        /// </summary>
        private HashSet<string> ExtractOutputKeys(List<RuleDefinition> rules)
        {
            var outputs = new HashSet<string>();

            foreach (var rule in rules)
            {
                foreach (var action in rule.Actions)
                {
                    if (action is SetValueAction setValueAction)
                    {
                        if (setValueAction.Key.StartsWith("output:"))
                        {
                            outputs.Add(setValueAction.Key);
                        }
                    }
                }
            }

            return outputs;
        }

        /// <summary>
        /// Checks if a rule has temporal conditions
        /// </summary>
        private bool HasTemporalCondition(RuleDefinition rule)
        {
            if (rule.Conditions == null)
                return false;

            return _conditionAnalyzer.HasTemporalCondition(rule.Conditions);
        }
    }

    /// <summary>
    /// Result of rule analysis
    /// </summary>
    public class RuleAnalysisResult
    {
        /// <summary>
        /// The analyzed rules
        /// </summary>
        public List<RuleDefinition> Rules { get; set; } = new List<RuleDefinition>();

        /// <summary>
        /// Input sensors used by rules
        /// </summary>
        public HashSet<string> InputSensors { get; set; } = new HashSet<string>();

        /// <summary>
        /// Output keys set by rules
        /// </summary>
        public HashSet<string> OutputKeys { get; set; } = new HashSet<string>();

        /// <summary>
        /// Rules with temporal conditions
        /// </summary>
        public List<RuleDefinition> TemporalRules { get; set; } = new List<RuleDefinition>();

        /// <summary>
        /// Dependencies between rules
        /// </summary>
        public List<RuleDependency> Dependencies { get; set; } = new List<RuleDependency>();
    }
}
