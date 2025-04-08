using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Text.RegularExpressions;
using Serilog;

namespace BeaconTester.Core.Validation
{
    /// <summary>
    /// Evaluates C# expressions for rule testing, using the same patterns as Beacon runtime
    /// </summary>
    public class ExpressionEvaluator
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, ScriptRunner<object>> _scriptCache = new();
        private readonly object _cacheLock = new();

        public ExpressionEvaluator(ILogger logger)
        {
            _logger = logger.ForContext<ExpressionEvaluator>();
        }

        /// <summary>
        /// Evaluates a rule expression with the provided input values
        /// </summary>
        /// <param name="expression">The expression to evaluate</param>
        /// <param name="inputs">Dictionary of input values</param>
        /// <returns>The result of evaluating the expression</returns>
        public async Task<object?> EvaluateAsync(string expression, Dictionary<string, object?> inputs)
        {
            try
            {
                string csharpExpression = TranslateExpression(expression, inputs);
                _logger.Debug("Translated expression '{Original}' to '{Translated}'", expression, csharpExpression);

                // Handle simple constant expressions directly
                if (TryEvaluateConstantExpression(csharpExpression, out var constantResult))
                {
                    return constantResult;
                }
                
                // Create globals object with inputs
                var globals = new ExpressionGlobals
                {
                    Inputs = inputs
                };

                // Try to get from cache or create new script
                var runner = GetCachedScriptRunner(csharpExpression);
                
                // Run the script
                var result = await runner.Invoke(globals);
                _logger.Debug("Expression '{Expression}' evaluated to {Result}", expression, result);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error evaluating expression '{Expression}'", expression);
                throw new ExpressionEvaluationException($"Failed to evaluate expression: {expression}", ex);
            }
        }

        /// <summary>
        /// Attempts to evaluate a constant expression without using the script engine
        /// </summary>
        private bool TryEvaluateConstantExpression(string expression, out object? result)
        {
            expression = expression.Trim();

            // Boolean constants
            if (expression == "true")
            {
                result = true;
                return true;
            }
            if (expression == "false")
            {
                result = false;
                return true;
            }
            
            // Numeric constants
            if (double.TryParse(expression, out var numericValue))
            {
                result = numericValue;
                return true;
            }
            
            // String constants (with quotes)
            if ((expression.StartsWith("\"") && expression.EndsWith("\"")) ||
                (expression.StartsWith("'") && expression.EndsWith("'")))
            {
                result = expression.Substring(1, expression.Length - 2);
                return true;
            }
            
            result = null;
            return false;
        }

