using System.Collections.Generic;
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
            TIME_FRAMES = new Dictionary<TimeFrame, int>();
            TIME_FRAMES_ARRAY = new []
            {
                TimeFrame.Tick,
                TimeFrame.Tick10,
                TimeFrame.Minute,
                TimeFrame.Minute5,
                TimeFrame.Minute15,
                TimeFrame.Minute30,
                TimeFrame.Hour,
                TimeFrame.Hour2,
                TimeFrame.Hour4,
                TimeFrame.Daily,
                TimeFrame.Weekly
            };

            for (int i = 0; i < TIME_FRAMES_ARRAY.Length; i++)
            {
                TIME_FRAMES.Add(TIME_FRAMES_ARRAY[i], i);
            }
        }

        private static readonly TimeFrame[] TIME_FRAMES_ARRAY;
        private static readonly Dictionary<TimeFrame, int> TIME_FRAMES;

        /// <summary>
        /// Gets the next minor time frame or the current time frame if no minor TF can be found.
        /// </summary>
        /// <param name="current">The current time frame.</param>
        public static TimeFrame GetMinorTimeFrame(TimeFrame current)
        {
            int currentIndex;
            if (TIME_FRAMES.TryGetValue(current, out currentIndex))
            {
                int minorIndex = currentIndex - 1;
                if (minorIndex < 0)
                {
                    return current;
                }

                TimeFrame result = TIME_FRAMES_ARRAY[minorIndex];
                return result;
            }

            return current;
        }
    }
}
