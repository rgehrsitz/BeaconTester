using System;

namespace BeaconTester.Core.Models
{
    /// <summary>
    /// Configuration settings for test execution
    /// </summary>
    public class TestConfig
    {
        /// <summary>
        /// Beacon's cycle time in milliseconds. Defaults to 100ms, but should be set to match
        /// Beacon's actual cycle time (or test mode cycle time when Beacon is running in test mode).
        /// </summary>
        public int BeaconCycleTimeMs { get; set; } = 100;

        /// <summary>
        /// Default multiplier for step delay times. This determines how many Beacon cycles to wait
        /// after setting inputs before checking outputs. Defaults to 2 cycles.
        /// </summary>
        public int DefaultStepDelayMultiplier { get; set; } = 2;

        /// <summary>
        /// Default multiplier for expectation timeouts. This determines how many Beacon cycles to
        /// wait for an expectation to be met. Defaults to 3 cycles plus a small buffer.
        /// </summary>
        public int DefaultTimeoutMultiplier { get; set; } = 3;

        /// <summary>
        /// Additional buffer time in milliseconds added to timeout calculations. Defaults to 50ms.
        /// </summary>
        public int TimeoutBufferMs { get; set; } = 50;

        /// <summary>
        /// Default polling interval for checking outputs, as a fraction of the Beacon cycle time.
        /// Defaults to 1.1 (poll slightly after the cycle should complete).
        /// </summary>
        public double PollingIntervalFactor { get; set; } = 1.1;

        /// <summary>
        /// Global timeout multiplier that is applied to all timeouts. Useful for slowing down
        /// all tests during debugging. Defaults to 1.0 (no change).
        /// </summary>
        public double GlobalTimeoutMultiplier { get; set; } = 1.0;

        /// <summary>
        /// Calculates an appropriate wait time for a test step based on the Beacon cycle time
        /// </summary>
        /// <param name="overrideMultiplier">Optional multiplier override</param>
        /// <returns>Wait time in milliseconds</returns>
        public int CalculateStepWaitTimeMs(int? overrideMultiplier = null)
        {
            var multiplier = overrideMultiplier ?? DefaultStepDelayMultiplier;
            return (int)Math.Ceiling(BeaconCycleTimeMs * multiplier * GlobalTimeoutMultiplier);
        }

        /// <summary>
        /// Calculates an appropriate timeout for an expectation based on the Beacon cycle time
        /// </summary>
        /// <param name="overrideMultiplier">Optional multiplier override</param>
        /// <returns>Timeout in milliseconds</returns>
        public int CalculateTimeoutMs(int? overrideMultiplier = null)
        {
            var multiplier = overrideMultiplier ?? DefaultTimeoutMultiplier;
            return (int)Math.Ceiling(BeaconCycleTimeMs * multiplier * GlobalTimeoutMultiplier) + TimeoutBufferMs;
        }

        /// <summary>
        /// Calculates an appropriate polling interval based on the Beacon cycle time
        /// </summary>
        /// <param name="overrideFactor">Optional factor override</param>
        /// <returns>Polling interval in milliseconds</returns>
        public int CalculatePollingIntervalMs(double? overrideFactor = null)
        {
            var factor = overrideFactor ?? PollingIntervalFactor;
            return Math.Max(50, (int)Math.Ceiling(BeaconCycleTimeMs * factor));
        }
    }
}
