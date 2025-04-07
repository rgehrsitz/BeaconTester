namespace BeaconTester.Core.Validation
{
    /// <summary>
    /// Validates numeric values with tolerance
    /// </summary>
    public class NumericValidator : IValidator
    {
        private string? _errorDetails;

        /// <summary>
        /// Validates numeric values, with conversion from strings and tolerance
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

            double expectedNumber;
            if (expected is double ed)
            {
                expectedNumber = ed;
            }
            else if (expected is int ei)
            {
                expectedNumber = ei;
            }
            else if (expected is float ef)
            {
                expectedNumber = ef;
            }
            else if (expected is long el)
            {
                expectedNumber = el;
            }
            else if (expected is string es && double.TryParse(es, out double esd))
            {
                expectedNumber = esd;
            }
            else
            {
                _errorDetails = $"Expected value is not a number: {expected}";
                return false;
            }

            double actualNumber;
            if (actual is double ad)
            {
                actualNumber = ad;
            }
            else if (actual is int ai)
            {
                actualNumber = ai;
            }
            else if (actual is float af)
            {
                actualNumber = af;
            }
            else if (actual is long al)
            {
                actualNumber = al;
            }
            else if (actual is string asValue && double.TryParse(asValue, out double asd))
            {
                actualNumber = asd;
            }
            else
            {
                _errorDetails = $"Actual value is not a number: {actual}";
                return false;
            }

            // Compare with tolerance
            if (Math.Abs(expectedNumber - actualNumber) <= options.Tolerance)
                return true;

            _errorDetails =
                $"Expected {expectedNumber}, but got {actualNumber} (tolerance: {options.Tolerance})";
            return false;
        }

        /// <summary>
        /// Gets details of any validation errors
        /// </summary>
        public string? GetErrorDetails() => _errorDetails;
    }
}