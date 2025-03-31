using System.Text.Json;
using BeaconTester.Core.Models;
using Polly;
using Serilog;
using StackExchange.Redis;

namespace BeaconTester.Core.Redis
{
    /// <summary>
    /// Handles communication with Redis for test operations
    /// </summary>
    public class RedisAdapter : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly TimeSpan _errorThrottleWindow = TimeSpan.FromSeconds(60);
        private readonly Dictionary<string, DateTime> _lastErrorTime = new();
        private readonly JsonSerializerOptions _jsonOptions =
            new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private bool _disposed;

        // Redis key prefixes
        public const string INPUT_PREFIX = "sensors:";
        public const string OUTPUT_PREFIX = "outputs:";
        public const string HISTORY_PREFIX = "history:";

        /// <summary>
        /// Creates a new Redis adapter with the specified configuration
        /// </summary>
        public RedisAdapter(RedisConfiguration config, ILogger logger)
        {
            _logger = logger.ForContext<RedisAdapter>();

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
                    "Connecting to Redis at {Endpoints}",
                    string.Join(", ", config.Endpoints)
                );
                _redis = ConnectionMultiplexer.Connect(redisOptions);
                _db = _redis.GetDatabase();
                _logger.Information("Redis connection established");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to connect to Redis");
                throw;
            }
        }

        /// <summary>
        /// Sends test inputs to Redis
        /// </summary>
        public async Task SendInputsAsync(List<TestInput> inputs)
        {
            if (inputs == null || inputs.Count == 0)
                return;

            try
            {
                foreach (var input in inputs)
                {
                    string key = input.Key;
                    string? field = input.Field;

                    // Apply format conventions if needed
                    if (input.Format == RedisDataFormat.Auto)
                    {
                        (key, field, var format) = DetermineKeyFormat(input.Key, input.Field);
                        input.Format = format;
                    }

                    // Handle the input based on its format
                    switch (input.Format)
                    {
                        case RedisDataFormat.String:
                            await _db.StringSetAsync(key, input.Value.ToString());
                            _logger.Debug("Set string {Key} = {Value}", key, input.Value);
                            break;

                        case RedisDataFormat.Hash:
                            if (string.IsNullOrEmpty(field))
                            {
                                _logger.Warning(
                                    "Hash field is required for hash format input {Key}",
                                    key
                                );
                                continue;
                            }

                            await _db.HashSetAsync(key, field, input.Value.ToString());
                            _logger.Debug(
                                "Set hash {Key}:{Field} = {Value}",
                                key,
                                field,
                                input.Value
                            );
                            break;

                        case RedisDataFormat.Json:
                            string json = input.Value is string s
                                ? s
                                : JsonSerializer.Serialize(input.Value, _jsonOptions);
                            await _db.StringSetAsync(key, json);
                            _logger.Debug("Set JSON {Key} = {Value}", key, json);
                            break;

                        case RedisDataFormat.Pub:
                            var subscriber = _redis.GetSubscriber();
                            var channel = RedisChannel.Literal(key);
                            await subscriber.PublishAsync(channel, input.Value.ToString());
                            _logger.Debug("Published to {Channel}: {Message}", key, input.Value);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ShouldThrottleError("SendInputs"))
                {
                    _logger.Error(ex, "Failed to send inputs to Redis");
                }
                throw;
            }
        }

        /// <summary>
        /// Retrieves and validates expectations from Redis
        /// </summary>
        public async Task<List<ExpectationResult>> CheckExpectationsAsync(
            List<TestExpectation> expectations
        )
        {
            var results = new List<ExpectationResult>();

            if (expectations == null || expectations.Count == 0)
                return results;

            try
            {
                foreach (var expectation in expectations)
                {
                    string key = expectation.Key;
                    string? field = expectation.Field;

                    // Apply format conventions if needed
                    if (expectation.Format == RedisDataFormat.Auto)
                    {
                        (key, field, var format) = DetermineKeyFormat(
                            expectation.Key,
                            expectation.Field
                        );
                        expectation.Format = format;
                    }

                    // Create a result object
                    var result = new ExpectationResult
                    {
                        Key = expectation.Key,
                        Expected = expectation.Expected,
                    };

                    // Wait for the expectation if a timeout is specified
                    bool success = false;
                    object? actualValue = null;

                    if (expectation.TimeoutMs.HasValue && expectation.TimeoutMs.Value > 0)
                    {
                        // Implement waiting logic with timeout
                        DateTime start = DateTime.UtcNow;
                        DateTime end = start.AddMilliseconds(expectation.TimeoutMs.Value);
                        int pollingInterval = expectation.PollingIntervalMs ?? 100;

                        while (DateTime.UtcNow < end)
                        {
                            (success, actualValue) = await CheckSingleExpectationAsync(
                                expectation,
                                key,
                                field
                            );
                            if (success)
                                break;

                            await Task.Delay(pollingInterval);
                        }
                    }
                    else
                    {
                        // Check once immediately
                        (success, actualValue) = await CheckSingleExpectationAsync(
                            expectation,
                            key,
                            field
                        );
                    }

                    result.Success = success;
                    result.Actual = actualValue;

                    if (!success)
                    {
                        result.Details = $"Expected: {expectation.Expected}, Actual: {actualValue}";
                    }

                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                if (!ShouldThrottleError("CheckExpectations"))
                {
                    _logger.Error(ex, "Failed to check expectations from Redis");
                }
                throw;
            }

            return results;
        }

        /// <summary>
        /// Checks a single expectation
        /// </summary>
        private async Task<(bool Success, object? ActualValue)> CheckSingleExpectationAsync(
            TestExpectation expectation,
            string key,
            string? field
        )
        {
            object? actualValue = null;

            try
            {
                // Get the actual value based on format
                switch (expectation.Format)
                {
                    case RedisDataFormat.String:
                        var stringValue = await _db.StringGetAsync(key);
                        if (stringValue.HasValue)
                        {
                            actualValue = stringValue.ToString();
                            // Try to parse as a number if that's what's expected
                            if (expectation.Expected is double || expectation.Expected is int)
                            {
                                if (double.TryParse(stringValue.ToString(), out var doubleVal))
                                {
                                    actualValue = doubleVal;
                                }
                            }
                            else if (expectation.Expected is bool)
                            {
                                if (bool.TryParse(stringValue.ToString(), out var boolVal))
                                {
                                    actualValue = boolVal;
                                }
                            }
                        }
                        break;

                    case RedisDataFormat.Hash:
                        if (string.IsNullOrEmpty(field))
                        {
                            _logger.Warning(
                                "Hash field is required for hash format expectation {Key}",
                                key
                            );
                            return (false, null);
                        }

                        var hashValue = await _db.HashGetAsync(key, field);
                        if (hashValue.HasValue)
                        {
                            actualValue = hashValue.ToString();
                            // Try to parse as a number if that's what's expected
                            if (expectation.Expected is double || expectation.Expected is int)
                            {
                                if (double.TryParse(hashValue.ToString(), out var doubleVal))
                                {
                                    actualValue = doubleVal;
                                }
                            }
                            else if (expectation.Expected is bool)
                            {
                                if (bool.TryParse(hashValue.ToString(), out var boolVal))
                                {
                                    actualValue = boolVal;
                                }
                            }
                        }
                        break;

                    case RedisDataFormat.Json:
                        var jsonValue = await _db.StringGetAsync(key);
                        if (jsonValue.HasValue)
                        {
                            try
                            {
                                // For JSON, we attempt to deserialize
                                actualValue = JsonSerializer.Deserialize<object>(
                                    jsonValue.ToString(),
                                    _jsonOptions
                                );
                            }
                            catch
                            {
                                // If it's not valid JSON, just use the string
                                actualValue = jsonValue.ToString();
                            }
                        }
                        break;
                }

                // Compare based on the validator type
                bool success = false;

                string validatorType = expectation.Validator.ToLowerInvariant();

                if (validatorType == "auto")
                {
                    // Determine validator type from expected value type
                    if (expectation.Expected is bool)
                    {
                        validatorType = "boolean";
                    }
                    else if (
                        expectation.Expected is double
                        || expectation.Expected is int
                        || expectation.Expected is float
                    )
                    {
                        validatorType = "numeric";
                    }
                    else
                    {
                        validatorType = "string";
                    }
                }

                // Perform comparison based on validator type
                switch (validatorType)
                {
                    case "boolean":
                        success = CompareBooleans(expectation.Expected, actualValue);
                        break;

                    case "numeric":
                        double tolerance = expectation.Tolerance ?? 0.0001;
                        success = CompareNumbers(expectation.Expected, actualValue, tolerance);
                        break;

                    case "string":
                    default:
                        success = CompareStrings(expectation.Expected, actualValue);
                        break;
                }

                return (success, actualValue);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking expectation {Key}", expectation.Key);
                return (false, null);
            }
        }

        /// <summary>
        /// Determine the key format and field based on conventions
        /// </summary>
        private (string Key, string? Field, RedisDataFormat Format) DetermineKeyFormat(
            string originalKey,
            string? originalField
        )
        {
            string key = originalKey;
            string? field = originalField;
            RedisDataFormat format = RedisDataFormat.String;

            // Check if key matches input:xxx format
            if (key.StartsWith("input:"))
            {
                key = $"{INPUT_PREFIX}{key.Substring(6)}";
                format = RedisDataFormat.String;
            }
            // Check if key matches output:xxx format
            else if (key.StartsWith("output:"))
            {
                key = $"{OUTPUT_PREFIX}{key.Substring(7)}";
                format = RedisDataFormat.String;
            }
            // Check for hash format with colon separator
            else if (key.Contains(':') && field == null)
            {
                var parts = key.Split(':');
                if (parts.Length == 2)
                {
                    key = parts[0];
                    field = parts[1];
                    format = RedisDataFormat.Hash;
                }
            }

            return (key, field, format);
        }

        /// <summary>
        /// Compare boolean values
        /// </summary>
        private bool CompareBooleans(object? expected, object? actual)
        {
            if (expected == null || actual == null)
                return expected == actual;

            bool expectedBool;
            bool actualBool;

            // Convert expected to bool
            if (expected is bool eb)
            {
                expectedBool = eb;
            }
            else if (expected is string es && bool.TryParse(es, out var esb))
            {
                expectedBool = esb;
            }
            else
            {
                return false;
            }

            // Convert actual to bool
            if (actual is bool ab)
            {
                actualBool = ab;
            }
            else if (actual is string asValue && bool.TryParse(asValue, out var asb))
            {
                actualBool = asb;
            }
            else
            {
                return false;
            }

            return expectedBool == actualBool;
        }

        /// <summary>
        /// Compare numeric values with tolerance
        /// </summary>
        private bool CompareNumbers(object? expected, object? actual, double tolerance)
        {
            if (expected == null || actual == null)
                return expected == actual;

            double expectedNumber;
            double actualNumber;

            // Convert expected to double
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
            else if (expected is string es && double.TryParse(es, out var esd))
            {
                expectedNumber = esd;
            }
            else
            {
                return false;
            }

            // Convert actual to double
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
            else if (actual is string asValue && double.TryParse(asValue, out var asd))
            {
                actualNumber = asd;
            }
            else
            {
                return false;
            }

            return Math.Abs(expectedNumber - actualNumber) <= tolerance;
        }

        /// <summary>
        /// Compare string values
        /// </summary>
        private bool CompareStrings(object? expected, object? actual)
        {
            if (expected == null || actual == null)
                return expected == actual;

            string expectedString = expected.ToString() ?? string.Empty;
            string actualString = actual.ToString() ?? string.Empty;

            return expectedString == actualString;
        }

        /// <summary>
        /// Clears all Redis keys matching a pattern
        /// </summary>
        public async Task ClearKeysAsync(string pattern)
        {
            try
            {
                var endpoints = _redis.GetEndPoints();
                var server = _redis.GetServer(endpoints.First());
                var keys = server.Keys(pattern: pattern);

                foreach (var key in keys)
                {
                    await _db.KeyDeleteAsync(key);
                }

                _logger.Information("Cleared keys matching pattern: {Pattern}", pattern);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to clear Redis keys with pattern {Pattern}", pattern);
                throw;
            }
        }

        /// <summary>
        /// Sets pre-test output values in Redis
        /// </summary>
        public async Task SetPreTestOutputsAsync(Dictionary<string, object> outputs)
        {
            if (outputs == null || outputs.Count == 0)
                return;

            try
            {
                foreach (var (key, value) in outputs)
                {
                    string redisKey = key;

                    // Apply format conventions if needed
                    if (key.StartsWith("output:"))
                    {
                        redisKey = $"{OUTPUT_PREFIX}{key.Substring(7)}";
                    }

                    await _db.StringSetAsync(redisKey, value.ToString());
                    _logger.Debug("Pre-set output {Key} = {Value}", redisKey, value);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to set pre-test outputs in Redis");
                throw;
            }
        }

        private bool ShouldThrottleError(string errorKey)
        {
            var now = DateTime.UtcNow;
            if (_lastErrorTime.TryGetValue(errorKey, out var lastTime))
            {
                if (now - lastTime < _errorThrottleWindow)
                {
                    return true;
                }
            }

            _lastErrorTime[errorKey] = now;
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _redis.Dispose();
            _disposed = true;
        }
    }
}
