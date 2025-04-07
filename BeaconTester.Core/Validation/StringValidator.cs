namespace BeaconTester.Core.Validation
{
    /// <summary>
    /// Validates string values
    /// </summary>
    public class StringValidator : IValidator
    {
        private string? _errorDetails;

        /// <summary>
        /// Validates string values, with optional case and trimming options
        /// </summary>
        public bool Validate(object? expected, object? actual, ValidationOptions options)
        {
            _errorDetails = null;

            // Special case for "__any__" which means "accept any value"
            if (expected is string expectedStr && expectedStr == "__any__")
            {
                // For __any__, just check that the actual value exists
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

            string expectedString = expected.ToString() ?? string.Empty;
            string actualString = actual.ToString() ?? string.Empty;

            if (options.TrimStrings)
            {
                expectedString = expectedString.Trim();
                actualString = actualString.Trim();
            }

            bool isMatch = options.IgnoreCase
                ? string.Equals(expectedString, actualString, StringComparison.OrdinalIgnoreCase)
                : string.Equals(expectedString, actualString, StringComparison.Ordinal);

            if (isMatch)
                return true;

            _errorDetails = $"Expected '{expectedString}', but got '{actualString}'";
            return false;
        }

        /// <summary>
        /// Gets details of any validation errors
        /// </summary>
        public string? GetErrorDetails() => _errorDetails;
    }
}