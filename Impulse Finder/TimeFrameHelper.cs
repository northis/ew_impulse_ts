using System;
using System.Collections.Generic;
using System.Linq;
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
            TIME_FRAMES_ARRAY = new TimeFrameInfo[]
            {
                new(TimeFrame.Tick15, TimeSpan.FromSeconds(20), 0),
                new(TimeFrame.Minute, TimeSpan.FromMinutes(1), 1),
                new(TimeFrame.Minute5, TimeSpan.FromMinutes(5), 2),
                new(TimeFrame.Minute15, TimeSpan.FromMinutes(15), 3),
                new(TimeFrame.Hour, TimeSpan.FromHours(1), 4),
                new(TimeFrame.Hour2, TimeSpan.FromHours(2), 5),
                new(TimeFrame.Hour4, TimeSpan.FromHours(4), 6),
                new(TimeFrame.Daily, TimeSpan.FromDays(1), 7),
                new(TimeFrame.Weekly, TimeSpan.FromDays(7), 8),
            };

            TimeFrames = TIME_FRAMES_ARRAY.ToDictionary(
                a => a.TimeFrame, a => a);
        }

        /// <summary>
        /// Gets the supported time frames.
        /// </summary>
        public static Dictionary<TimeFrame, TimeFrameInfo> TimeFrames { get; }

        private static readonly TimeFrameInfo[] TIME_FRAMES_ARRAY;

        /// <summary>
        /// Gets the next minor time frame or the current time frame if no minor TF can be found.
        /// </summary>
        /// <param name="current">The current time frame.</param>
        public static TimeFrame GetMinorTimeFrame(TimeFrame current)
        {
            if (TimeFrames.TryGetValue(
                    current, out TimeFrameInfo currentInfo))
            {
                int minorIndex = currentInfo.Index - 1;
                if (minorIndex < 0)
                {
                    return current;
                }

                TimeFrame result = TIME_FRAMES_ARRAY[minorIndex].TimeFrame;
                return result;
            }

            return current;
        }
    }
}
