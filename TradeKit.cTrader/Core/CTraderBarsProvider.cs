using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private const int MAX_BARS_TO_KEEP = 10000;
        private const int CLEAN_EVERY = 1000;
        
        private readonly SortedDictionary<DateTime, int> m_CandlesDate;
        private readonly SortedDictionary<int, Candle> m_CandlesIndex;

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
            m_CandlesDate = new SortedDictionary<DateTime, int>();
            m_CandlesIndex = new SortedDictionary<int, Candle>();
            m_Bars = bars;
            ReloadBars();
            bars.BarClosed += OnBarClosed;
            BarSymbol = symbolEntity;
            TimeFrame = new CTraderTimeFrame(bars.TimeFrame);
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
            unchecked
            {
                m_TotalBarsCount++;
            }
        }

        private void ReloadBars()
        {
            m_CandlesDate.Clear();
            m_CandlesIndex.Clear();
            for (int i = 0; i < m_Bars.OpenTimes.Count; i++)
            {
                DateTime dt = m_Bars.OpenTimes[i];
                Candle candle = new Candle(m_Bars.OpenPrices[i], 
                    m_Bars.HighPrices[i], 
                    m_Bars.LowPrices[i],
                    m_Bars.ClosePrices[i], null, i, dt);
                m_CandlesIndex.Add(i, candle);
                m_CandlesDate.Add(dt, i);
            }

            m_TotalBarsCount = m_Bars.OpenTimes.Count;
        }

        private void UpdateLastBar()
        {
            Bar lastBar = m_Bars.LastBar;
            if (m_CandlesDate.ContainsKey(lastBar.OpenTime))
            {
                return;
            }
            
            Candle candle = new Candle(lastBar.Open, lastBar.High, lastBar.Low,
                lastBar.Close, null, m_TotalBarsCount, lastBar.OpenTime);
            m_CandlesIndex.Add(m_TotalBarsCount, candle);
            m_CandlesDate.Add(lastBar.OpenTime, m_TotalBarsCount);
            UpdateCount();

            if (m_TotalBarsCount % CLEAN_EVERY != 0) return;
            
            int countDates = m_CandlesDate.Count;
            int countCandles = m_CandlesIndex.Count;
            if (countCandles != countDates)
            {
                Logger.Write(
                    $"Wrong logic, check the code! Count of candles: {countCandles}, count of dates: {countDates}.");
            }
                
            if (countDates > MAX_BARS_TO_KEEP)
                m_CandlesDate.RemoveLeftTop(countDates - MAX_BARS_TO_KEEP);
                
            if (countCandles > MAX_BARS_TO_KEEP)
                m_CandlesIndex.RemoveLeftTop(countCandles - MAX_BARS_TO_KEEP);
        }

        private void OnBarClosed(BarClosedEventArgs obj)
        {
            UpdateLastBar();
            BarClosed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gets the low price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetLowPrice(int index)
        {
            return m_CandlesIndex[index].L;
        }

        /// <summary>
        /// Gets the high price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetHighPrice(int index)
        {
            return m_CandlesIndex[index].H;
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
            return m_CandlesIndex[index].O;
        }

        /// <summary>
        /// Gets the close price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetClosePrice(int index)
        {
            return m_CandlesIndex[index].C;
        }

        /// <summary>
        /// Gets the open time of the candle by the <see cref="index" /> specified
        /// </summary>
        /// <param name="index">The index.</param>
        public DateTime GetOpenTime(int index)
        {
            return m_CandlesIndex[index].OpenDateTime.GetValueOrDefault();
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
            m_Bars.BarClosed -= OnBarClosed;
            if (m_Bars.OpenTimes.Count == 0)
                m_Bars.LoadMoreHistory();

            DateTime lastDate;
            while ((lastDate = m_Bars.OpenTimes[0]) > date && 
                   m_Bars.OpenTimes[0] != lastDate)
            {
                m_Bars.LoadMoreHistory();
            }

            ReloadBars();
            m_Bars.BarClosed += OnBarClosed;
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
            return m_CandlesDate[dateTime];
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
