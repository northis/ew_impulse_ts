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
                .ToArray();

            TimeFrames = timeFramesArray.ToDictionary(
                a => a.TimeFrame, a => a);

            FavoriteTimeFrames = timeFramesArray.Where(a =>
                    a.TimeFrame == TimeFrame.Minute || 
                    a.TimeFrame == TimeFrame.Minute5 ||
                    a.TimeFrame == TimeFrame.Minute15)
                    .ToDictionary(a => a.TimeFrame, a => a);
        }

        /// <summary>
        /// Gets the supported time frames.
        /// </summary>
        public static Dictionary<TimeFrame, TimeFrameInfo> TimeFrames { get; }

        /// <summary>
        /// Gets the favorite time frames.
        /// </summary>
        public static Dictionary<TimeFrame, TimeFrameInfo> FavoriteTimeFrames { get; }
    }
}
