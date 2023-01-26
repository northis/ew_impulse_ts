using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace TradeKit.Core
{
    /// <summary>
    /// Implements the <see cref="IBarsProvider"/> from the cTrader objects.
    /// </summary>
    /// <seealso cref="IBarsProvider" />
    internal class CTraderBarsProvider : IBarsProvider
    {
        private readonly Bars m_Bars;

        /// <summary>
        /// Initializes a new instance of the <see cref="CTraderBarsProvider"/> class.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        public CTraderBarsProvider(Bars bars, Symbol symbolEntity)
        {
            m_Bars = bars;
            Symbol = symbolEntity;
        }

        /// <summary>
        /// Gets the low price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetLowPrice(int index)
        {
            return m_Bars.LowPrices[index];
        }

        /// <summary>
        /// Gets the high price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetHighPrice(int index)
        {
            return m_Bars.HighPrices[index];
        }

        /// <summary>
        /// Gets the open price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetOpenPrice(int index)
        {
            return m_Bars.OpenPrices[index];
        }

        /// <summary>
        /// Gets the close price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetClosePrice(int index)
        {
            return m_Bars.ClosePrices[index];
        }

        /// <summary>
        /// Gets the max price of the candle body by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetMaxBodyPrice(int index)
        {
            return Math.Max(GetClosePrice(index), GetOpenPrice(index));
        }

        /// <summary>
        /// Gets the min price of the candle body by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetMinBodyPrice(int index)
        {
            return Math.Min(GetClosePrice(index), GetOpenPrice(index));
        }

        /// <summary>
        /// Gets the open time of the candle by the <see cref="index" /> specified
        /// </summary>
        /// <param name="index">The index.</param>
        public DateTime GetOpenTime(int index)
        {
            return m_Bars.OpenTimes[index];
        }

        /// <summary>
        /// Gets the total count of bars collected.
        /// </summary>
        public int Count => m_Bars.Count;

        /// <summary>
        /// Loads the bars until <see cref="Limit" /> was reached.
        /// </summary>
        public void LoadBars()
        {
            while (Count < Limit)
            {
                m_Bars.LoadMoreHistory();
            }
        }

        /// <summary>
        /// Gets the limit amount for bars loaded.
        /// </summary>
        public int Limit => Count;

        /// <summary>
        /// Gets the start bar index according by limit.
        /// </summary>
        public int StartIndexLimit => 0;

        /// <summary>
        /// Gets the time frame of the current instance.
        /// </summary>
        public TimeFrame TimeFrame => m_Bars.TimeFrame;

        /// <summary>
        /// Gets the current symbol.
        /// </summary>
        public Symbol Symbol { get; }

        /// <summary>
        /// Gets the int index of bar (candle) by datetime.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        public int GetIndexByTime(DateTime dateTime)
        {
            return m_Bars.OpenTimes.GetIndexByTime(dateTime);
        }

        /// <summary>
        /// Gets the open time for the latest bar available.
        /// </summary>
        public DateTime GetLastBarOpenTime()
        {
            return m_Bars.LastBar.OpenTime;
        }
    }
}
