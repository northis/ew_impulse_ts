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
    internal static class TimeFrameHelper
    {
        private static readonly List<TimeFrameInfo> TIME_FRAMES_LIST;
        private static readonly List<TimeFrameInfo> TIME_FRAMES_LIST_SELECTED;

        static TimeFrameHelper()
        {
            int timeFrameTypeTimeEnum = 0;//TimeFrameType.Time
            Type timeFrameType = typeof(TimeFrame);
            TIME_FRAMES_LIST = timeFrameType.GetFields(
                    BindingFlags.Public | BindingFlags.Static)
                .Where(a => a.FieldType == timeFrameType)
                .Select(a => a.GetValue(null) as TimeFrame)
                .Where(a => Convert.ToInt32(timeFrameType
                    .GetProperty("TimeFrameType")
                    ?.GetValue(a)) == timeFrameTypeTimeEnum)
                .Select(a => new TimeFrameInfo(a,
                    TimeSpan.FromMinutes(Convert.ToInt32(timeFrameType
                        .GetProperty("Size")
                        ?.GetValue(a)))))
                .OrderBy(a => a.TimeSpan)
                .ToList();

            TimeSpan[] selected =
            {
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(4),
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(7)
            };

            TIME_FRAMES_LIST_SELECTED = TIME_FRAMES_LIST
                .Where(a => selected.Contains(a.TimeSpan))
                .ToList();

            TimeFrames = TIME_FRAMES_LIST.ToDictionary(a => a.TimeFrame, a => a);
        }

        /// <summary>
        /// Gets the supported time frames.
        /// </summary>
        public static Dictionary<TimeFrame, TimeFrameInfo> TimeFrames { get; }
#if !GARTLEY_PROD
        /// <summary>
        /// Gets the next time frame (bigger).
        /// </summary>
        /// <param name="tf">The current time frame.</param>
        /// <param name="periodRatio">The period ratio - how bigger we want to get a TF. For periodRatio=2 and H1 tf the result will be H2</param>
        public static TimeFrameInfo GetNextTimeFrame(TimeFrame tf, double periodRatio)
        {
            TimeFrameInfo val = GetTimeFrameInfo(tf);

            if (periodRatio <= 0)
                periodRatio = 1;

            TimeSpan nexTimeSpan = val.TimeSpan * periodRatio;
            TimeFrameInfo nextVal = TimeFrames
                .SkipWhile(a => a.Value.TimeSpan < nexTimeSpan)
                .Select(a => a.Value)
                .FirstOrDefault();

            if (nextVal == null)
                return val;

            return nextVal;
        }
#endif
        /// <summary>
        /// Gets the time frame information.
        /// </summary>
        /// <param name="tf">The TimeFrame.</param>
        /// <exception cref="NotSupportedException">The TF {tf.Name} is not supported!</exception>
        internal static TimeFrameInfo GetTimeFrameInfo(TimeFrame tf)
        {
            if (!TimeFrames.TryGetValue(tf, out TimeFrameInfo val))
                throw new NotSupportedException($"The TF {tf.Name} is not supported!");

            return val;
        }

        /// <summary>
        /// Gets the next time frame (bigger).
        /// </summary>
        /// <param name="tf">The current time frame.</param>
        public static TimeFrameInfo GetNextTimeFrameInfo(TimeFrame tf)
        {
            TimeFrameInfo val = GetTimeFrameInfo(tf);
            int index = TIME_FRAMES_LIST_SELECTED.IndexOf(val);
            return index > 0 && index < TIME_FRAMES_LIST.Count - 1 
                ? TIME_FRAMES_LIST[index + 1] 
                : val;
        }

        /// <summary>
        /// Gets the previous time frame (smaller).
        /// </summary>
        /// <param name="tf">The current time frame.</param>
        public static TimeFrameInfo GetPreviousTimeFrameInfo(TimeFrame tf)
        {
            TimeFrameInfo val = GetTimeFrameInfo(tf);
            int index = TIME_FRAMES_LIST_SELECTED.IndexOf(val);
            return index > 0 ? TIME_FRAMES_LIST[index - 1] : val;
        }
    }
}
