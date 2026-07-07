using System.Text.RegularExpressions;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;

namespace TradeKit.ReplayViewer.Services;

/// <summary>
/// Scans a CSV data file for ABCDE-triangle trade setups using the very same
/// <see cref="TriangleSetupFinder"/> that drives the live cTrader indicator, then
/// records each setup together with its eventual TP/SL outcome. Used by the
/// interactive "triangle training" page to quiz the user on profitable-vs-losing
/// situations (trend → triangle correction → thrust in the trend direction).
/// </summary>
public sealed class TriangleScanEngine
{
    /// <summary>Extra bars shown to the right of the outcome on the chart.</summary>
    private const int RIGHT_PADDING_BARS = 6;

    /// <summary>Runs a scan over the requested file and date range.</summary>
    public TriangleScanResult Scan(string csvPath, TriangleScanRequest req)
    {
        // ── 1. Resolve symbol/timeframe and load candles (with correct price digits) ──
        ITimeFrame tf = ResolveTimeFrame(csvPath);

        var probe = new ReplayBarsProvider(tf);
        probe.LoadCandles(csvPath);
        int digits = probe.PriceDecimals;

        string symbolName = ResolveSymbolName(csvPath);
        double pipSize = Math.Pow(10, -digits);
        var symbol = new SymbolBase(symbolName, symbolName, 1, digits, pipSize, pipSize, 100000);

        var provider = new ReplayBarsProvider(tf, symbol);
        provider.LoadCandles(csvPath);

        int n = provider.Count;
        if (n == 0)
            return new TriangleScanResult { Symbol = symbolName, Timeframe = tf.Name };

        (int startBar, int endBar) = provider.ResolveDateRange(
            req.FromDate ?? string.Empty, req.ToDate ?? string.Empty);
        if (endBar <= 0 || endBar >= n) endBar = n - 1;
        if (startBar < 0) startBar = 0;

        // ── 2. Build the finder from the page inputs ──
        var ewParams = new EWParams(req.Period, req.MinSizePercent, req.BarsCount);
        var finder = new TriangleSetupFinder(provider, symbol, ewParams);

        var setups = new List<TriangleSetupDto>();
        TriangleSetupDto? pending = null;
        int enterCount = 0;

        finder.OnEnter += (_, args) =>
        {
            enterCount++;
            bool isUp = args.TakeProfit.Value > args.StopLoss.Value;
            var wavePoints = args.WavePoints
                .Select(p => new TriangleWavePointDto { Bar = p.BarIndex, Price = p.Value })
                .ToList();

            pending = new TriangleSetupDto
            {
                IsUp = isUp,
                WavePoints = wavePoints,
                EntryBar = args.Level.BarIndex,
                EntryPrice = args.Level.Value,
                StopLoss = args.StopLoss.Value,
                TakeProfit = args.TakeProfit.Value,
                ViewStartBar = wavePoints.Count > 0 ? wavePoints[0].Bar : args.Level.BarIndex,
                EntryTime = args.Level.OpenTime.ToString("o"),
                Comment = args.Comment ?? string.Empty,
                Outcome = "NONE"
            };
        };

        void Resolve(string outcome, LevelEventArgs e)
        {
            if (pending == null) return;
            pending.Outcome = outcome;
            pending.OutcomeBar = e.Level.BarIndex;
            pending.OutcomePrice = e.Level.Value;
            setups.Add(pending);
            pending = null;
        }

        finder.OnTakeProfit += (_, e) => Resolve("TP", e);
        finder.OnStopLoss += (_, e) => Resolve("SL", e);

        finder.MarkAsInitialized();

        // ── 3. Drive the finder bar-by-bar (causal). Warm up from bar 0 so the
        //       zigzag has history; stop once past the window with no open trade. ──
        for (int bar = 0; bar < n; bar++)
        {
            if (bar > endBar && !finder.IsInSetup)
                break;
            finder.CheckBar(provider.GetOpenTime(bar));
        }

        // ── 4. Keep only resolved (TP/SL) setups whose entry is inside the window ──
        var kept = setups
            .Where(s => s.EntryBar >= startBar && s.EntryBar <= endBar)
            .Where(s => s.Outcome is "TP" or "SL")
            .OrderBy(s => s.EntryBar)
            .ToList();

        for (int i = 0; i < kept.Count; i++)
            kept[i].Id = i;

        // ── 5. Slice candles to only the region the UI actually draws ──
        var candles = new List<CandleBar>();
        if (kept.Count > 0)
        {
            int minBar = kept.Min(s =>
            {
                int span = Math.Max(0, s.EntryBar - s.ViewStartBar);
                return s.ViewStartBar - span;
            });
            int maxBar = kept.Max(s => s.OutcomeBar);
            minBar = Math.Max(0, minBar);
            maxBar = Math.Min(n - 1, maxBar + RIGHT_PADDING_BARS);
            candles = provider.GetAllCandles()
                .Skip(minBar).Take(maxBar - minBar + 1).ToList();
        }

        return new TriangleScanResult
        {
            Symbol = symbolName,
            Timeframe = tf.Name,
            PriceDecimals = digits,
            UsedPeriod = finder.ZigzagPeriod,
            MedianBarBps = AutoPeriodEstimator.MedianBarBps(provider),
            StartBar = startBar,
            EndBar = endBar,
            Candles = candles,
            Setups = kept,
            EnterCount = enterCount,
            ResolvedCount = setups.Count,
            BarCount = n
        };
    }

