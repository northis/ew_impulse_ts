using System;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Interface isolated all the cTrader objects from the main code
    /// </summary>
    public interface IBarsProvider
    {
        /// <summary>
        /// Gets the low price of the candle by the <see cref="index"/> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        double GetLowPrice(int index);

        /// <summary>
        /// Gets the high price of the candle by the <see cref="index"/> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        double GetHighPrice(int index);

        /// <summary>
        /// Gets the open time of the candle by the <see cref="index"/> specified
        /// </summary>
        /// <param name="index">The index.</param>
        DateTime GetOpenTime(int index);

        /// <summary>
        /// Gets the total count of bars collected.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Gets the time frame of the current instance.
        /// </summary>
        TimeFrame TimeFrame { get; }

        /// <summary>
        /// Gets the bars of the specified time frame.
        /// </summary>
        /// <param name="timeFrame">The time frame.</param>
        IBarsProvider GetBars(TimeFrame timeFrame);

        /// <summary>
        /// Gets the int index of bar (candle) by datetime.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        int GetIndexByTime(DateTime dateTime);

        /// <summary>
        /// Gets the open time for the last bar available.
        /// </summary>
        DateTime GetLastBarOpenTime();
    }
}
