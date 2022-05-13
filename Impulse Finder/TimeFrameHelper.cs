using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Handles all the Time Frame-related logic
    /// </summary>
    public static class TimeFrameHelper
    {
        static TimeFrameHelper()
        {
            int timeFrameTypeTimeEnum = 0;//TimeFrameType.Time
            Type timeFrameType = typeof(TimeFrame);
            TIME_FRAMES_ARRAY = timeFrameType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(a => a.FieldType == timeFrameType)
                .Select(a => a.GetValue(null) as TimeFrame)
                .Where(a => Convert.ToInt32(timeFrameType
                    .GetProperty("TimeFrameType", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(a)) == timeFrameTypeTimeEnum)
                .Select(a => new TimeFrameInfo(a, TimeSpan.FromMinutes(Convert.ToInt32(timeFrameType
                    .GetProperty("Size", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(a)))))
                .ToArray();

            TimeFrames = TIME_FRAMES_ARRAY.ToDictionary(
                a => a.TimeFrame, a => a);
        }

        /// <summary>
        /// Gets the supported time frames.
        /// </summary>
        public static Dictionary<TimeFrame, TimeFrameInfo> TimeFrames { get; }

        private static readonly TimeFrameInfo[] TIME_FRAMES_ARRAY;

        /// <summary>
        /// Gets the next major time frames for the current time frame.
        /// </summary>
        /// <param name="current">The current.</param>
        /// <param name="nextRatio">Get next TF at least this times bigger.</param>
        public static TimeFrame GetNextTimeFrame(TimeFrame current, double nextRatio)
        {
            if (!TimeFrames.TryGetValue(current, out TimeFrameInfo info))
            {
                return current;
            }

            TimeSpan nextTs = TimeSpan.FromMinutes(info.TimeSpan.Minutes* nextRatio);
            TimeFrameInfo res = TIME_FRAMES_ARRAY
                .SkipWhile(a => a.TimeSpan < nextTs)
                .FirstOrDefault();

            return res == null ? TIME_FRAMES_ARRAY[^1].TimeFrame : res.TimeFrame;
        }
    }
}
