using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo
{
    /// <summary>
    /// Implements the <see cref="IBarsProvider"/> from the cTrader objects.
    /// </summary>
    /// <seealso cref="cAlgo.IBarsProvider" />
    public class CTraderBarsProvider : IBarsProvider
    {
        private readonly Bars m_Bars;
        private readonly MarketData m_MarketData;
        private readonly int m_BarsLimit;

        /// <summary>
        /// Initializes a new instance of the <see cref="CTraderBarsProvider"/> class.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="marketData">The market data.</param>
        /// <param name="barsLimit"></param>
        public CTraderBarsProvider(
            Bars bars, MarketData marketData, int barsLimit)
        {
            m_Bars = bars;
            m_MarketData = marketData;
            m_BarsLimit = barsLimit;
        }

        /// <summary>
        /// Gets the low price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public double GetLowPrice(int index)
        {
            return m_Bars.LowPrices[index];
        }

        /// <summary>
        /// Gets the high price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public double GetHighPrice(int index)
        {
            return m_Bars.HighPrices[index];
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
        public int Limit => m_BarsLimit;

        /// <summary>
        /// Gets the start bar index according by limit.
        /// </summary>
        public int StartIndexLimit => Math.Max(Count - m_BarsLimit, 0);

        /// <summary>
        /// Gets the time frame of the current instance.
        /// </summary>
        public TimeFrame TimeFrame => m_Bars.TimeFrame;

        /// <summary>
        /// Gets the bars of the specified time frame.
        /// </summary>
        /// <param name="timeFrame">The time frame.</param>
        /// <returns>
        /// A new instance for the <see cref="timeFrame" />
        /// </returns>
        public IBarsProvider GetBars(TimeFrame timeFrame)
        {
            if (timeFrame == TimeFrame)
            {
                // Maybe it's better to return null
                return this;
            }
            
            if (m_Bars.Count == 0)
            {
                // What to do if we cannot load the bars? Dunno
                LoadBars();
            }

            // We want to convert the start bar index the datetime
            // because there are other indices on other time frames.
            DateTime currentStartDate = m_Bars.OpenTimes[StartIndexLimit];
            Bars bars = m_MarketData.GetBars(timeFrame);
            do
            {
                bars.LoadMoreHistory();

            } while (bars[^1].OpenTime > currentStartDate);

            var res = new CTraderBarsProvider(bars, m_MarketData, bars.Count);
            return res;
        }

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
