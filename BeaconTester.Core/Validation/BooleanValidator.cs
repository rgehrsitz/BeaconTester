namespace BeaconTester.Core.Validation
{
    /// <summary>
    /// Validates boolean values
    /// </summary>
    public class BooleanValidator : IValidator
    {
        private string? _errorDetails;

        /// <summary>
        /// Validates boolean values, with conversion from strings and other types
        /// </summary>
        public bool Validate(object? expected, object? actual, ValidationOptions options)
        {
            _errorDetails = null;

            // Special case for "__any__" which means "accept any value"
            if (expected is string expectedStr && expectedStr == "__any__")
            {
                if (actual != null)
                {
                    return true;
                }
                
                _errorDetails = "Expected any value, but got null";
                return false;
            }

            if (expected == null && actual == null)
                return true;

            if (expected == null || actual == null)
            {
                _errorDetails = $"Expected {expected}, but got {actual}";
                return false;
            }

            bool expectedBool;
            if (expected is bool eb)
            {
                expectedBool = eb;
            }
            else if (expected is string es)
            {
                if (bool.TryParse(es, out bool parsed))
                {
                    expectedBool = parsed;
                }
                else if (
                    string.Equals(es, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(es, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(es, "y", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(es, "true", StringComparison.OrdinalIgnoreCase)
                )
                {
                    expectedBool = true;
                }
                else if (
                    string.Equals(es, "0", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(es, "no", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(es, "n", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(es, "false", StringComparison.OrdinalIgnoreCase)
                )
                {
                    expectedBool = false;
                }
                else
                {
                    _errorDetails = $"Could not convert expected value '{es}' to boolean";
                    return false;
                }
            }
            else if (expected is int ei)
            {
                expectedBool = ei != 0;
            }
            else
            {
                _errorDetails = $"Expected value is not a boolean: {expected}";
                return false;
            }

            bool actualBool;
            if (actual is bool ab)
            {
                actualBool = ab;
            }
            else if (actual is string asString)
            {
                if (bool.TryParse(asString, out bool parsed))
                {
                    actualBool = parsed;
                }
                else if (
                    string.Equals(asString, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(asString, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(asString, "y", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(asString, "true", StringComparison.OrdinalIgnoreCase)
                )
                {
                    actualBool = true;
                }
                else if (
                    string.Equals(asString, "0", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(asString, "no", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(asString, "n", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(asString, "false", StringComparison.OrdinalIgnoreCase)
                )
                {
                    actualBool = false;
                }
                else
                {
                    _errorDetails = $"Could not convert actual value '{asString}' to boolean";
                    return false;
                }
            }
            else if (actual is int ai)
            {
                actualBool = ai != 0;
            }
            else
            {
                _errorDetails = $"Actual value is not a boolean: {actual}";
                return false;
            }

            if (expectedBool == actualBool)
                return true;

            _errorDetails = $"Expected {expectedBool}, but got {actualBool}";
            return false;
        }

        /// <summary>
        /// Gets details of any validation errors
        /// </summary>
        public string? GetErrorDetails() => _errorDetails;
    }
}