namespace TradeKit.Core.Common
{
    /// <summary>
    /// Interface isolates all the bar obtaining logic from the main code
    /// </summary>
    public interface IBarsProvider : IDisposable
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
        /// Gets the median price ((H+L)/2) of the candle by the <see cref="index"/> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        double GetMedianPrice(int index);

        /// <summary>
        /// Gets the open price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        double GetOpenPrice(int index);

        /// <summary>
        /// Gets the close price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        double GetClosePrice(int index);

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
        /// Loads the bars until <see cref="date"/> is reached.
        /// </summary>
        void LoadBars(DateTime date);

        /// <summary>
        /// Gets the start bar index according to limit.
        /// </summary>
        int StartIndexLimit { get; }

        /// <summary>
        /// Gets the time frame of the current instance.
        /// </summary>
        ITimeFrame TimeFrame { get; }

        /// <summary>
        /// Gets the current symbol.
        /// </summary>
        ISymbol BarSymbol { get; }

        /// <summary>
        /// Gets the int index of bar (candle) by datetime.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        int GetIndexByTime(DateTime dateTime);

        /// <summary>
        /// Called when current bar is closed and ready to analyze.
        /// </summary>
        event EventHandler BarClosed;
    }
}
