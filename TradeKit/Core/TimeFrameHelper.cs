using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cAlgo.API;

namespace TradeKit.Core
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
            TimeFrameInfo[] timeFramesArray = timeFrameType.GetFields(
                    BindingFlags.Public | BindingFlags.Static)
                .Where(a => a.FieldType == timeFrameType)
                .Select(a => a.GetValue(null) as TimeFrame)
                .Where(a => Convert.ToInt32(timeFrameType
                    .GetProperty("TimeFrameType",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(a)) == timeFrameTypeTimeEnum)
                .Select(a => new TimeFrameInfo(a,
                    TimeSpan.FromMinutes(Convert.ToInt32(timeFrameType
                        .GetProperty("Size", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(a)))))
                .OrderBy(a => a.TimeSpan)
                .ToArray();

            TimeFrames = timeFramesArray.ToDictionary(a => a.TimeFrame, a => a);
        }

        /// <summary>
        /// Gets the supported time frames.
        /// </summary>
        public static Dictionary<TimeFrame, TimeFrameInfo> TimeFrames { get; }

        /// <summary>
        /// Gets the next time frame (bigger).
        /// </summary>
        /// <param name="tf">The current time frame.</param>
        /// <param name="periodRatio">The period ratio - how bigger we want to get a TF. For periodRatio=2 and H1 tf the result will be H2</param>
        /// <exception cref="NotSupportedException">$"The TF {tf.Name} is not supported!</exception>
        public static TimeFrameInfo GetNextTimeFrame(TimeFrame tf, double periodRatio)
        {
            if (!TimeFrames.TryGetValue(tf, out TimeFrameInfo val))
                throw new NotSupportedException($"The TF {tf.Name} is not supported!");

            TimeSpan nexTimeSpan = val.TimeSpan * periodRatio;
            TimeFrameInfo nextVal = TimeFrames
                .SkipWhile(a => a.Value.TimeSpan < nexTimeSpan)
                .Select(a => a.Value)
                .FirstOrDefault();

            if (nextVal == null)
                return val;

            return nextVal;
        }
    }
}
