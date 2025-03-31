namespace BeaconTester.Core.Models
{
    /// <summary>
    /// Represents an expected output from a rule
    /// </summary>
    public class TestExpectation
    {
        /// <summary>
        /// The Redis key to check
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// For hash format, the field name (if not specified, parsed from key)
        /// </summary>
        public string? Field { get; set; }

        /// <summary>
        /// The expected value
        /// </summary>
        public object? Expected { get; set; }

        /// <summary>
        /// Type of validator to use
        /// </summary>
        public string Validator { get; set; } = "auto";

        /// <summary>
        /// The format to use when reading from Redis (hash, string, JSON)
        /// </summary>
        public RedisDataFormat Format { get; set; } = RedisDataFormat.Auto;

        /// <summary>
        /// Tolerance for numeric comparisons
        /// </summary>
        public double? Tolerance { get; set; }

        /// <summary>
        /// Maximum time to wait for the condition to be met (for asynchronous tests)
        /// </summary>
        public int? TimeoutMs { get; set; }

        /// <summary>
        /// Polling interval for checking conditions with timeouts
        /// </summary>
        public int? PollingIntervalMs { get; set; } = 100;
    }
}