        /// <summary>
        /// Translates a rule expression to valid C# code
        /// </summary>
        private string TranslateExpression(string expression, Dictionary<string, object?> inputs)
        {
            // Handle simple expressions directly
            if (expression.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                return "true";
            if (expression.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
                return "false";

            // Handle string template expressions (expressions with double quotes)
            // These are typically string concatenation templates
            bool isStringTemplate = false;
            if (expression.Contains("\""))
            {
                isStringTemplate = true;
                _logger.Debug("Detected string template: {Expression}", expression);
            }

            // Replace input references with dictionary lookups
            var translatedExpression = Regex.Replace(expression, @"(input:[a-zA-Z0-9_]+)", match =>
            {
                string key = match.Groups[1].Value;
                if (inputs.ContainsKey(key))
                {
                    var value = inputs[key];
                    if (value == null)
                    {
                        return "null";
                    }
                    else if (value is double || value is int || value is float || value is decimal)
                    {
                        return $"GetDouble(\"{key}\")";
                    }
                    else if (value is bool)
                    {
                        return $"GetBoolean(\"{key}\")";
                    }
                    else
                    {
                        return $"GetString(\"{key}\")";
                    }
                }
                return $"GetDouble(\"{key}\")"; // Default to double for unknown types
            });

            // Replace state references
            translatedExpression = Regex.Replace(translatedExpression, @"(state:[a-zA-Z0-9_]+)", match =>
            {
                string key = match.Groups[1].Value;
                if (inputs.ContainsKey(key))
                {
                    var value = inputs[key];
                    if (value == null)
                    {
                        return "null";
                    }
                    else if (value is double || value is int || value is float || value is decimal)
                    {
                        return $"GetDouble(\"{key}\")";
                    }
                    else if (value is bool)
                    {
                        return $"GetBoolean(\"{key}\")";
                    }
                    else
                    {
                        return $"GetString(\"{key}\")";
                    }
                }
                return $"GetDouble(\"{key}\")"; // Default to double for unknown types
            });

            // Replace output references
            translatedExpression = Regex.Replace(translatedExpression, @"(output:[a-zA-Z0-9_]+)", match =>
            {
                string key = match.Groups[1].Value;
                if (inputs.ContainsKey(key))
                {
                    var value = inputs[key];
                    if (value == null)
                    {
                        return "null";
                    }
                    else if (value is double || value is int || value is float || value is decimal)
                    {
                        return $"GetDouble(\"{key}\")";
                    }
                    else if (value is bool)
                    {
                        return $"GetBoolean(\"{key}\")";
                    }
                    else
                    {
                        return $"GetString(\"{key}\")";
                    }
                }
                return $"GetDouble(\"{key}\")"; // Default to double for unknown types
            });

            // Replace logical operators
            translatedExpression = translatedExpression
                .Replace(" and ", " && ")
                .Replace(" or ", " || ")
                .Replace(" not ", " ! ");
                
            // For string literals and templates, special handling to avoid escaping issues
            if (expression.Contains("\""))
            {
                try
                {
                    // Special handling for string literals and string concatenation expressions
                    if (expression.StartsWith("\"") || expression.Contains("\"") && expression.Contains("+"))
                    {
                        // Replace any escaped quotes with temporary markers
                        var processedExpr = expression.Replace("\\\"", "##QUOTE##");
                        
                        // For string concat expressions, manually evaluate them
                        if (processedExpr.Contains("+"))
                        {
                            var parts = processedExpr.Split('+');
                            var resultParts = new List<string>();
                            
                            foreach (var part in parts)
                            {
                                var trimmedPart = part.Trim();
                                
                                // Handle string literals
                                if (trimmedPart.StartsWith("\"") && trimmedPart.EndsWith("\""))
                                {
                                    // Extract string content without quotes
                                    var content = trimmedPart.Substring(1, trimmedPart.Length - 2);
                                    // Restore any escaped quotes
                                    content = content.Replace("##QUOTE##", "\"");
                                    resultParts.Add($"\"{content}\"");
                                }
                                // Handle input/state/output references
                                else if (Regex.IsMatch(trimmedPart, @"(input:|state:|output:)[a-zA-Z0-9_]+"))
                                {
                                    // Replace with ToString() call to handle concatenation properly
                                    var match = Regex.Match(trimmedPart, @"(input:|state:|output:)[a-zA-Z0-9_]+");
                                    if (match.Success)
                                    {
                                        string key = match.Groups[0].Value;
                                        resultParts.Add($"GetString(\"{key}\")");
                                    }
                                    else
                                    {
                                        resultParts.Add(trimmedPart);
                                    }
                                }
                                else
                                {
                                    resultParts.Add(trimmedPart);
                                }
                            }
                            
                            // Create a string concatenation expression with proper conversions
                            var stringConcatExpression = string.Join(" + ", resultParts);
                            _logger.Debug("String concatenation expression: {Expression}", stringConcatExpression);
                            return stringConcatExpression;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error processing string template, falling back to normal evaluation");
                }
            }

            return translatedExpression;
        }

        /// <summary>
        /// Gets or creates a cached script runner for the given expression
        /// </summary>
        private ScriptRunner<object> GetCachedScriptRunner(string expression)
        {
            lock (_cacheLock)
            {
                if (_scriptCache.TryGetValue(expression, out var cachedRunner))
                {
                    return cachedRunner;
                }

                // Create a new script with globals
                var scriptOptions = ScriptOptions.Default
                    .WithImports("System", "System.Math", "System.Collections.Generic")
                    .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Release);

                var script = CSharpScript.Create<object>(expression, scriptOptions, typeof(ExpressionGlobals));
                var runner = script.CreateDelegate();
                
                // Cache the compiled script
                _scriptCache[expression] = runner;
                return runner;
            }
        }
    }

    /// <summary>
    /// Globals for script execution
    /// </summary>
    public class ExpressionGlobals
    {
        public Dictionary<string, object?> Inputs { get; set; } = new();

        /// <summary>
        /// Gets a double value from inputs or returns 0 if not found or not convertible
        /// </summary>
        public double GetDouble(string key)
        {
            if (Inputs.TryGetValue(key, out var value))
            {
                if (value == null)
                    return 0;

                if (value is double d)
                    return d;
                if (value is int i)
                    return i;
                if (value is float f)
                    return f;
                if (value is decimal dec)
                    return (double)dec;
                if (value is bool b)
                    return b ? 1 : 0;
                if (value is string s && double.TryParse(s, out var parsed))
                    return parsed;
            }
            return 0;
        }

        /// <summary>
        /// Gets a boolean value from inputs or returns false if not found or not convertible
        /// </summary>
        public bool GetBoolean(string key)
        {
            if (Inputs.TryGetValue(key, out var value))
            {
                if (value == null)
                    return false;

                if (value is bool b)
                    return b;
                if (value is int i)
                    return i != 0;
                if (value is double d)
                    return d != 0;
                if (value is string s)
                {
                    if (bool.TryParse(s, out var parsedBool))
                        return parsedBool;
                    
                    s = s.Trim().ToLowerInvariant();
                    return s == "true" || s == "yes" || s == "1" || s == "on";
                }
            }
            return false;
        }

        /// <summary>
        /// Gets a string value from inputs or returns empty string if not found
        /// </summary>
        public string GetString(string key)
        {
            if (Inputs.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }
    }

    /// <summary>
    /// Exception thrown when expression evaluation fails
    /// </summary>
    public class ExpressionEvaluationException : Exception
    {
        public ExpressionEvaluationException(string message) : base(message) { }
        public ExpressionEvaluationException(string message, Exception innerException) : base(message, innerException) { }
    }
}