using System.CommandLine;
using BeaconTester.Core.Validation;
using Serilog;

namespace BeaconTester.Runner.Commands
{
    /// <summary>
    /// Command to test expression evaluation
    /// </summary>
    public class TestExpressionCommand
    {
        /// <summary>
        /// Creates the test-expression command
        /// </summary>
        public Command Create()
        {
            var command = new Command("test-expression", "Test the expression evaluator");

            // Add options
            var expressionOption = new Option<string>(
                name: "--expression",
                description: "The expression to evaluate"
            )
            {
                IsRequired = true,
            };

            var inputsOption = new Option<string>(
                name: "--inputs",
                description: "Input key-value pairs in the format key1=value1,key2=value2"
            );

            command.AddOption(expressionOption);
            command.AddOption(inputsOption);

            // Set handler
            command.SetHandler(
                (expression, inputs) => HandleTestExpressionCommand(expression, inputs),
                expressionOption,
                inputsOption
            );

            return command;
        }

        /// <summary>
        /// Handles the test-expression command
        /// </summary>
        private async Task<int> HandleTestExpressionCommand(string expression, string? inputs)
        {
            var logger = Log.Logger.ForContext<TestExpressionCommand>();

            try
            {
                logger.Information("Evaluating expression: {Expression}", expression);

                // Parse inputs
                var inputsDict = new Dictionary<string, object?>();
                if (!string.IsNullOrEmpty(inputs))
                {
                    var pairs = inputs.Split(',');
                    foreach (var pair in pairs)
                    {
                        var keyValue = pair.Split('=');
                        if (keyValue.Length == 2)
                        {
                            var key = keyValue[0].Trim();
                            var value = ParseValue(keyValue[1].Trim());
                            inputsDict[key] = value;
                            logger.Information("Input: {Key} = {Value} ({Type})", 
                                key, value, value?.GetType().Name ?? "null");
                        }
                    }
                }
                else
                {
                    // Add some default values
                    inputsDict["input:temperature"] = 25.0;
                    inputsDict["input:humidity"] = 50.0;
                    inputsDict["input:pressure"] = 1013.0;
                    inputsDict["input:status"] = "active";
                    inputsDict["input:enabled"] = true;
                    
                    logger.Information("Using default input values");
                }

                // Handle string templates directly
                if (expression.Contains("\"") && expression.Contains("+"))
                {
                    logger.Information("Directly evaluating string template expression");
                    string stringResult = "";
                    var parts = expression.Split('+');
                    foreach (var part in parts)
                    {
                        var trimmedPart = part.Trim();
                        
                        // Handle string literals
                        if (trimmedPart.StartsWith("\"") && trimmedPart.EndsWith("\""))
                        {
                            stringResult += trimmedPart.Substring(1, trimmedPart.Length - 2);
                        }
                        // Handle input references
                        else if (trimmedPart.Contains("input:"))
                        {
                            var key = trimmedPart.Trim();
                            if (inputsDict.TryGetValue(key, out var value))
                            {
                                stringResult += value?.ToString() ?? "null";
                            }
                            else
                            {
                                stringResult += $"{{{key}}}";
                            }
                        }
                        else
                        {
                            stringResult += trimmedPart;
                        }
                    }
                    
                    logger.Information("Result: {Result} (Directly Evaluated String)", stringResult);
                    return 0;
                }

                // Create an expression evaluator and evaluate the expression
                var evaluator = new ExpressionEvaluator(logger);
                var result = await evaluator.EvaluateAsync(expression, inputsDict);

                logger.Information("Result: {Result} ({Type})", 
                    result, result?.GetType().Name ?? "null");

                return 0;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error evaluating expression");
                return 1;
            }
        }

        /// <summary>
        /// Parses a string value into an appropriate type
        /// </summary>
        private object? ParseValue(string value)
        {
            // Try to parse as boolean
            if (bool.TryParse(value, out var boolResult))
                return boolResult;

            // Try to parse as numeric
            if (double.TryParse(value, out var doubleResult))
                return doubleResult;

            // Return as string
            return value;
        }
    }
}