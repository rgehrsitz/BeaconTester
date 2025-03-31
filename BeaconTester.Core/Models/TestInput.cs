namespace BeaconTester.Core.Models
{
    /// <summary>
    /// Represents an input to send to Redis
    /// </summary>
    public class TestInput
    {
        /// <summary>
        /// The Redis key to set
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// The value to set
        /// </summary>
        public object Value { get; set; } = new object();

        /// <summary>
        /// The format to use when storing in Redis (hash, string, JSON)
        /// </summary>
        public RedisDataFormat Format { get; set; } = RedisDataFormat.Auto;

        /// <summary>
        /// For hash format, the optional field name (if not specified, parsed from key)
        /// </summary>
        public string? Field { get; set; }
    }

    /// <summary>
    /// Format of data stored in Redis
    /// </summary>
    public enum RedisDataFormat
    {
        /// <summary>
        /// Automatically determine based on key format and conventions
        /// </summary>
        Auto,

        /// <summary>
        /// Store as a Redis string
        /// </summary>
        String,

        /// <summary>
        /// Store as a field in a Redis hash
        /// </summary>
        Hash,

        /// <summary>
        /// Store as a JSON document
        /// </summary>
        Json,

        /// <summary>
        /// Publish to a Redis channel
        /// </summary>
        Pub,
    }
}
