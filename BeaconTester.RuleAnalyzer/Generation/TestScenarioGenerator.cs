using BeaconTester.Core.Models;
using BeaconTester.RuleAnalyzer.Analysis;
using BeaconTester.RuleAnalyzer.Parsing;
using Serilog;

namespace BeaconTester.RuleAnalyzer.Generation
{
    /// <summary>
    /// Generates test scenarios from rule definitions
    /// </summary>
    public class TestScenarioGenerator
    {
        private readonly ILogger _logger;
        private readonly TestCaseGenerator _testCaseGenerator;
        private readonly Analysis.RuleAnalyzer _ruleAnalyzer;

        /// <summary>
        /// Creates a new test scenario generator
        /// </summary>
        public TestScenarioGenerator(ILogger logger)
        {
            _logger = logger.ForContext<TestScenarioGenerator>();
            _testCaseGenerator = new TestCaseGenerator(logger);
            _ruleAnalyzer = new Analysis.RuleAnalyzer(logger);
        }

        /// <summary>
        /// Generates test scenarios for all rules
        /// </summary>
        public List<TestScenario> GenerateScenarios(List<RuleDefinition> rules)
        {
            _logger.Information("Generating test scenarios for {RuleCount} rules", rules.Count);
            var scenarios = new List<TestScenario>();

            try
            {
                // Analyze rules first to understand structure
                var analysis = _ruleAnalyzer.AnalyzeRules(rules);

                // Generate basic test for each rule
                foreach (var rule in rules)
                {
                    var scenario = GenerateBasicScenario(rule);
                    scenarios.Add(scenario);
                }

                // Generate dependency tests
                if (analysis.Dependencies.Count > 0)
                {
                    var dependencyTests = GenerateDependencyScenarios(analysis);
                    scenarios.AddRange(dependencyTests);
                }

                // Generate temporal tests
                if (analysis.TemporalRules.Count > 0)
                {
                    var temporalTests = GenerateTemporalScenarios(analysis.TemporalRules);
                    scenarios.AddRange(temporalTests);
                }

                _logger.Information("Generated {ScenarioCount} test scenarios", scenarios.Count);
                return scenarios;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating test scenarios");
                throw;
            }
        }

        /// <summary>
        /// Generates a basic test scenario for a rule
        /// </summary>
        private TestScenario GenerateBasicScenario(RuleDefinition rule)
        {
            _logger.Debug("Generating basic test scenario for rule: {RuleName}", rule.Name);

            var scenario = new TestScenario
            {
                Name = $"{rule.Name}BasicTest",
                Description = $"Basic test for rule {rule.Name}: {rule.Description}",
            };

            try
            {
                // Generate test cases (input values that trigger the rule)
                var testCase = _testCaseGenerator.GenerateBasicTestCase(rule);

                if (testCase.Inputs.Count > 0)
                {
                    // Create step for positive test case
                    var positiveStep = new TestStep
                    {
                        Name = "Positive test case",
                        Description = "Test inputs that should trigger the rule",
                        Inputs = testCase
                            .Inputs.Select(i => new TestInput { Key = i.Key, Value = i.Value })
                            .ToList(),
                        Delay = 500, // Default delay
                        Expectations = testCase
                            .Outputs.Select(o => new TestExpectation
                            {
                                Key = o.Key,
                                Expected = o.Value,
                                Validator = GetValidatorType(o.Value),
                            })
                            .ToList(),
                    };

                    scenario.Steps.Add(positiveStep);
                }

                // Generate negative test case if possible
                var negativeCase = _testCaseGenerator.GenerateNegativeTestCase(rule);

                if (negativeCase.Inputs.Count > 0)
                {
                    // Create step for negative test case
                    var negativeStep = new TestStep
                    {
                        Name = "Negative test case",
                        Description = "Test inputs that should not trigger the rule",
                        Inputs = negativeCase
                            .Inputs.Select(i => new TestInput { Key = i.Key, Value = i.Value })
                            .ToList(),
                        Delay = 500, // Default delay
                        Expectations = negativeCase
                            .Outputs.Select(o => new TestExpectation
                            {
                                Key = o.Key,
                                Expected = o.Value,
                                Validator = GetValidatorType(o.Value),
                            })
                            .ToList(),
                    };

                    scenario.Steps.Add(negativeStep);
                }

                return scenario;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error generating basic scenario for rule {RuleName}", rule.Name);

                // Return a placeholder scenario
                scenario.Steps.Add(
                    new TestStep
                    {
                        Name = "Error generating test case",
                        Description = $"Error: {ex.Message}",
                        Inputs = new List<TestInput>(),
                        Expectations = new List<TestExpectation>(),
                    }
                );

                return scenario;
            }
        }

