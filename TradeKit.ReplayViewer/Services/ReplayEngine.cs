using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.Json;

namespace TradeKit.ReplayViewer.Services;

/// <summary>
/// Orchestrates a bar-by-bar Elliott Wave v2 markup replay over a CSV data file.
/// Builds a zigzag and runs <see cref="ElliottWaveExactMarkupV2.Parse"/> on
/// growing prefixes, collecting delta frames (§17.1) plus the full snapshot (§17).
/// </summary>
public sealed class ReplayEngine
{
    private readonly ReplayBarsProvider m_BarsProvider;

    public ReplayEngine(ReplayBarsProvider barsProvider)
    {
        m_BarsProvider = barsProvider;
    }

    /// <summary>Describes a CSV file available for replay.</summary>
    public static IEnumerable<CsvFileInfo> ListCsvFiles(string dataDir)
    {
        if (!Directory.Exists(dataDir))
            yield break;

        foreach (string f in Directory.EnumerateFiles(dataDir, "*.csv"))
        {
            var fi = new FileInfo(f);
            yield return new CsvFileInfo
            {
                Name = fi.Name,
                Path = f,
                SizeBytes = fi.Length
            };
        }
    }

    /// <summary>Runs the full bar-by-bar replay over <c>[startBar..endBar]</c>.</summary>
    public ReplayData Run(
        string csvPath, int startBar, int endBar, int deadDepth = 1,
        double? deviationPercent = null)
    {
        // 1. Load candles
        m_BarsProvider.LoadCandles(csvPath);

        int n = m_BarsProvider.Count;
        if (endBar <= 0 || endBar >= n)
            endBar = n - 1;
        if (startBar < 0)
            startBar = 0;
        if (startBar >= endBar)
            startBar = Math.Max(0, endBar - 1);

        // Guard: after clamping, end must be strictly after start
        if (endBar <= startBar)
            throw new ArgumentException(
                $"Range too narrow: startBar={startBar}, endBar={endBar}, total bars={n}. " +
                "Try widening the date range.");

        // 2. Candles for web
        var candles = m_BarsProvider.GetAllCandles()
            .Skip(startBar).Take(endBar - startBar + 1).ToList();

        // 3. Build the full zigzag for the range
        var markup = new ElliottWaveExactMarkupV2(
            m_BarsProvider, startBar, endBar, deviationPercent,
            isUpDirection: false);

        // 4. Replay frames (§17.1)
        EwReplayDto replay = EwMarkupTreeExporter.BuildReplay(markup);

        // 5. Snapshot (§17)
        MarkupSearchResult result = markup.Parse(deadDepth);
        EwTreeSnapshotDto snapshot = EwMarkupTreeExporter.BuildSnapshot(markup, result, deadDepth);

        return new ReplayData
        {
            Candles = candles,
            Replay = replay,
            Snapshot = snapshot,
            StartBar = startBar,
            EndBar = endBar
        };
    }

    /// <summary>Runs the replay for a date range (ISO-format strings).</summary>
    public ReplayData RunByDate(
        string csvPath, string? fromDate, string? toDate,
        int deadDepth = 1, double? deviationPercent = null)
    {
        m_BarsProvider.LoadCandles(csvPath);
        (int start, int end) = m_BarsProvider.ResolveDateRange(fromDate ?? "", toDate ?? "");
        return Run(csvPath, start, end, deadDepth, deviationPercent);
    }

    /// <summary>Returns basic info about a CSV file.</summary>
    public CsvFileInfo GetFileInfo(string csvPath)
    {
        var fi = new FileInfo(csvPath);
        var info = new CsvFileInfo
        {
            Name = fi.Name,
            Path = csvPath,
            SizeBytes = fi.Length
        };

        if (fi.Exists)
        {
            // Quick peek: load first bar and last bar
            m_BarsProvider.LoadCandles(csvPath);
            int n = m_BarsProvider.Count;
            if (n > 0)
            {
                info.BarCount = n;
                info.FirstBarTime = m_BarsProvider.GetOpenTime(0).ToString("o");
                info.LastBarTime = m_BarsProvider.GetOpenTime(n - 1).ToString("o");
            }
        }

        return info;
    }
}

// ---- DTOs ----

public sealed class CsvFileInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int BarCount { get; set; }
    public string? FirstBarTime { get; set; }
    public string? LastBarTime { get; set; }
}

public sealed class ReplayData
{
    public IReadOnlyList<CandleBar> Candles { get; set; } = Array.Empty<CandleBar>();
    public EwReplayDto Replay { get; set; } = null!;
    public EwTreeSnapshotDto Snapshot { get; set; } = null!;
    public int StartBar { get; set; }
    public int EndBar { get; set; }
}