    // ── helpers ──────────────────────────────────────────

    /// <summary>Extracts the symbol name from a CSV filename ("AUDCAD_h1_...csv" → "AUDCAD").</summary>
    private static string ResolveSymbolName(string csvPath)
    {
        string name = Path.GetFileNameWithoutExtension(csvPath);
        int us = name.IndexOf('_');
        return us > 0 ? name[..us] : name;
    }

    /// <summary>Extracts the timeframe from a CSV filename ("AUDCAD_h1_...csv" → Hour1).</summary>
    private static ITimeFrame ResolveTimeFrame(string csvPath)
    {
        string name = Path.GetFileNameWithoutExtension(csvPath);
        Match m = Regex.Match(name, @"_([a-zA-Z]\d{1,2})_");
        if (m.Success)
        {
            string shortName = m.Groups[1].Value;
            TimeFrameInfo? match = TimeFrameHelper.TimeFrames.Values
                .FirstOrDefault(v => string.Equals(
                    v.TimeFrame.ShortName, shortName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match.TimeFrame;
        }
        return TimeFrameHelper.Hour1;
    }
}

/// <summary>Parameters for a triangle scan (page inputs + indicator defaults).</summary>
public sealed record TriangleScanRequest(
    string? File,
    string? FromDate = null,
    string? ToDate = null,
    int Period = 0,
    double MinSizePercent = 0.3,
    int BarsCount = (int)Helper.MINIMUM_BARS_IN_IMPULSE);

/// <summary>A single wave pivot of a detected triangle (bar + price).</summary>
public sealed class TriangleWavePointDto
{
    public int Bar { get; set; }
    public double Price { get; set; }
}

/// <summary>A single detected triangle setup with its resolved TP/SL outcome.</summary>
public sealed class TriangleSetupDto
{
    public int Id { get; set; }
    public bool IsUp { get; set; }

    /// <summary>Six pivots: 0, A, B, C, D, E.</summary>
    public IReadOnlyList<TriangleWavePointDto> WavePoints { get; set; } =
        Array.Empty<TriangleWavePointDto>();

    public int EntryBar { get; set; }
    public double EntryPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }

    public int ViewStartBar { get; set; }
    public string EntryTime { get; set; } = string.Empty;

    /// <summary>Model + fibo score info from the finder ("TRIANGLE_CONTRACTING fibo=0.83").</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>"TP", "SL" or "NONE".</summary>
    public string Outcome { get; set; } = "NONE";
    public int OutcomeBar { get; set; }
    public double OutcomePrice { get; set; }
}

/// <summary>Full result of a triangle scan: metadata, candles and the detected setups.</summary>
public sealed class TriangleScanResult
{
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public int PriceDecimals { get; set; } = 5;

    /// <summary>The zigzag period actually used (auto-detected when the request period was 0).</summary>
    public int UsedPeriod { get; set; }

    /// <summary>Median bar range (bps) of the instrument — the volatility the auto-period is based on.</summary>
    public double MedianBarBps { get; set; }

    public int StartBar { get; set; }
    public int EndBar { get; set; }
    public IReadOnlyList<CandleBar> Candles { get; set; } = Array.Empty<CandleBar>();
    public IReadOnlyList<TriangleSetupDto> Setups { get; set; } = Array.Empty<TriangleSetupDto>();

    // Diagnostics.
    public int EnterCount { get; set; }
    public int ResolvedCount { get; set; }
    public int BarCount { get; set; }
}
