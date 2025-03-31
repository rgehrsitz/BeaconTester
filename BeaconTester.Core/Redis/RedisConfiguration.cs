namespace BeaconTester.Core.Redis
{
    /// <summary>
    /// Configuration for Redis connection
    /// </summary>
    public class RedisConfiguration
    {
        /// <summary>
        /// Redis endpoints (host:port)
        /// </summary>
        public List<string> Endpoints { get; set; } = new List<string>();

        /// <summary>
        /// Optional Redis password
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Whether to use SSL for the connection
        /// </summary>
        public bool Ssl { get; set; } = false;

        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        public int ConnectTimeout { get; set; } = 5000;

        /// <summary>
        /// Sync operation timeout in milliseconds
        /// </summary>
        public int SyncTimeout { get; set; } = 5000;

        /// <summary>
        /// Whether to allow admin commands
        /// </summary>
        public bool AllowAdmin { get; set; } = false;

        /// <summary>
        /// Number of connection retry attempts
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// Base delay between retry attempts in milliseconds
        /// </summary>
        public int RetryBaseDelayMs { get; set; } = 200;

        /// <summary>
        /// Size of the connection pool
        /// </summary>
        public int PoolSize { get; set; } = 3;

        /// <summary>
        /// Default constructor sets localhost endpoint
        /// </summary>
        public RedisConfiguration()
        {
            Endpoints.Add("localhost:6379");
        }
    }
}
