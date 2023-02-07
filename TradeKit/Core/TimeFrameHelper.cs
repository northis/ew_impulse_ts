using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace TradeKit.Core
{
    /// <summary>
    /// Handles all the Time Frame-related logic
    /// </summary>
    internal static class TimeFrameHelper
    {
        private static readonly List<TimeFrameInfo> TIME_FRAMES_LIST;

        static TimeFrameHelper()
        {
            TIME_FRAMES_LIST = new List<TimeFrameInfo>
            {
                new TimeFrameInfo(TimeFrame.Minute, TimeSpan.FromMinutes(1)),
                new TimeFrameInfo(TimeFrame.Minute5, TimeSpan.FromMinutes(5)),
                new TimeFrameInfo(TimeFrame.Minute10, TimeSpan.FromMinutes(10)),
                new TimeFrameInfo(TimeFrame.Minute15, TimeSpan.FromMinutes(15)),
                new TimeFrameInfo(TimeFrame.Minute20, TimeSpan.FromMinutes(20)),
                new TimeFrameInfo(TimeFrame.Minute30, TimeSpan.FromMinutes(30)),
                new TimeFrameInfo(TimeFrame.Minute45, TimeSpan.FromMinutes(45)),
                new TimeFrameInfo(TimeFrame.Hour, TimeSpan.FromHours(1)),
                new TimeFrameInfo(TimeFrame.Hour2, TimeSpan.FromHours(2)),
                new TimeFrameInfo(TimeFrame.Hour4, TimeSpan.FromHours(4)),
                new TimeFrameInfo(TimeFrame.Daily, TimeSpan.FromDays(1)),
                new TimeFrameInfo(TimeFrame.Weekly, TimeSpan.FromDays(7)),
                new TimeFrameInfo(TimeFrame.Monthly, TimeSpan.FromDays(30))
            };

            TimeFrames = TIME_FRAMES_LIST.ToDictionary(a => a.TimeFrame, a => a);
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
            int index = TIME_FRAMES_LIST.IndexOf(val);
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
            int index = TIME_FRAMES_LIST.IndexOf(val);
            return index > 0 ? TIME_FRAMES_LIST[index - 1] : val;
        }
    }
}
