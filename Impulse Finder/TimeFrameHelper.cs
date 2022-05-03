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
            // TODO Support all the TFs
            TIME_FRAMES_ARRAY = new TimeFrameInfo[]
            {
                new(TimeFrame.Tick15, TimeSpan.FromSeconds(15), 0),
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
        /// Gets the minor time frames for the current time frame.
        /// </summary>
        /// <param name="current">The current.</param>
        /// <param name="depth">The depth.</param>
        /// <returns>The ordered list of minor TFs</returns>
        public static List<TimeFrame> GetMinorTimeFrames(
            TimeFrame current, int depth)
        {
            var res = new List<TimeFrame>();
            if (depth <= 0)
            {
                return res;
            }
            
            if (!TimeFrames.TryGetValue(current, out TimeFrameInfo info))
            {
                //System.Diagnostics.Debugger.Launch();
                KeyValuePair<TimeFrame, TimeFrameInfo>[] possibleSub = 
                    TimeFrames.Where(a => current > a.Key).ToArray();
                if (possibleSub.Length > 0)
                {
                    info = possibleSub[^1].Value;
                }
                else
                {

                    return res;
                }
            }

            int limit = Math.Max(info.Index - depth, 0);
            int minorStartIndex = info.Index - 1;
            for (int i = minorStartIndex; i >= limit; i--)
            {
                res.Add(TIME_FRAMES_ARRAY[i].TimeFrame);
            }

            return res;
        }
    }
}
