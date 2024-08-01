using System;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Core;
using TradeKit.Core.Common;

namespace TradeKit.CTrader.Core
{
    /// <summary>
    /// Implements the <see cref="IBarsProvider"/> from the cTrader objects.
    /// </summary>
    /// <seealso cref="IBarsProvider" />
    internal class CTraderBarsProvider : IBarsProvider
    {
        private readonly Bars m_Bars;
        private const int MAX_LOAD_ATTEMPTS = 5;

        /// <summary>
        /// Initializes a new instance of the <see cref="CTraderBarsProvider"/> class.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        public CTraderBarsProvider(Bars bars, Symbol symbolEntity) : this(bars, symbolEntity.ToISymbol())
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CTraderBarsProvider"/> class.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        public CTraderBarsProvider(Bars bars, ISymbol symbolEntity)
        {
            m_Bars = bars;
            bars.BarOpened += OnBarOpened;
            BarSymbol = symbolEntity;
            TimeFrame = new CTraderTimeFrame(bars.TimeFrame);
        }

        private void OnBarOpened(BarOpenedEventArgs obj)
        {
            BarOpened?.Invoke(this, System.EventArgs.Empty);
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
        /// Gets the median price ((H+L)/2) of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public double GetMedianPrice(int index)
        {
            return (GetHighPrice(index) + GetLowPrice(index)) / 2;
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
        /// Loads the bars until <see cref="date"/> is reached.
        /// </summary>
        public void LoadBars(DateTime date)
        {
            if (m_Bars.OpenTimes.Count == 0)
                m_Bars.LoadMoreHistory();

            DateTime lastDate;
            while ((lastDate = m_Bars.OpenTimes[0]) > date && 
                   m_Bars.OpenTimes[0] != lastDate)
            {
                m_Bars.LoadMoreHistory();
            }
        }

        /// <summary>
        /// Gets the start bar index according to the limit.
        /// </summary>
        public int StartIndexLimit => 0;

        /// <summary>
        /// Gets the time frame of the current instance.
        /// </summary>
        public ITimeFrame TimeFrame { get; }

        /// <summary>
        /// Gets the current symbol.
        /// </summary>
        public ISymbol BarSymbol { get; }

        /// <summary>
        /// Gets the int index of bar (candle) by datetime.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        public int GetIndexByTime(DateTime dateTime)
        {
            int index;
            int attempts = 0;
            do
            {
                index = m_Bars.OpenTimes.GetIndexByTime(dateTime);
                if (index < 0)
                {
                    m_Bars.LoadMoreHistory();
                }
                else
                {
                    break;
                }

                attempts++;

            } while (attempts < MAX_LOAD_ATTEMPTS);

            return index;
        }

        /// <summary>
        /// Called when a new bar is opened and the previous bar is ready to analyze.
        /// </summary>
        public event EventHandler BarOpened;

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        public void Dispose()
        {
            m_Bars.BarOpened -= OnBarOpened;
        }
    }
}
