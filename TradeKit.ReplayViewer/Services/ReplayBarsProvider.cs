using System.Globalization;
using TradeKit.Core.Common;

namespace TradeKit.ReplayViewer.Services;

/// <summary>
/// Lightweight in-memory <see cref="IBarsProvider"/> for replay, backed by CSV data.
/// </summary>
public sealed class ReplayBarsProvider : IBarsProvider
{
    private readonly List<Candle> m_Candles = new();
    private readonly Dictionary<int, DateTime> m_OpenTimes = new();
    private readonly Dictionary<DateTime, int> m_TimeToIndex = new();

    public ReplayBarsProvider(ITimeFrame timeFrame, ISymbol? barSymbol = null)
    {
        TimeFrame = timeFrame;
        BarSymbol = barSymbol ?? new SymbolBase("SYM", "Replay Symbol", 1, 5, 0.00001, 0.01, 100000);
    }

    public ITimeFrame TimeFrame { get; }
    public ISymbol BarSymbol { get; }
    public int Count => m_Candles.Count;
    public int StartIndexLimit => 0;
    public event EventHandler? BarClosed;

    /// <summary>Loads OHLC candles from a CSV file (data/*.csv format).</summary>
    public void LoadCandles(string pathToFile, DateTime? from = null, DateTime? to = null)
    {
        if (!File.Exists(pathToFile))
            throw new FileNotFoundException("Candle data file not found", pathToFile);

        // Clear previous data — each load is a fresh start
        m_Candles.Clear();
        m_OpenTimes.Clear();
        m_TimeToIndex.Clear();

        var lines = File.ReadAllLines(pathToFile);
        if (lines.Length == 0) return;

        // Check header
        int startLine = lines[0].StartsWith("Time") ||
                        lines[0].Contains($"Open{Helper.CSV_SEPARATOR}High{Helper.CSV_SEPARATOR}Low{Helper.CSV_SEPARATOR}Close")
            ? 1 : 0;

        for (int i = startLine; i < lines.Length; i++)
        {
            string[] parts = lines[i].Split(Helper.CSV_SEPARATOR);
            if (parts.Length < 5) continue;

            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime openTime))
                continue;
            openTime = DateTime.SpecifyKind(openTime, DateTimeKind.Utc);

            if (from.HasValue && openTime < from.Value) continue;
            if (to.HasValue && openTime > to.Value) continue;

            if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double open)) continue;
            if (!double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double high)) continue;
            if (!double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double low)) continue;
            if (!double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double close)) continue;

            int index = m_Candles.Count;
            m_Candles.Add(new Candle(open, high, low, close, null, index));
            m_OpenTimes[index] = openTime;
            m_TimeToIndex[openTime] = index;
        }
    }

    /// <summary>Returns candle OHLC data for the web UI (bar-by-bar).</summary>
    public IReadOnlyList<CandleBar> GetAllCandles()
    {
        var result = new List<CandleBar>(m_Candles.Count);
        for (int i = 0; i < m_Candles.Count; i++)
        {
            result.Add(new CandleBar
            {
                Time = m_OpenTimes[i].ToString("o"),
                Open = m_Candles[i].O,
                High = m_Candles[i].H,
                Low = m_Candles[i].L,
                Close = m_Candles[i].C,
                BarIndex = i
            });
        }
        return result;
    }

    // ---- IBarsProvider ----

    public double GetLowPrice(int index) => m_Candles[index].L;
    public double GetHighPrice(int index) => m_Candles[index].H;
    public double GetMedianPrice(int index) => (m_Candles[index].H + m_Candles[index].L) / 2.0;
    public double GetOpenPrice(int index) => m_Candles[index].O;
    public double GetClosePrice(int index) => m_Candles[index].C;
    public DateTime GetOpenTime(int index) => m_OpenTimes[index];

    public int GetIndexByTime(DateTime dateTime) =>
        m_TimeToIndex.TryGetValue(dateTime, out int idx) ? idx : -1;

    public void LoadBars(DateTime date) { /* preloaded */ }

    public void Dispose() { }

    private void OnBarClosed() => BarClosed?.Invoke(this, EventArgs.Empty);

    /// <summary>Returns bar index closest to <paramref name="date"/> (UTC).</summary>
    public int FindBarIndex(DateTime date)
    {
        int idx = GetIndexByTime(date);
        if (idx >= 0) return idx;

        int best = 0;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < m_Candles.Count; i++)
        {
            double diff = Math.Abs((m_OpenTimes[i] - date).TotalMilliseconds);
            if (diff < bestDiff) { bestDiff = diff; best = i; }
        }
        return best;
    }

    public (int first, int last) GetBarRange() => (0, m_Candles.Count - 1);

    public (int start, int end) ResolveDateRange(string fromDate, string toDate)
    {
        int start = 0, end = m_Candles.Count - 1;
        if (!string.IsNullOrWhiteSpace(fromDate) &&
            DateTime.TryParse(fromDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime from))
            start = FindBarIndex(from);
        if (!string.IsNullOrWhiteSpace(toDate) &&
            DateTime.TryParse(toDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime to))
            end = FindBarIndex(to);
        if (start > end) (start, end) = (end, start);
        return (start, end);
    }
}

/// <summary>Lightweight OHLC bar DTO for JSON serialisation to the web client.</summary>
public sealed class CandleBar
{
    public string Time { get; set; } = string.Empty;
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public int BarIndex { get; set; }
}
