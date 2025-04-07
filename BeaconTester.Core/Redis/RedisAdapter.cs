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

        // Redis key prefixes - using consistent domain prefixes across the system
        public const string INPUT_PREFIX = "input:";
        public const string OUTPUT_PREFIX = "output:";
        public const string STATE_PREFIX = "state:";
        public const string BUFFER_PREFIX = "buffer:";

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
                            // Special handling for boolean values to ensure consistent representation
                            string value = input.Value is bool boolValue
                                ? boolValue.ToString() // "True" or "False" with capital first letter
                                : input.Value.ToString();
                                
                            await _db.StringSetAsync(key, value);
                            _logger.Debug("Set string {Key} = {Value}", key, value);
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
                        // Implement waiting logic with timeout and exponential backoff
                        DateTime start = DateTime.UtcNow;
                        DateTime end = start.AddMilliseconds(expectation.TimeoutMs.Value);
                        int basePollingInterval = expectation.PollingIntervalMs ?? 100;
                        int currentPollingInterval = basePollingInterval;
                        int maxPollingInterval = Math.Min(expectation.TimeoutMs.Value / 2, 2000); // Max 2 seconds or half timeout
                        int attemptCount = 0;
                        
                        _logger.Debug("Starting polling for {Key} with timeout {Timeout}ms and base interval {BaseInterval}ms", 
                            expectation.Key, expectation.TimeoutMs.Value, basePollingInterval);

                        while (DateTime.UtcNow < end)
                        {
                            attemptCount++;
                            (success, actualValue) = await CheckSingleExpectationAsync(
                                expectation,
                                key,
                                field
                            );
                            
                            if (success)
                            {
                                _logger.Debug("Expectation for {Key} met after {AttemptCount} attempts in {ElapsedTime}ms", 
                                    expectation.Key, attemptCount, (DateTime.UtcNow - start).TotalMilliseconds);
                                break;
                            }

                            // Calculate next polling interval with exponential backoff (up to maxPollingInterval)
                            // Start with base interval, then increase by 50% each time
                            if (attemptCount > 1) 
                            {
                                currentPollingInterval = Math.Min(
                                    (int)(currentPollingInterval * 1.5), 
                                    maxPollingInterval
                                );
                            }
                            
                            var remainingTime = (end - DateTime.UtcNow).TotalMilliseconds;
                            if (remainingTime <= 0)
                                break;
                                
                            // Don't wait longer than remaining time
                            int waitTime = (int)Math.Min(currentPollingInterval, remainingTime);
                            
                            _logger.Debug("Attempt {AttemptCount} for {Key} failed, waiting {WaitTime}ms before retry", 
                                attemptCount, expectation.Key, waitTime);
                                
                            await Task.Delay(waitTime);
                        }
                        
                        if (!success)
                        {
                            _logger.Debug("Expectation for {Key} not met after {AttemptCount} attempts and {Timeout}ms", 
                                expectation.Key, attemptCount, expectation.TimeoutMs.Value);
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
                            if (expectation.Expected is double || expectation.Expected is int || expectation.Expected is long || expectation.Expected is float)
                            {
                                string stringVal = stringValue.ToString() ?? string.Empty;
                                // Handle both period and comma as decimal separators
                                string normalizedString = stringVal.Trim().Replace(",", ".");
                                if (double.TryParse(normalizedString, out var doubleVal))
                                {
                                    actualValue = doubleVal;
                                }
                            }
                            // Always attempt to convert string values that look like booleans to actual booleans
                            // This helps handle inconsistencies between test expectations and actual results
                            string stringBoolValue = stringValue.ToString() ?? string.Empty;
                            string normalizedBoolValue = stringBoolValue.Trim().ToLowerInvariant();
                            
                            if (normalizedBoolValue == "true" || normalizedBoolValue == "1" || normalizedBoolValue == "yes")
                            {
                                _logger.Debug("Converting string '{RawValue}' to boolean: true", stringBoolValue);
                                actualValue = true;
                            }
                            else if (normalizedBoolValue == "false" || normalizedBoolValue == "0" || normalizedBoolValue == "no")
                            {
                                _logger.Debug("Converting string '{RawValue}' to boolean: false", stringBoolValue);
                                actualValue = false;
                            }
                            else if (bool.TryParse(stringBoolValue, out var boolVal))
                            {
                                _logger.Debug("Parsed '{RawValue}' as boolean: {BoolVal}", stringBoolValue, boolVal);
                                actualValue = boolVal;
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
                            if (expectation.Expected is double || expectation.Expected is int || expectation.Expected is long || expectation.Expected is float)
                            {
                                string stringVal = hashValue.ToString() ?? string.Empty;
                                // Handle both period and comma as decimal separators
                                string normalizedString = stringVal.Trim().Replace(",", ".");
                                if (double.TryParse(normalizedString, out var doubleVal))
                                {
                                    actualValue = doubleVal;
                                }
                            }
                            else if (expectation.Expected is bool)
                            {
                                string stringVal = hashValue.ToString() ?? string.Empty;
                                string normalizedValue = stringVal.Trim().ToLowerInvariant();
                                
                                if (normalizedValue == "true" || normalizedValue == "1" || normalizedValue == "yes")
                                {
                                    actualValue = true;
                                }
                                else if (normalizedValue == "false" || normalizedValue == "0" || normalizedValue == "no")
                                {
                                    actualValue = false;
                                }
                                else if (bool.TryParse(stringVal, out var boolVal))
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

                // Log detailed type information for debugging
                _logger.Debug("Comparing value - Key: {Key}, Expected Type: {ExpectedType}, Value: {ExpectedValue}, Actual Type: {ActualType}, Value: {ActualValue}, Validator: {Validator}",
                    expectation.Key,
                    expectation.Expected?.GetType().Name ?? "null",
                    expectation.Expected,
                    actualValue?.GetType().Name ?? "null",
                    actualValue,
                    validatorType);
                    
                // Handle expected value as JsonElement 
                if (expectation.Expected?.GetType().Name == "JsonElement")
                {
                    var jsonElement = (System.Text.Json.JsonElement)expectation.Expected;
                    object? convertedValue = null;
                    
                    switch (jsonElement.ValueKind)
                    {
                        case System.Text.Json.JsonValueKind.True:
                            convertedValue = true;
                            break;
                            
                        case System.Text.Json.JsonValueKind.False:
                            convertedValue = false;
                            break;
                            
                        case System.Text.Json.JsonValueKind.Number:
                            // Try to maintain the most appropriate numeric type
                            if (jsonElement.TryGetInt32(out int intValue))
                            {
                                convertedValue = intValue;
                            }
                            else if (jsonElement.TryGetInt64(out long longValue))
                            {
                                convertedValue = longValue; 
                            }
                            else if (jsonElement.TryGetDouble(out double doubleValue))
                            {
                                convertedValue = doubleValue;
                            }
                            else
                            {
                                // Fall back to string if parsing fails
                                convertedValue = jsonElement.ToString();
                            }
                            break;
                            
                        case System.Text.Json.JsonValueKind.String:
                            string strValue = jsonElement.GetString() ?? "";
                            
                            // Apply additional type conversions based on validator type
                            if (validatorType == "boolean")
                            {
                                if (bool.TryParse(strValue, out var boolVal))
                                {
                                    convertedValue = boolVal;
                                }
                                else if (strValue.Trim().ToLowerInvariant() == "true" || 
                                         strValue == "1" || strValue == "yes")
                                {
                                    convertedValue = true;
                                }
                                else if (strValue.Trim().ToLowerInvariant() == "false" || 
                                         strValue == "0" || strValue == "no")
                                {
                                    convertedValue = false;
                                }
                                else
                                {
                                    convertedValue = strValue;
                                }
                            }
                            else if (validatorType == "numeric" && double.TryParse(strValue, out var numVal))
                            {
                                convertedValue = numVal;
                            }
                            else
                            {
                                convertedValue = strValue;
                            }
                            break;
                            
                        case System.Text.Json.JsonValueKind.Object:
                        case System.Text.Json.JsonValueKind.Array:
                            // For complex objects, keep as JsonElement for now
                            // They will be compared as strings via ToString()
                            convertedValue = jsonElement;
                            break;
                            
                        case System.Text.Json.JsonValueKind.Null:
                            convertedValue = null;
                            break;
                            
                        default:
                            convertedValue = jsonElement.ToString();
                            break;
                    }
                    
                    expectation.Expected = convertedValue;
                    
                    _logger.Debug("Converted JsonElement ({Kind}) to {Type}: {Value}", 
                        jsonElement.ValueKind,
                        expectation.Expected?.GetType().Name ?? "null", 
                        expectation.Expected);
                }

                // Perform comparison based on validator type
                switch (validatorType)
                {
                    case "boolean":
                        success = CompareBooleans(expectation.Expected, actualValue);
                        _logger.Debug("Boolean comparison result: {Success}", success);
                        break;

                    case "numeric":
                        double tolerance = expectation.Tolerance ?? 0.0001;
                        success = CompareNumbers(expectation.Expected, actualValue, tolerance);
                        _logger.Debug("Numeric comparison result: {Success}", success);
                        break;

                    case "string":
                    default:
                        success = CompareStrings(expectation.Expected, actualValue);
                        _logger.Debug("String comparison result: {Success}", success);
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

            // Domain prefixes (input:, output:, state:, buffer:) are now preserved as-is
            // No transformation is done to ensure consistent naming across the system
            
            // Check for hash format with colon separator (if not a domain prefix)
            if (key.Contains(':') && field == null && 
                !key.StartsWith(INPUT_PREFIX) && 
                !key.StartsWith(OUTPUT_PREFIX) && 
                !key.StartsWith(STATE_PREFIX) && 
                !key.StartsWith(BUFFER_PREFIX))
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
            else if (expected is string es)
            {
                // Handle case-insensitive "true"/"false" and more flexible boolean representations
                var normalizedValue = es.Trim().ToLowerInvariant();
                if (normalizedValue == "true" || normalizedValue == "1" || normalizedValue == "yes")
                {
                    expectedBool = true;
                }
                else if (normalizedValue == "false" || normalizedValue == "0" || normalizedValue == "no")
                {
                    expectedBool = false;
                }
                else if (bool.TryParse(es, out var esb))
                {
                    expectedBool = esb;
                }
                else
                {
                    return false;
                }
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
            else if (actual is string asValue)
            {
                // Handle case-insensitive "true"/"false" and more flexible boolean representations
                var normalizedValue = asValue.Trim().ToLowerInvariant();
                if (normalizedValue == "true" || normalizedValue == "1" || normalizedValue == "yes")
                {
                    actualBool = true;
                }
                else if (normalizedValue == "false" || normalizedValue == "0" || normalizedValue == "no")
                {
                    actualBool = false;
                }
                else if (bool.TryParse(asValue, out var asb))
                {
                    actualBool = asb;
                }
                else
                {
                    _logger.Debug("Failed to parse boolean value: {Value}", asValue);
                    return false;
                }
            }
            else
            {
                _logger.Debug("Actual value is not a string or boolean: {Type}", actual?.GetType().Name ?? "null");
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
            else if (expected is long el)
            {
                expectedNumber = el;
            }
            else if (expected is string es)
            {
                // Normalize strings, handling regional format issues
                string normalizedString = es.Trim().Replace(",", ".");
                if (double.TryParse(normalizedString, out var esd))
                {
                    expectedNumber = esd;
                }
                else
                {
                    _logger.Debug("Cannot parse expected string as number: {Value}", es);
                    return false;
                }
            }
            else
            {
                _logger.Debug("Unexpected type for expected value: {Type}", expected.GetType().Name);
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
            else if (actual is long al)
            {
                actualNumber = al;
            }
            else if (actual is string asValue)
            {
                // Normalize strings, handling regional format issues
                string normalizedString = asValue.Trim().Replace(",", ".");
                if (double.TryParse(normalizedString, out var asd))
                {
                    actualNumber = asd;
                }
                else
                {
                    _logger.Debug("Cannot parse actual string as number: {Value}", asValue);
                    return false;
                }
            }
            else
            {
                _logger.Debug("Unexpected type for actual value: {Type}", actual.GetType().Name);
                return false;
            }

            bool isEqual = Math.Abs(expectedNumber - actualNumber) <= tolerance;
            
            if (!isEqual)
            {
                _logger.Debug("Numeric comparison failed: expected {Expected}, actual {Actual}, tolerance {Tolerance}", 
                    expectedNumber, actualNumber, tolerance);
            }
            
            return isEqual;
        }

        /// <summary>
        /// Compare string values
        /// </summary>
        private bool CompareStrings(object? expected, object? actual)
        {
            // Special handling for null values
            if (expected == null && actual == null)
                return true;
                
            // Handle cases where only one is null - treat null as empty string for comparisons
            if (expected == null)
                expected = string.Empty;
                
            if (actual == null)
                actual = string.Empty;

            string expectedString = expected.ToString() ?? string.Empty;
            string actualString = actual.ToString() ?? string.Empty;

            // Handle empty strings and whitespace - do this check early
            bool expectedEmpty = string.IsNullOrWhiteSpace(expectedString);
            bool actualEmpty = string.IsNullOrWhiteSpace(actualString);
            
            if (expectedEmpty && actualEmpty)
                return true;

            // Trim strings for more forgiving comparison
            string trimmedExpected = expectedString.Trim();
            string trimmedActual = actualString.Trim();

            // If the expected value looks like a boolean, try to handle case variations
            if (trimmedExpected.ToLowerInvariant() == "true" || 
                trimmedExpected.ToLowerInvariant() == "false" ||
                trimmedExpected == "1" || trimmedExpected == "0")
            {
                // Try converting both to booleans and compare
                return CompareBooleans(expected, actual);
            }
            
            // For numeric strings, try to compare numerically with improved parsing
            string normalizedExpected = trimmedExpected.Replace(",", ".");
            string normalizedActual = trimmedActual.Replace(",", ".");
            
            if (double.TryParse(normalizedExpected, out var expectedNum) && 
                double.TryParse(normalizedActual, out var actualNum))
            {
                // If both are numbers, use a small tolerance
                const double tolerance = 0.0001;
                bool isEqual = Math.Abs(expectedNum - actualNum) <= tolerance;
                
                if (isEqual)
                    return true;
                    
                _logger.Debug("Numeric string comparison failed: {Expected} vs {Actual}", 
                    expectedString, actualString);
            }

            // Try case-insensitive comparison
            if (string.Equals(trimmedExpected, trimmedActual, StringComparison.OrdinalIgnoreCase))
                return true;

            // Default to exact string comparison
            bool exactMatch = expectedString == actualString;
            
            if (!exactMatch)
            {
                _logger.Debug("String comparison failed: expected '{Expected}', actual '{Actual}'", 
                    expectedString, actualString);
            }
            
            return exactMatch;
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
                var keys = server.Keys(pattern: pattern).ToList();
                
                if (keys.Count > 0)
                {
                    _logger.Debug("Found {KeyCount} keys matching pattern {Pattern}: {Keys}", 
                        keys.Count, pattern, string.Join(", ", keys));
                        
                    foreach (var key in keys)
                    {
                        string value = await _db.StringGetAsync(key);
                        _logger.Debug("Clearing key {Key} with value {Value}", key, value);
                        await _db.KeyDeleteAsync(key);
                    }
                    
                    _logger.Information("Cleared {KeyCount} keys matching pattern: {Pattern}", keys.Count, pattern);
                }
                else
                {
                    _logger.Debug("No keys found matching pattern: {Pattern}", pattern);
                }
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
                    
                    // No prefix transformation is done to ensure consistent naming across the system
                    // Domain prefixes (output:, state:, buffer:) are preserved as-is

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
