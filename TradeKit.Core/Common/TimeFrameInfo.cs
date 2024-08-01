namespace TradeKit.Core.Common
{
    /// <summary>
    /// Class with handy info for <see cref="TimeFrame"/>
    /// </summary>
    public class TimeFrameInfo
    {
        /// <summary>
        /// Gets the time frame (initial).
        /// </summary>
        public ITimeFrame TimeFrame { get; }

        /// <summary>
        /// Gets the minor time span (previous).
        /// </summary>
        public ITimeFrame PrevTimeFrame { get; }

        /// <summary>
        /// Gets the major time span (next).
        /// </summary>
        public ITimeFrame NextTimeFrame { get; }

        /// <summary>
        /// Gets the interval for the time span.
        /// </summary>
        public TimeSpan TimeSpan { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TimeFrameInfo"/> class.
        /// </summary>
        /// <param name="timeFrame">The time frame.</param>
        /// <param name="timeSpan">The time span.</param>
        /// <param name="prevTimeFrame">The minor time span (previous).</param>
        /// <param name="nextTimeFrame">The major time span (next).</param>
        public TimeFrameInfo(
            ITimeFrame timeFrame, TimeSpan timeSpan, ITimeFrame prevTimeFrame, ITimeFrame nextTimeFrame)
        {
            TimeFrame = timeFrame;
            TimeSpan = timeSpan;
            PrevTimeFrame = prevTimeFrame;
            NextTimeFrame = nextTimeFrame;
        }
    }
}
