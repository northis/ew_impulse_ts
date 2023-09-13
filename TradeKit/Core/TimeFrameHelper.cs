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
        static TimeFrameHelper()
        {
            var timeFramesList = new List<TimeFrameInfo>
            {
                new(TimeFrame.Minute, TimeSpan.FromMinutes(1), TimeFrame.Minute, TimeFrame.Minute5),
                new(TimeFrame.Minute5, TimeSpan.FromMinutes(5), TimeFrame.Minute, TimeFrame.Minute10),
                new(TimeFrame.Minute10, TimeSpan.FromMinutes(10), TimeFrame.Minute, TimeFrame.Minute15),
                new(TimeFrame.Minute15, TimeSpan.FromMinutes(15), TimeFrame.Minute, TimeFrame.Minute30),
                new(TimeFrame.Minute20, TimeSpan.FromMinutes(20), TimeFrame.Minute, TimeFrame.Minute30),
                new(TimeFrame.Minute30, TimeSpan.FromMinutes(30), TimeFrame.Minute5, TimeFrame.Minute45),
                new(TimeFrame.Minute45, TimeSpan.FromMinutes(45), TimeFrame.Minute5, TimeFrame.Hour),
                new(TimeFrame.Hour, TimeSpan.FromHours(1), TimeFrame.Minute15, TimeFrame.Hour2),
                new(TimeFrame.Hour2, TimeSpan.FromHours(2), TimeFrame.Minute15, TimeFrame.Hour4),
                new(TimeFrame.Hour4, TimeSpan.FromHours(4), TimeFrame.Minute15, TimeFrame.Daily),
                new(TimeFrame.Daily, TimeSpan.FromDays(1), TimeFrame.Hour, TimeFrame.Weekly),
                new(TimeFrame.Weekly, TimeSpan.FromDays(7), TimeFrame.Hour4, TimeFrame.Monthly),
                new(TimeFrame.Monthly, TimeSpan.FromDays(30), TimeFrame.Hour4, TimeFrame.Monthly)
            };

            TimeFrames = timeFramesList.ToDictionary(a => a.TimeFrame, a => a);
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
            return GetTimeFrameInfo(val.NextTimeFrame);
        }

        /// <summary>
        /// Gets the previous time frame (smaller).
        /// </summary>
        /// <param name="tf">The current time frame.</param>
        public static TimeFrameInfo GetPreviousTimeFrameInfo(TimeFrame tf)
        {
            TimeFrameInfo val = GetTimeFrameInfo(tf);
            return GetTimeFrameInfo(val.PrevTimeFrame);
        }
    }
}
