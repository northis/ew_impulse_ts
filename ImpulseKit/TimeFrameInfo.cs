using System;
using cAlgo.API;

namespace TradeKit
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
        /// Initializes a new instance of the <see cref="TimeFrameInfo"/> class.
        /// </summary>
        /// <param name="timeFrame">The time frame.</param>
        /// <param name="timeSpan">The time span.</param>
        public TimeFrameInfo(TimeFrame timeFrame, TimeSpan timeSpan)
        {
            TimeFrame = timeFrame;
            TimeSpan = timeSpan;
        }
    }
}
