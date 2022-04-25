using System;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Interface isolates all the cTrader objects from the main code
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
        /// Loads the bars until <see cref="Limit"/> was reached.
        /// </summary>
        void LoadBars();

        /// <summary>
        /// Gets the limit amount for bars loaded.
        /// </summary>
        int Limit { get; }

        /// <summary>
        /// Gets the start bar index according by limit.
        /// </summary>
        int StartIndexLimit { get; }

        /// <summary>
        /// Gets the time frame of the current instance.
        /// </summary>
        TimeFrame TimeFrame { get; }

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
