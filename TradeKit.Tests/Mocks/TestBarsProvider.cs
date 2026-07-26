using System.Globalization;
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

        /// <summary>
        /// Initializes a new instance of the <see cref="TestBarsProvider"/> class.
        /// </summary>
        /// <param name="timeFrame">The time frame.</param>
        /// <param name="barSymbol">The bar symbol.</param>
        public TestBarsProvider(ITimeFrame timeFrame, ISymbol? barSymbol = null)
        {
            TimeFrame = timeFrame;
            BarSymbol = barSymbol ?? new SymbolBase("TEST", "Test Symbol", 1, 5, 0.00001, 0.01, 100000);
        }

        /// <summary>
        /// Loads and processes OHLC candles from the specified .csv file.
        /// </summary>
        /// <param name="pathToFile">The path to the file containing the data to load.</param>
        /// <param name="from">Optional UTC start time filter (inclusive).</param>
        /// <param name="to">Optional UTC end time filter (inclusive).</param>
        /// <remarks>
        /// Both the semicolon-separated archive format of <c>data/</c> and the comma-separated
        /// TradingView "Export chart data" format are accepted; extra columns after Close are
        /// ignored, so a Pine reference export can be replayed as a plain candle series.
        /// </remarks>
        public void LoadCandles(string pathToFile, DateTime? from = null, DateTime? to = null)
        {
            if (string.IsNullOrEmpty(pathToFile))
                throw new ArgumentNullException(nameof(pathToFile));
        
            if (!File.Exists(pathToFile))
                throw new FileNotFoundException("Candle data file not found", pathToFile);
        
            using var reader = new StreamReader(pathToFile);
            string? line = reader.ReadLine();
            
            if (line == null)
                return;

            char separator = DetectSeparator(line);
            bool hasHeader = line.StartsWith("time", StringComparison.OrdinalIgnoreCase);

            int index = 0;
            if (!hasHeader)
            {
                ProcessCandleLine(line, separator, from, to, ref index);
            }

            while ((line = reader.ReadLine()) != null)
            {
                ProcessCandleLine(line, separator, from, to, ref index);
            }
        }

        /// <summary>
        /// Detects the column separator of the file from its first line.
        /// </summary>
        /// <param name="firstLine">The first line of the file.</param>
        public static char DetectSeparator(string firstLine)
        {
            if (firstLine.Contains(Helper.CSV_SEPARATOR))
                return Helper.CSV_SEPARATOR;

            return firstLine.Contains(',') ? ',' : Helper.CSV_SEPARATOR;
        }

        /// <summary>
        /// Parses a UTC timestamp of the candle file. A value without a time zone suffix is
        /// treated as UTC; a value with one (for example the trailing <c>Z</c> of a TradingView
        /// export) is converted to UTC instead of the machine local time.
        /// </summary>
        /// <param name="value">The raw timestamp.</param>
        /// <param name="openTime">The parsed UTC time.</param>
        public static bool TryParseUtc(string value, out DateTime openTime)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out openTime);
        }
        
        private void ProcessCandleLine(
            string line, char separator, DateTime? from, DateTime? to, ref int addedIndex)
        {
            string[] parts = line.Split(separator);
            if (parts.Length < 5)
                return;
        
            if (!TryParseUtc(parts[0], out DateTime openTime))
                return;

            openTime = DateTime.SpecifyKind(openTime, DateTimeKind.Utc);

            if (from.HasValue && openTime < from.Value) return;
            if (to.HasValue && openTime > to.Value) return;
                
            if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double open))
                return;
                
            if (!double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double high))
                return;
                
            if (!double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double low))
                return;
                
            if (!double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double close))
                return;

            Candle candle = new Candle(open, high, low, close, null, addedIndex);
            AddCandle(candle, openTime);
            addedIndex++;
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
            
            OnBarClosed();
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
            return m_OpenTimes.TryGetValue(index, out DateTime dt) ? dt : DateTime.MinValue;
        }

        public int GetIndexByTime(DateTime time)
        {
            return m_TimeToIndexMap.GetValueOrDefault(time, -1);
        }

        public event EventHandler? BarClosed;

        public void LoadBars(DateTime date)
        {
            // In test implementation, we don't need to load bars from external source
            // as they are manually added via AddCandle method
        }
        
        protected virtual void OnBarClosed()
        {
            BarClosed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            // No resources to dispose
        }
    }
}
