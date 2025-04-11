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
        /// Maximum time to wait for the condition to be met (for asynchronous tests).
        /// If specified, this takes precedence over TimeoutMultiplier.
        /// </summary>
        public int? TimeoutMs { get; set; }

        /// <summary>
        /// Number of Beacon cycles to wait for the condition to be met.
        /// This is used only if TimeoutMs is not set, and provides a more cycle-aware alternative.
        /// </summary>
        public int? TimeoutMultiplier { get; set; }

        /// <summary>
        /// Polling interval for checking conditions with timeouts in milliseconds.
        /// If specified, this takes precedence over PollingIntervalFactor.
        /// </summary>
        public int? PollingIntervalMs { get; set; }

        /// <summary>
        /// Factor to multiply by Beacon cycle time to determine polling interval.
        /// Default of 1.1 means to poll just after a cycle should have completed.
        /// This is used only if PollingIntervalMs is not set.
        /// </summary>
        public double? PollingIntervalFactor { get; set; }
    }
}
