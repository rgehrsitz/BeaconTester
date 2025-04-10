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
                
                // Store the complete set of required input sensors for all rules
                var allRequiredInputs = analysis.InputSensors;
                _logger.Information("Found {InputCount} total input sensors required by all rules", allRequiredInputs.Count);

                // Build a map of input sensors to the conditions that reference them
                var inputConditionMap = BuildInputConditionMap(rules);
                
                // Generate basic test for each rule
                foreach (var rule in rules)
                {
                    var scenario = GenerateBasicScenario(rule, allRequiredInputs, inputConditionMap);
                    scenarios.Add(scenario);
                }

                // Generate dependency tests
                if (analysis.Dependencies.Count > 0)
                {
                    var dependencyTests = GenerateDependencyScenarios(analysis, inputConditionMap);
                    scenarios.AddRange(dependencyTests);
                }

                // Generate temporal tests
                if (analysis.TemporalRules.Count > 0)
                {
                    var temporalTests = GenerateTemporalScenarios(analysis.TemporalRules, allRequiredInputs, inputConditionMap);
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
        /// Builds a map of input sensors to the conditions that reference them
        /// This helps us determine appropriate values for each input based on actual conditions
        /// </summary>
        private Dictionary<string, List<RuleConditionPair>> BuildInputConditionMap(List<RuleDefinition> rules)
        {
            _logger.Debug("Building input condition map for all rules");
            var inputConditionMap = new Dictionary<string, List<RuleConditionPair>>();
            
            foreach (var rule in rules)
            {
                if (rule.Conditions == null)
                    continue;
                    
                // Find all input sensors used by this rule
                var sensors = FindConditionsForAllSensors(rule);
                
                // Add these to our map
                foreach (var pair in sensors)
                {
                    string sensor = pair.Key;
                    if (sensor.StartsWith("input:"))
                    {
                        if (!inputConditionMap.ContainsKey(sensor))
                        {
                            inputConditionMap[sensor] = new List<RuleConditionPair>();
                        }
                        
                        // Add the rule and condition pair
                        foreach (var condition in pair.Value)
                        {
                            inputConditionMap[sensor].Add(new RuleConditionPair(rule, condition));
                        }
                    }
                }
            }
            
            return inputConditionMap;
        }
        
        /// <summary>
        /// Finds all conditions for all sensors in a rule
        /// </summary>
        private Dictionary<string, List<ConditionDefinition>> FindConditionsForAllSensors(RuleDefinition rule)
        {
            var result = new Dictionary<string, List<ConditionDefinition>>();
            
            if (rule.Conditions == null)
                return result;
                
            // Process the condition tree
            ProcessConditionForSensors(rule.Conditions, result);
            
            return result;
        }
        
        /// <summary>
        /// Recursively processes a condition tree to find all sensors and their conditions
        /// </summary>
        private void ProcessConditionForSensors(
            ConditionDefinition condition, 
            Dictionary<string, List<ConditionDefinition>> result
        )
        {
            if (condition is ComparisonCondition comparison)
            {
                // Add this sensor and condition
                string sensor = comparison.Sensor;
                if (!result.ContainsKey(sensor))
                {
                    result[sensor] = new List<ConditionDefinition>();
                }
                result[sensor].Add(comparison);
            }
            else if (condition is ThresholdOverTimeCondition temporal)
            {
                // Add this sensor and condition
                string sensor = temporal.Sensor;
                if (!result.ContainsKey(sensor))
                {
                    result[sensor] = new List<ConditionDefinition>();
                }
                result[sensor].Add(temporal);
            }
            else if (condition is ConditionGroup group)
            {
                // Process 'all' conditions
                foreach (var wrapper in group.All)
                {
                    if (wrapper.Condition != null)
                    {
                        ProcessConditionForSensors(wrapper.Condition, result);
                    }
                }

                // Process 'any' conditions
                foreach (var wrapper in group.Any)
                {
                    if (wrapper.Condition != null)
                    {
                        ProcessConditionForSensors(wrapper.Condition, result);
                    }
                }
            }
        }
        
        /// <summary>
        /// Helper class to associate a rule with a condition
        /// </summary>
        private class RuleConditionPair
        {
            public RuleDefinition Rule { get; }
            public ConditionDefinition Condition { get; }
            
            public RuleConditionPair(RuleDefinition rule, ConditionDefinition condition)
            {
                Rule = rule;
                Condition = condition;
            }
        }

        /// <summary>
        /// Generates a basic test scenario for a rule
        /// </summary>
        /// <param name="rule">The rule to generate a test scenario for</param>
        /// <param name="allRequiredInputs">The complete set of input sensors required by all rules</param>
        /// <param name="inputConditionMap">Map of input sensors to the conditions that reference them</param>
        private TestScenario GenerateBasicScenario(
            RuleDefinition rule, 
            HashSet<string> allRequiredInputs,
            Dictionary<string, List<RuleConditionPair>>? inputConditionMap = null
        )
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
                
                // Create preset outputs for testing latching behavior
                var preSetOutputs = new Dictionary<string, object>();
                
                // Initialize all outputs that might be affected by this rule to a known state
                foreach (var action in rule.Actions)
                {
                    if (action is SetValueAction setValueAction && setValueAction.Key.StartsWith("output:"))
                    {
                        // Determine a suitable initial value (opposite of what the rule will set)
                        var expectedValue = testCase.Outputs.ContainsKey(setValueAction.Key) 
                            ? testCase.Outputs[setValueAction.Key] 
                            : null;
                            
                        if (expectedValue is bool boolValue)
                        {
                            // For boolean outputs, start with the opposite value
                            preSetOutputs[setValueAction.Key] = !boolValue;
                        }
                        else if (expectedValue != null)
                        {
                            // For non-boolean outputs, initialize to a different value
                            if (expectedValue is double numValue)
                            {
                                preSetOutputs[setValueAction.Key] = numValue > 5 ? 0.0 : 100.0;
                            }
                            else
                            {
                                preSetOutputs[setValueAction.Key] = "initial_value";
                            }
                        }
                    }
                }
                
                // Add the preSetOutputs to the scenario if we have any
                if (preSetOutputs.Count > 0)
                {
                    scenario.PreSetOutputs = preSetOutputs;
                }
                
                // Helper function to ensure all required inputs are included
                List<TestInput> EnsureAllRequiredInputs(Dictionary<string, object> inputs, bool isPositiveCase)
                {
                    var allInputs = new Dictionary<string, object>(inputs);
                    
                    // Add any missing inputs from the complete set of required inputs
                    foreach (var sensor in allRequiredInputs)
                    {
                        if (!allInputs.ContainsKey(sensor))
                        {
                            // If we have condition information for this sensor, use it to determine an appropriate value
                            if (inputConditionMap != null && inputConditionMap.TryGetValue(sensor, out var conditionPairs) && conditionPairs.Count > 0)
                            {
                                // Use the first condition to determine a suitable value
                                var pair = conditionPairs.First();
                                var valueTarget = isPositiveCase ? ValueTarget.Positive : ValueTarget.Negative;
                                var value = _testCaseGenerator.GenerateValueForSensor(sensor, pair.Condition, valueTarget);
                                allInputs[sensor] = value;
                                _logger.Debug("Added missing input sensor {Sensor} with condition-based value: {Value}", sensor, value);
                            }
                            else
                            {
                                // No condition info available, use a neutral value
                                // Use different values for positive and negative tests to avoid accidental triggers
                                allInputs[sensor] = isPositiveCase ? 50.0 : 0.0;
                                _logger.Debug("Added missing input sensor {Sensor} with neutral value", sensor);
                            }
                        }
                    }
                    
                    return allInputs.Select(i => new TestInput { Key = i.Key, Value = i.Value }).ToList();
                }

                // Check if this is a temporal rule
                bool isTemporalRule = _ruleAnalyzer.ConditionAnalyzer.HasTemporalCondition(rule.Conditions);
                
                if (testCase.Inputs.Count > 0)
                {
                    // For temporal rules, the basic test should be minimal or modified
                    if (isTemporalRule)
                    {
                        _logger.Debug("Rule {RuleName} has temporal conditions - modifying basic test", rule.Name);
                        
                        // For temporal rules, add a note but don't add expectations that would fail
                        // The proper test will be generated in the temporal scenario
                        var temporalStep = new TestStep
                        {
                            Name = "Basic test for temporal rule",
                            Description = "Note: This is a temporal rule that requires a sequence of values over time. See the temporal test scenario.",
                            Inputs = EnsureAllRequiredInputs(testCase.Inputs, true),
                            Delay = 500, // Default delay
                            // Don't add expectations for temporal outputs - they'll likely fail
                        };
                        
                        scenario.Steps.Add(temporalStep);
                    }
                    else
                    {
                        // Regular non-temporal rules get a normal positive test step
                        var positiveStep = new TestStep
                        {
                            Name = "Positive test case",
                            Description = "Test inputs that should trigger the rule",
                            Inputs = EnsureAllRequiredInputs(testCase.Inputs, true),
                            Delay = 500, // Default delay
                            Expectations = testCase
                                .Outputs.Select(o => new TestExpectation
                                {
                                    Key = o.Key,
                                    Expected = o.Value,
                                    Validator = GetValidatorType(o.Value),
                                    TimeoutMs = 1000 // Add timeout for rules to process
                                })
                                .ToList(),
                        };

                        scenario.Steps.Add(positiveStep);
                    }
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
                        Inputs = EnsureAllRequiredInputs(negativeCase.Inputs, false),
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
        private List<TestScenario> GenerateDependencyScenarios(
            RuleAnalysisResult analysis,
            Dictionary<string, List<RuleConditionPair>>? inputConditionMap = null
        )
        {
            _logger.Debug(
                "Generating dependency test scenarios for {DependencyCount} dependencies",
                analysis.Dependencies.Count
            );

            var scenarios = new List<TestScenario>();
            var allRequiredInputs = analysis.InputSensors;

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

                    // Get all the target rule's dependency requirements
                    // by analyzing its conditions
                    Dictionary<string, object> dependencyRequirements = new Dictionary<string, object>();
                    if (targetRule.Conditions != null)
                    {
                        dependencyRequirements = _ruleAnalyzer.ConditionAnalyzer.AnalyzeConditionRequirements(targetRule.Conditions);
                    }

                    // For each dependency, set appropriate values based on analysis
                    foreach (var dependency in dependencies)
                    {
                        var key = dependency.Key;
                        object value;

                        // First, try to get the value from our analyzed requirements
                        if (dependencyRequirements.TryGetValue(key, out var requiredValue))
                        {
                            value = requiredValue;
                            _logger.Debug("Using dependency value {Key} = {Value} based on target rule condition requirements", 
                                key, value);
                        }
                        else
                        {
                            // Otherwise try to find the value in the source rule's actions
                            var sourceRule = dependency.SourceRule;
                            var setValueAction = sourceRule.Actions
                                .OfType<SetValueAction>()
                                .FirstOrDefault(a => a.Key == key);

                            if (setValueAction?.Value != null)
                            {
                                // Use the direct value from the action
                                value = setValueAction.Value;
                                _logger.Debug("Using dependency value {Key} = {Value} from source rule direct action", 
                                    key, value);
                            }
                            else
                            {
                                // Last resort: use a default value based on analyzed rules
                                // Check if it's a boolean and use true, otherwise use 1.0
                                if (key.Contains("enabled") || key.Contains("status") || 
                                    key.Contains("active") || key.Contains("alarm") || 
                                    key.Contains("alert") || key.Contains("normal"))
                                {
                                    value = true;
                                }
                                else
                                {
                                    value = 1.0;
                                }
                                _logger.Debug("No direct value found for {Key}, using default value {Value}", key, value);
                            }
                        }

                        // Ensure the value is properly normalized (especially string booleans to real booleans)
                        preSetOutputs[key] = _ruleAnalyzer.ConditionAnalyzer.NormalizeValue(value);
                    }

                    scenario.PreSetOutputs = preSetOutputs;

                    // Generate a basic test case for the target rule
                    var testCase = _testCaseGenerator.GenerateBasicTestCase(targetRule);
                    
                    // For all actions in the target rule that use value expressions,
                    // find any input sensors referenced in those expressions
                    Dictionary<string, object> requiredInputs = new Dictionary<string, object>();
                    
                    // Extract input references from all rule actions in the target rule
                    foreach (var action in targetRule.Actions)
                    {
                        if (action is SetValueAction setAction && !string.IsNullOrEmpty(setAction.ValueExpression))
                        {
                            // Extract sensor references from the expression
                            var sensors = _ruleAnalyzer.ConditionAnalyzer.ExtractSensorsFromExpression(setAction.ValueExpression);
                            foreach (var sensor in sensors)
                            {
                                if (sensor.StartsWith("input:") && !requiredInputs.ContainsKey(sensor))
                                {
                                    // This input is required by an action expression - find any rules that reference it
                                    foreach (var rule in analysis.Rules)
                                    {
                                        if (rule.Conditions != null)
                                        {
                                            // Check if this rule references our input
                                            var ruleSensors = _ruleAnalyzer.ConditionAnalyzer.ExtractSensors(rule.Conditions);
                                            if (ruleSensors.Contains(sensor))
                                            {
                                                // Use the condition analyzer to get a suitable value
                                                var ruleRequirements = _ruleAnalyzer.ConditionAnalyzer.AnalyzeConditionRequirements(rule.Conditions);
                                                if (ruleRequirements.TryGetValue(sensor, out var sensorValue))
                                                {
                                                    requiredInputs[sensor] = sensorValue;
                                                    _logger.Debug("Found value {Value} for input {Sensor} from rule {Rule}", 
                                                        sensorValue, sensor, rule.Name);
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    
                                    // If we didn't find a value, use a default that's easily identified
                                    if (!requiredInputs.ContainsKey(sensor))
                                    {
                                        requiredInputs[sensor] = 1.0;
                                        _logger.Debug("Using default value 1.0 for required input {Sensor} - no rule conditions found", sensor);
                                    }
                                }
                            }
                        }
                    }
                    
                    // Add all required inputs to the test case
                    foreach (var (key, value) in requiredInputs)
                    {
                        if (!testCase.Inputs.ContainsKey(key))
                        {
                            testCase.Inputs[key] = value;
                        }
                    }

                    // Helper function to ensure all required inputs are included
                    List<TestInput> EnsureAllRequiredInputs(Dictionary<string, object> inputs, bool isPositiveCase)
                    {
                        var allInputs = new Dictionary<string, object>(inputs);
                        
                        // Add any missing inputs from the complete set of required inputs
                        foreach (var sensor in allRequiredInputs)
                        {
                            if (!allInputs.ContainsKey(sensor))
                            {
                                // If we have condition information for this sensor, use it to determine an appropriate value
                                if (inputConditionMap != null && inputConditionMap.TryGetValue(sensor, out var conditionPairs) && conditionPairs.Count > 0)
                                {
                                    // Use the first condition to determine a suitable value
                                    var pair = conditionPairs.First();
                                    var valueTarget = isPositiveCase ? ValueTarget.Positive : ValueTarget.Negative;
                                    var value = _testCaseGenerator.GenerateValueForSensor(sensor, pair.Condition, valueTarget);
                                    allInputs[sensor] = value;
                                    _logger.Debug("Added missing input sensor {Sensor} with condition-based value: {Value}", sensor, value);
                                }
                                else
                                {
                                    // No condition info available, use 1.0 for positive and 0.0 for negative
                                    allInputs[sensor] = isPositiveCase ? 1.0 : 0.0;
                                    _logger.Debug("Added missing input sensor {Sensor} with default value", sensor);
                                }
                            }
                        }
                        
                        return allInputs.Select(i => new TestInput { Key = i.Key, Value = i.Value }).ToList();
                    }

                    // Create the main test step
                    var step = new TestStep
                    {
                        Name = "Test with dependencies",
                        Description = "Tests rule with dependencies satisfied",
                        // Ensure all required inputs are included
                        Inputs = EnsureAllRequiredInputs(testCase.Inputs, true),
                        Delay = 500,
                        Expectations = testCase
                            .Outputs.Select(o => new TestExpectation
                            {
                                Key = o.Key,
                                Expected = o.Value,
                                Validator = GetValidatorType(o.Value),
                                TimeoutMs = 1000 // Add timeout for rules to process
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

                    // Use opposite/inverted values for each preset output
                    var oppositeOutputs = new Dictionary<string, object>();
                    foreach (var (key, value) in preSetOutputs)
                    {
                        // Normalize the value to ensure proper type
                        var normalizedValue = _ruleAnalyzer.ConditionAnalyzer.NormalizeValue(value);
                        
                        if (normalizedValue is bool b)
                        {
                            oppositeOutputs[key] = !b;
                        }
                        else if (normalizedValue is double d)
                        {
                            // Use an inverted value
                            oppositeOutputs[key] = d > 0 ? 0.0 : 1.0;
                        }
                        else
                        {
                            // For other values, use a distinct string
                            oppositeOutputs[key] = "different_value";
                        }
                    }

                    negativeScenario.PreSetOutputs = oppositeOutputs;

                    // Create the negative test step
                    var negativeStep = new TestStep
                    {
                        Name = "Test with missing dependencies",
                        Description = "Tests rule with dependencies not satisfied",
                        // Ensure all required inputs are included
                        Inputs = EnsureAllRequiredInputs(testCase.Inputs, false),
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
        private List<TestScenario> GenerateTemporalScenarios(
            List<RuleDefinition> temporalRules,
            HashSet<string> allRequiredInputs,
            Dictionary<string, List<RuleConditionPair>>? inputConditionMap = null
        )
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
                    // Generate a temporal test case for this rule
                    var scenario = _testCaseGenerator.GenerateTemporalTestCase(rule);
                    if (scenario != null)
                    {
                        // Add additional properties to enhance the latching behavior testing
                        
                        // 1. Set initial state to reflect the start condition
                        // This tests the "latching" behavior by explicitly setting initial states
                        var presetOutputs = new Dictionary<string, object>();
                        
                        // Initialize all outputs that might be affected by this rule to a known state
                        foreach (var action in rule.Actions)
                        {
                            if (action is SetValueAction setValueAction && setValueAction.Key.StartsWith("output:"))
                            {
                                // For boolean outputs, start with the opposite value to verify rule changes it
                                if (setValueAction.Value is bool boolValue)
                                {
                                    presetOutputs[setValueAction.Key] = !boolValue;
                                }
                                else
                                {
                                    // For non-boolean outputs, initialize to 0 or other neutral value
                                    presetOutputs[setValueAction.Key] = 0;
                                }
                            }
                        }
                        
                        // Only set preSetOutputs if we have values
                        if (presetOutputs.Count > 0)
                        {
                            scenario.PreSetOutputs = presetOutputs;
                        }
                        
                        // 2. Ensure all required inputs are included in each step of the sequence
                        if (scenario.InputSequence != null)
                        {
                            foreach (var sequenceInput in scenario.InputSequence)
                            {
                            // Get the existing inputs for this step
                            var existingInputs = sequenceInput.Inputs;
                            
                            // Add any missing inputs from the complete set of required inputs
                            foreach (var sensor in allRequiredInputs)
                            {
                                if (!existingInputs.ContainsKey(sensor))
                                {
                                    // If we have condition information for this sensor, use it to determine an appropriate value
                                    if (inputConditionMap != null && inputConditionMap.TryGetValue(sensor, out var conditionPairs) && conditionPairs.Count > 0)
                                    {
                                        // Use the first condition to determine a suitable value based on actual rule conditions
                                        var pair = conditionPairs.First();
                                        var value = _testCaseGenerator.GenerateValueForSensor(sensor, pair.Condition, ValueTarget.Positive);
                                        existingInputs[sensor] = value;
                                        _logger.Debug("Added missing input sensor {Sensor} with condition-based value to temporal step", sensor);
                                    }
                                    else
                                    {
                                        // No condition info available, use a neutral value that won't trigger edge cases
                                        existingInputs[sensor] = 50.0;
                                        _logger.Debug("Added missing input sensor {Sensor} with neutral value to temporal step", sensor);
                                    }
                                }
                            }
                        }
                        }
                        
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
        /// Gets a generic default value for a key
        /// </summary>
        private object GetDefaultValueForKey(string key)
        {
            // For keys that appear to represent boolean values
            if (key.EndsWith("_enabled") || key.EndsWith("_active") || key.EndsWith("_status"))
            {
                return true;
            }
            
            // For other outputs, prefer numeric values that won't trigger edge conditions
            // Use a mid-range value to minimize chance of unexpected interactions
            return 50.0;
        }
        
        /// <summary>
        /// Finds all conditions in a rule that reference a specific key
        /// </summary>
        private List<ConditionDefinition> FindConditionsReferencingKey(ConditionDefinition condition, string key)
        {
            var matchingConditions = new List<ConditionDefinition>();
            
            if (condition is ComparisonCondition comparison)
            {
                // Direct comparison with the key
                if (comparison.Sensor == key)
                {
                    matchingConditions.Add(comparison);
                }
                // Expression that might reference the key
                else if (!string.IsNullOrEmpty(comparison.ValueExpression) && 
                         comparison.ValueExpression.Contains(key))
                {
                    matchingConditions.Add(comparison);
                }
            }
            else if (condition is ThresholdOverTimeCondition temporal)
            {
                if (temporal.Sensor == key)
                {
                    matchingConditions.Add(temporal);
                }
            }
            else if (condition is ExpressionCondition expression)
            {
                if (!string.IsNullOrEmpty(expression.Expression) && 
                    expression.Expression.Contains(key))
                {
                    matchingConditions.Add(expression);
                }
            }
            else if (condition is ConditionGroup group)
            {
                // Process 'all' conditions
                foreach (var wrapper in group.All)
                {
                    if (wrapper.Condition != null)
                    {
                        matchingConditions.AddRange(FindConditionsReferencingKey(wrapper.Condition, key));
                    }
                }

                // Process 'any' conditions
                foreach (var wrapper in group.Any)
                {
                    if (wrapper.Condition != null)
                    {
                        matchingConditions.AddRange(FindConditionsReferencingKey(wrapper.Condition, key));
                    }
                }
            }
            
            return matchingConditions;
        }

        /// <summary>
        /// Gets appropriate validator type for a value
        /// </summary>
        private string GetValidatorType(object? value)
        {
            // For null values, use string validator which works better for checking nulls
            if (value == null)
                return "string";
            
            // Use appropriate validators based on actual type
            if (value is bool)
                return "boolean";
            if (value is double || value is int || value is float)
                return "numeric";
                
            // For string values check if they're boolean or numeric in disguise
            if (value is string stringValue)
            {
                if (stringValue.Equals("true", StringComparison.OrdinalIgnoreCase) || 
                    stringValue.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return "boolean";
                    
                if (double.TryParse(stringValue, out _))
                    return "numeric";
            }
            
            // Default to string for all other types
            return "string";
        }
    }
}
