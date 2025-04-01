using Serilog;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeaconTester.RuleAnalyzer.Parsing
{
    /// <summary>
    /// Parses YAML rule definitions
    /// </summary>
    public class RuleParser
    {
        private readonly ILogger _logger;
        private readonly IDeserializer _deserializer;

        /// <summary>
        /// Creates a new rule parser
        /// </summary>
        public RuleParser(ILogger logger)
        {
            _logger = logger.ForContext<RuleParser>();

            // Configure YAML deserializer
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)  // Use underscore convention like Pulsar
                .IgnoreUnmatchedProperties()
                .Build();
        }

        /// <summary>
        /// Parses rules from YAML content
        /// </summary>
        public List<RuleDefinition> ParseRules(string yamlContent, string sourceFile = "")
        {
            try
            {
                _logger.Debug(
                    "Parsing rules from {SourceFile}",
                    string.IsNullOrEmpty(sourceFile) ? "content" : sourceFile
                );

                // First deserialize using our YAML model
                RuleRoot? ruleRoot;
                try 
                {
                    ruleRoot = _deserializer.Deserialize<RuleRoot>(yamlContent);
                    _logger.Debug("Successfully deserialized YAML");
                }
                catch (YamlDotNet.Core.YamlException yamlEx)
                {
                    _logger.Error(yamlEx, "YAML parsing error at {Start}: {Message}", 
                        yamlEx.Start, yamlEx.Message);
                    throw;
                }

                if (ruleRoot == null || ruleRoot.Rules == null || ruleRoot.Rules.Count == 0)
                {
                    _logger.Warning(
                        "No rules found in {SourceFile}",
                        string.IsNullOrEmpty(sourceFile) ? "content" : sourceFile
                    );
                    return new List<RuleDefinition>();
                }

                // Convert from the YAML model to the domain model
                var ruleDefinitions = new List<RuleDefinition>();
                foreach (var rule in ruleRoot.Rules)
                {
                    var ruleDef = new RuleDefinition
                    {
                        Name = rule.Name,
                        Description = rule.Description,
                        SourceFile = sourceFile,
                        LineNumber = rule.LineNumber
                    };

                    // Convert conditions
                    if (rule.Conditions != null)
                    {
                        ruleDef.Conditions = new ConditionGroup();
                        
                        // Handle 'all' conditions
                        if (rule.Conditions.All != null)
                        {
                            foreach (var condItem in rule.Conditions.All)
                            {
                                var wrapper = new ConditionWrapper();
                                wrapper.Condition = ConvertCondition(condItem.Condition);
                                ruleDef.Conditions.All.Add(wrapper);
                            }
                        }
                        
                        // Handle 'any' conditions
                        if (rule.Conditions.Any != null)
                        {
                            foreach (var condItem in rule.Conditions.Any)
                            {
                                var wrapper = new ConditionWrapper();
                                wrapper.Condition = ConvertCondition(condItem.Condition);
                                ruleDef.Conditions.Any.Add(wrapper);
                            }
                        }
                    }

                    // Convert actions
                    if (rule.Actions != null)
                    {
                        foreach (var actionItem in rule.Actions)
                        {
                            if (actionItem.SetValue != null)
                            {
                                ruleDef.Actions.Add(new SetValueAction
                                {
                                    Key = actionItem.SetValue.Key,
                                    Value = actionItem.SetValue.Value,
                                    ValueExpression = actionItem.SetValue.ValueExpression
                                });
                            }
                            else if (actionItem.SendMessage != null)
                            {
                                ruleDef.Actions.Add(new SendMessageAction
                                {
                                    Channel = actionItem.SendMessage.Channel,
                                    Message = actionItem.SendMessage.Message,
                                    MessageExpression = actionItem.SendMessage.MessageExpression
                                });
                            }
                        }
                    }

                    ruleDefinitions.Add(ruleDef);
                }

                _logger.Information(
                    "Parsed {RuleCount} rules from {SourceFile}",
                    ruleDefinitions.Count,
                    string.IsNullOrEmpty(sourceFile) ? "content" : sourceFile
                );

                return ruleDefinitions;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to parse rules from {SourceFile}",
                    string.IsNullOrEmpty(sourceFile) ? "content" : sourceFile
                );
                throw;
            }
        }
        
        /// <summary>
        /// Converts a YAML condition to a domain model condition
        /// </summary>
        private ConditionDefinition ConvertCondition(ConditionDetails condition)
        {
            var type = condition.Type.ToLowerInvariant();
            
            switch (type)
            {
                case "comparison":
                    return new ComparisonCondition
                    {
                        Type = "comparison",
                        Sensor = condition.Sensor ?? string.Empty,
                        Operator = condition.Operator ?? ">",
                        Value = condition.Value ?? 0
                    };
                    
                case "expression":
                    return new ExpressionCondition
                    {
                        Type = "expression",
                        Expression = condition.Expression ?? string.Empty
                    };
                    
                case "threshold_over_time":
                    return new ThresholdOverTimeCondition
                    {
                        Type = "threshold_over_time",
                        Sensor = condition.Sensor ?? string.Empty,
                        Threshold = condition.Value ?? 0,
                        Duration = condition.Duration ?? 0
                    };
                    
                default:
                    _logger.Warning("Unknown condition type: {Type}", type);
                    return new ComparisonCondition { Type = type };
            }
        }

        /// <summary>
        /// Parses rules from a YAML file
        /// </summary>
        public List<RuleDefinition> ParseRulesFromFile(string filePath)
        {
            try
            {
                _logger.Debug("Reading rules from file {FilePath}", filePath);

                if (!File.Exists(filePath))
                {
                    _logger.Error("Rule file does not exist: {FilePath}", filePath);
                    throw new FileNotFoundException($"Rule file not found: {filePath}");
                }

                string yamlContent = File.ReadAllText(filePath);
                return ParseRules(yamlContent, filePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse rules from file {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// Parses rules from multiple YAML files
        /// </summary>
        public List<RuleDefinition> ParseRulesFromFiles(IEnumerable<string> filePaths)
        {
            var allRules = new List<RuleDefinition>();

            foreach (var filePath in filePaths)
            {
                try
                {
                    var rules = ParseRulesFromFile(filePath);
                    allRules.AddRange(rules);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse rules from file {FilePath}", filePath);
                    // Continue with other files
                }
            }

            _logger.Information(
                "Parsed {RuleCount} rules from {FileCount} files",
                allRules.Count,
                filePaths.Count()
            );

            return allRules;
        }
    }
}
