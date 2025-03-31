using Serilog;
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
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
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

                // Deserialize the YAML into our model
                var ruleGroup = _deserializer.Deserialize<RuleGroup>(yamlContent);

                if (ruleGroup == null || ruleGroup.Rules == null || ruleGroup.Rules.Count == 0)
                {
                    _logger.Warning(
                        "No rules found in {SourceFile}",
                        string.IsNullOrEmpty(sourceFile) ? "content" : sourceFile
                    );
                    return new List<RuleDefinition>();
                }

                // Set the source file for each rule
                foreach (var rule in ruleGroup.Rules)
                {
                    rule.SourceFile = sourceFile;
                }

                _logger.Information(
                    "Parsed {RuleCount} rules from {SourceFile}",
                    ruleGroup.Rules.Count,
                    string.IsNullOrEmpty(sourceFile) ? "content" : sourceFile
                );

                return ruleGroup.Rules;
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
