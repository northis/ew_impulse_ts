namespace TradeKit.Core.Common
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
        /// Gets the [low price-bar key] pair from <see cref="startIndex"/> to <see cref="endIndex"/>.
        /// </summary>
        /// <param name="startIndex">The start index.</param>
        /// <param name="endIndex">The end index.</param>
        KeyValuePair<int, double> GetLowPrice(int startIndex, int endIndex);

        /// <summary>
        /// Gets the [low price-bar key] pair from <see cref="startDate"/> to <see cref="endDate"/>.
        /// </summary>
        /// <param name="startDate">The start date.</param>
        /// <param name="endDate">The end date.</param>
        KeyValuePair<DateTime, double> GetLowPrice(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Gets the high price of the candle by the <see cref="index"/> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        double GetHighPrice(int index);

        /// <summary>
        /// Gets the [high price-bar key] pair from <see cref="startIndex"/> to <see cref="endIndex"/>.
        /// </summary>
        /// <param name="startIndex">The start index.</param>
        /// <param name="endIndex">The end index.</param>
        KeyValuePair<int, double> GetHighPrice(int startIndex, int endIndex);

        /// <summary>
        /// Gets the [high price-bar key] pair from <see cref="startDate"/> to <see cref="endDate"/>.
        /// </summary>
        /// <param name="startDate">The start date.</param>
        /// <param name="endDate">The end date.</param>
        KeyValuePair<DateTime, double> GetHighPrice(DateTime startDate, DateTime endDate);

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
        /// Gets the max price of the candle body by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        double GetMaxBodyPrice(int index);

        /// <summary>
        /// Gets the min price of the candle body by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        double GetMinBodyPrice(int index);

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
        /// Gets the limit amount for bars loaded.
        /// </summary>
        int Limit { get; }

        /// <summary>
        /// Gets the start bar index according to limit.
        /// </summary>
        int StartIndexLimit { get; }

        /// <summary>
        /// Gets the time frame of the current instance.
        /// </summary>
        ITimeFrame TimeFrame { get; }

        /// <summary>
        /// Gets the current symbol name (full).
        /// </summary>
        string SymbolName { get; }

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
