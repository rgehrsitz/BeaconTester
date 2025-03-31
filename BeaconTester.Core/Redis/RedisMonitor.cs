using System.Collections.Concurrent;
using Serilog;
using StackExchange.Redis;

namespace BeaconTester.Core.Redis
{
    /// <summary>
    /// Monitors Redis for changes to keys
    /// </summary>
    public class RedisMonitor : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConnectionMultiplexer _redis;
        private readonly ConcurrentDictionary<string, DateTime> _keyChanges = new();
        private readonly ConcurrentDictionary<string, string> _keyValues = new();
        private readonly ConcurrentDictionary<
            RedisChannel,
            Action<RedisChannel, RedisValue>
        > _subscriptions = new();
        private ISubscriber? _subscriber;
        private bool _disposed;

        /// <summary>
        /// Creates a new Redis monitor
        /// </summary>
        public RedisMonitor(RedisConfiguration config, ILogger logger)
        {
            _logger = logger.ForContext<RedisMonitor>();

            try
            {
                var redisOptions = new ConfigurationOptions
                {
                    AbortOnConnectFail = false,
                    ConnectTimeout = config.ConnectTimeout,
                    SyncTimeout = config.SyncTimeout,
                    Password = config.Password,
                    Ssl = config.Ssl,
                    AllowAdmin = config.AllowAdmin,
                };

                // Add all endpoints
                foreach (var endpoint in config.Endpoints)
                {
                    redisOptions.EndPoints.Add(endpoint);
                }

                _logger.Information(
                    "Connecting Redis monitor to {Endpoints}",
                    string.Join(", ", config.Endpoints)
                );
                _redis = ConnectionMultiplexer.Connect(redisOptions);
                _logger.Information("Redis monitor connection established");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to connect Redis monitor");
                throw;
            }
        }

        /// <summary>
        /// Starts monitoring a specific Redis key pattern using keyspace notifications
        /// </summary>
        public void StartMonitoring(string keyPattern)
        {
            try
            {
                var endpoints = _redis.GetEndPoints();
                var server = _redis.GetServer(endpoints.First());

                // Ensure keyspace notifications are enabled
                server.ConfigSet("notify-keyspace-events", "KEA");

                // Subscribe to keyspace events
                _subscriber = _redis.GetSubscriber();

                // Subscribe to all database keyspace events
                var channel = new RedisChannel(
                    "__keyspace@*__:" + keyPattern,
                    RedisChannel.PatternMode.Pattern
                );

                _subscriber.Subscribe(
                    channel,
                    (ch, val) =>
                    {
                        string keyName = ch.ToString().Split(':')[1];
                        _keyChanges[keyName] = DateTime.UtcNow;

                        // Get the new value
                        Task.Run(async () =>
                        {
                            try
                            {
                                var db = _redis.GetDatabase();
                                var value = await db.StringGetAsync(keyName);
                                if (value.HasValue)
                                {
                                    _keyValues[keyName] = value.ToString();
                                    _logger.Debug("Key {Key} changed to {Value}", keyName, value);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to get value for key {Key}", keyName);
                            }
                        });
                    }
                );

                _logger.Information(
                    "Started monitoring keys matching pattern: {Pattern}",
                    keyPattern
                );
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to start Redis monitoring for pattern {Pattern}",
                    keyPattern
                );
                throw;
            }
        }

        /// <summary>
        /// Subscribe to a Redis channel
        /// </summary>
        public void Subscribe(string channel, Action<string> handler)
        {
            try
            {
                _subscriber ??= _redis.GetSubscriber();

                var redisChannel = new RedisChannel(channel, RedisChannel.PatternMode.Literal);

                Action<RedisChannel, RedisValue> redisHandler = (ch, val) =>
                {
                    try
                    {
                        handler(val.ToString());
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error in Redis channel handler for {Channel}", channel);
                    }
                };

                _subscriptions[redisChannel] = redisHandler;
                _subscriber.Subscribe(redisChannel, redisHandler);

                _logger.Information("Subscribed to Redis channel: {Channel}", channel);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to subscribe to Redis channel {Channel}", channel);
                throw;
            }
        }

        /// <summary>
        /// Gets all key changes since a specific time
        /// </summary>
        public Dictionary<string, DateTime> GetChangesSince(DateTime since)
        {
            return _keyChanges
                .Where(kv => kv.Value > since)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        /// <summary>
        /// Gets the latest value for a key
        /// </summary>
        public string? GetLatestValue(string key)
        {
            _keyValues.TryGetValue(key, out var value);
            return value;
        }

        /// <summary>
        /// Gets all current key values
        /// </summary>
        public Dictionary<string, string> GetAllValues()
        {
            return _keyValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            // Unsubscribe from all channels
            if (_subscriber != null)
            {
                foreach (var (channel, handler) in _subscriptions)
                {
                    _subscriber.Unsubscribe(channel, handler);
                }
            }

            _redis.Dispose();
            _disposed = true;
        }
    }
}