        /// <summary>
        /// Generates test scenarios for rule dependencies
        /// </summary>
        private List<TestScenario> GenerateDependencyScenarios(RuleAnalysisResult analysis)
        {
            _logger.Debug(
                "Generating dependency test scenarios for {DependencyCount} dependencies",
                analysis.Dependencies.Count
            );

            var scenarios = new List<TestScenario>();

            // Group dependencies by target rule
            var dependenciesByTarget = analysis
                .Dependencies.GroupBy(d => d.TargetRule.Name)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var targetRuleName in dependenciesByTarget.Keys)
            {
                var dependencies = dependenciesByTarget[targetRuleName];
                var targetRule = dependencies.First().TargetRule;

                // Create a test scenario that tests the dependencies
                var scenario = new TestScenario
                {
                    Name = $"{targetRuleName}DependencyTest",
                    Description = $"Tests dependencies for rule {targetRuleName}",
                };

                try
                {
                    // We'll use preSetOutputs to simulate the outputs of dependency rules
                    var preSetOutputs = new Dictionary<string, object>();

                    foreach (var dependency in dependencies)
                    {
                        // Find a suitable value for the dependency
                        var key = dependency.Key;
                        var value = GetDefaultValueForKey(key);

                        preSetOutputs[key] = value;
                    }

                    scenario.PreSetOutputs = preSetOutputs;

                    // Now generate inputs for the target rule
                    var testCase = _testCaseGenerator.GenerateBasicTestCase(targetRule);

                    // Create the main test step
                    var step = new TestStep
                    {
                        Name = "Test with dependencies",
                        Description = "Tests rule with dependencies satisfied",
                        Inputs = testCase
                            .Inputs.Select(i => new TestInput { Key = i.Key, Value = i.Value })
                            .ToList(),
                        Delay = 500,
                        Expectations = testCase
                            .Outputs.Select(o => new TestExpectation
                            {
                                Key = o.Key,
                                Expected = o.Value,
                                Validator = GetValidatorType(o.Value),
                            })
                            .ToList(),
                    };

                    scenario.Steps.Add(step);
                    scenarios.Add(scenario);

                    // Also generate a negative case where dependencies are not met
                    var negativeScenario = new TestScenario
                    {
                        Name = $"{targetRuleName}MissingDependencyTest",
                        Description =
                            $"Tests that {targetRuleName} doesn't trigger when dependencies are not met",
                    };

                    // Use empty or opposite preset outputs
                    var oppositeOutputs = new Dictionary<string, object>();
                    foreach (var (key, value) in preSetOutputs)
                    {
                        if (value is bool b)
                        {
                            oppositeOutputs[key] = !b;
                        }
                        else
                        {
                            // For non-boolean values, just use a different value
                            oppositeOutputs[key] = value is double ? 0.0 : "different";
                        }
                    }

                    negativeScenario.PreSetOutputs = oppositeOutputs;

                    // Create the negative test step
                    var negativeStep = new TestStep
                    {
                        Name = "Test with missing dependencies",
                        Description = "Tests rule with dependencies not satisfied",
                        Inputs = testCase
                            .Inputs.Select(i => new TestInput { Key = i.Key, Value = i.Value })
                            .ToList(),
                        Delay = 500,
                        Expectations = testCase
                            .Outputs.Select(o => new TestExpectation
                            {
                                Key = o.Key,
                                Expected = o.Value is bool b ? !b : null, // Expect opposite for boolean, null for others
                                Validator = GetValidatorType(o.Value),
                            })
                            .ToList(),
                    };

                    negativeScenario.Steps.Add(negativeStep);
                    scenarios.Add(negativeScenario);
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Error generating dependency scenario for rule {RuleName}",
                        targetRuleName
                    );
                }
            }

            return scenarios;
        }

        /// <summary>
        /// Generates test scenarios for temporal rules
        /// </summary>
        private List<TestScenario> GenerateTemporalScenarios(List<RuleDefinition> temporalRules)
        {
            _logger.Debug(
                "Generating temporal test scenarios for {RuleCount} rules",
                temporalRules.Count
            );
            var scenarios = new List<TestScenario>();

            foreach (var rule in temporalRules)
            {
                try
                {
                    var scenario = _testCaseGenerator.GenerateTemporalTestCase(rule);
                    if (scenario != null)
                    {
                        scenarios.Add(scenario);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Error generating temporal scenario for rule {RuleName}",
                        rule.Name
                    );
                }
            }

            return scenarios;
        }

        /// <summary>
        /// Gets an appropriate default value for a key
        /// </summary>
        private object GetDefaultValueForKey(string key)
        {
            // For boolean outputs, use true
            if (
                key.Contains("alert")
                || key.Contains("alarm")
                || key.Contains("enabled")
                || key.Contains("active")
                || key.Contains("detected")
            )
            {
                return true;
            }

            // For numeric outputs, use a reasonable value
            if (key.Contains("temperature"))
                return 25.0;
            if (key.Contains("humidity"))
                return 50.0;
            if (key.Contains("pressure"))
                return 1013.0;
            if (key.Contains("level") || key.Contains("percent"))
                return 75.0;
            if (key.Contains("count") || key.Contains("number"))
                return 5;

            // Default to a string value
            return "test_value";
        }

        /// <summary>
        /// Gets appropriate validator type for a value
        /// </summary>
        private string GetValidatorType(object? value)
        {
            if (value is bool)
                return "boolean";
            if (value is double || value is int || value is float)
                return "numeric";
            return "string";
        }
    }
}
