using System;
using cAlgo.API;
using cAlgo.API.Internals;
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
        private int m_TotalBarsCount = 0;
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
            bars.BarClosed += OnBarClosed;
            BarSymbol = symbolEntity;
            TimeFrame = new CTraderTimeFrame(bars.TimeFrame);
        }

        private int GetActualIndex(int index)
        {
            int dCount = m_TotalBarsCount - m_Bars.Count;
            if (dCount > 0)
            {
                Logger.Write($"ActualIndex: {index} -> {dCount} ");
                return index - dCount;
            }

            return index;
        }

        /// <summary>
        /// Creates <see cref="CTraderBarsProvider"/> instance.
        /// </summary>
        /// <param name="timeFrame">The time frame.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        /// <param name="marketData">The market data.</param>
        /// <param name="tradeManager">The trade manager.</param>
        public static CTraderBarsProvider Create(
            ITimeFrame timeFrame, ISymbol symbolEntity, MarketData marketData, CTraderViewManager tradeManager)
        {
            Bars bars = marketData.GetBars(tradeManager.GetCTraderTimeFrame(timeFrame.Name), symbolEntity.Name);
            var cTraderBarsProvider = new CTraderBarsProvider(bars, symbolEntity);
            return cTraderBarsProvider;
        }

        private void UpdateCount()
        {
            if (m_TotalBarsCount < m_Bars.Count)
            {
                m_TotalBarsCount = m_Bars.Count;
            }
            else
            {
                unchecked
                {
                    m_TotalBarsCount++;
                }
            }
        }

        private void OnBarClosed(BarClosedEventArgs obj)
        {
            UpdateCount();
            BarClosed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gets the low price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetLowPrice(int index)
        {
            index = GetActualIndex(index);
            return m_Bars.LowPrices[index];
        }

        /// <summary>
        /// Gets the high price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetHighPrice(int index)
        {
            index = GetActualIndex(index);
            return m_Bars.HighPrices[index];
        }

        /// <summary>
        /// Gets the median price ((H+L)/2) of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public double GetMedianPrice(int index)
        {
            index = GetActualIndex(index);
            return (GetHighPrice(index) + GetLowPrice(index)) / 2;
        }

        /// <summary>
        /// Gets the open price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetOpenPrice(int index)
        {
            index = GetActualIndex(index);
            return m_Bars.OpenPrices[index];
        }

        /// <summary>
        /// Gets the close price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetClosePrice(int index)
        {
            index = GetActualIndex(index);
            return m_Bars.ClosePrices[index];
        }

        /// <summary>
        /// Gets the open time of the candle by the <see cref="index" /> specified
        /// </summary>
        /// <param name="index">The index.</param>
        public DateTime GetOpenTime(int index)
        {
            index = GetActualIndex(index);
            return m_Bars.OpenTimes[index];
        }

        /// <summary>
        /// Gets the total count of bars collected.
        /// </summary>
        public int Count => m_TotalBarsCount;

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

            index = GetActualIndex(index);
            return index;
        }

      
        public event EventHandler BarClosed;

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        public void Dispose()
        {
            m_Bars.BarClosed -= OnBarClosed;
        }
    }
}
