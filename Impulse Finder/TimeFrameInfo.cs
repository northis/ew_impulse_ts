using System;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Class with handy info for <see cref="TimeFrame"/>
    /// </summary>
    public class TimeFrameInfo
    {
        /// <summary>
        /// Gets the time frame (initial).
        /// </summary>
        public TimeFrame TimeFrame { get; }

        /// <summary>
        /// Gets the interval for the time span.
        /// </summary>
        public TimeSpan TimeSpan { get; }

        /// <summary>
        /// Gets the index (number) for the time span.
        /// </summary>
        public int Index { get; }


        /// <summary>
        /// Initializes a new instance of the <see cref="TimeFrameInfo"/> class.
        /// </summary>
        /// <param name="timeFrame">The time frame.</param>
        /// <param name="timeSpan">The time span.</param>
        /// <param name="index">The index.</param>
        public TimeFrameInfo(TimeFrame timeFrame, TimeSpan timeSpan, int index)
        {
            TimeFrame = timeFrame;
            TimeSpan = timeSpan;
            Index = index;
        }
    }
}
