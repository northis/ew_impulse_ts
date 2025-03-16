using TradeKit.Core.Common;

namespace TradeKit.Tests.Mocks
{
    /// <summary>
    /// Test implementation of IBarsProvider for unit testing.
    /// </summary>
    internal class TestBarsProvider : IBarsProvider
    {
        private readonly List<Candle> m_Candles = new List<Candle>();
        private readonly Dictionary<int, DateTime> m_OpenTimes = new Dictionary<int, DateTime>();
        private readonly Dictionary<DateTime, int> m_TimeToIndexMap = new Dictionary<DateTime, int>();

        public ITimeFrame TimeFrame { get; }
        
        public int Count => m_Candles.Count;
        
        public int StartIndexLimit => 0;
        
        public ISymbol BarSymbol { get; }

        public event EventHandler? BarOpened;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestBarsProvider"/> class.
        /// </summary>
        /// <param name="timeFrame">The time frame.</param>
        /// <param name="barSymbol">The bar symbol.</param>
        public TestBarsProvider(ITimeFrame timeFrame, ISymbol? barSymbol = default)
        {
            TimeFrame = timeFrame;
            BarSymbol = barSymbol ?? new SymbolBase("TEST", "Test Symbol", 1, 5, 0.00001, 0.01, 100000);
        }

        /// <summary>
        /// Adds a candle to the provider.
        /// </summary>
        /// <param name="candle">The candle to add.</param>
        /// <param name="openTime">The open time of the candle.</param>
        public void AddCandle(Candle candle, DateTime openTime)
        {
            int index = m_Candles.Count;
            m_Candles.Add(candle);
            m_OpenTimes[index] = openTime;
            m_TimeToIndexMap[openTime] = index;
            
            // Trigger BarOpened event for the newly added candle
            if (index > 0)
            {
                OnBarOpened();
            }
        }

        /// <summary>
        /// Adds multiple candles to the provider.
        /// </summary>
        /// <param name="candles">The collection of candles with their open times.</param>
        public void AddCandles(IEnumerable<(Candle candle, DateTime openTime)> candles)
        {
            foreach (var (candle, openTime) in candles)
            {
                AddCandle(candle, openTime);
            }
        }

        public double GetLowPrice(int index)
        {
            return m_Candles[index].L;
        }

        public double GetHighPrice(int index)
        {
            return m_Candles[index].H;
        }

        public double GetMedianPrice(int index)
        {
            return (m_Candles[index].H + m_Candles[index].L) / 2;
        }

        public double GetOpenPrice(int index)
        {
            return m_Candles[index].O;
        }

        public double GetClosePrice(int index)
        {
            return m_Candles[index].C;
        }

        public DateTime GetOpenTime(int index)
        {
            return m_OpenTimes[index];
        }

        public int GetIndexByTime(DateTime time)
        {
            return m_TimeToIndexMap.TryGetValue(time, out int index) ? index : -1;
        }
        
        public void LoadBars(DateTime date)
        {
            // In test implementation, we don't need to load bars from external source
            // as they are manually added via AddCandle method
        }
        
        protected virtual void OnBarOpened()
        {
            BarOpened?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            // No resources to dispose
        }
    }
}
