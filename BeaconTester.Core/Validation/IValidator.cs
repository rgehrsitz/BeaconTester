namespace BeaconTester.Core.Validation
{
    /// <summary>
    /// Interface for validating test expectations
    /// </summary>
    public interface IValidator
    {
        /// <summary>
        /// Validates that an actual value matches an expected value
        /// </summary>
        /// <param name="expected">The expected value</param>
        /// <param name="actual">The actual value</param>
        /// <param name="options">Additional validation options</param>
        /// <returns>True if validation passes, false otherwise</returns>
        bool Validate(object? expected, object? actual, ValidationOptions options);

        /// <summary>
        /// Gets a description of any validation errors
        /// </summary>
        string? GetErrorDetails();
    }

    /// <summary>
    /// Additional options for validation
    /// </summary>
    public class ValidationOptions
    {
        /// <summary>
        /// Numeric tolerance for comparisons
        /// </summary>
        public double Tolerance { get; set; } = 0.0001;

        /// <summary>
        /// Whether to ignore case in string comparisons
        /// </summary>
        public bool IgnoreCase { get; set; } = false;

        /// <summary>
        /// Whether to trim strings before comparison
        /// </summary>
        public bool TrimStrings { get; set; } = false;
    }
}
