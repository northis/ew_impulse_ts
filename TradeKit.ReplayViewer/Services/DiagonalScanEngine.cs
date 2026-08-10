using System.Text.RegularExpressions;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;

namespace TradeKit.ReplayViewer.Services;

/// <summary>
/// Scans a CSV data file for contracting-diagonal trade setups using the very same
/// <see cref="DiagonalSetupFinder"/> that drives the live cTrader indicator, then records
/// each setup together with its eventual TP/SL outcome. Used by the interactive
/// "diagonal training" page (see DIAGONAL.md §9.2).
/// </summary>
public sealed class DiagonalScanEngine
{
    /// <summary>Extra bars shown to the right of the outcome on the chart.</summary>
    private const int RIGHT_PADDING_BARS = 6;

    /// <summary>Runs a scan over the requested file and date range.</summary>
    public TriangleScanResult Scan(string csvPath, DiagonalScanRequest req)
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
        var finder = new DiagonalSetupFinder(
            provider, symbol, ewParams, req.TakeProfitRatio,
            req.RequireWave5Ratio, req.RequireWave4Ratio, req.RequireInitialMovement,
            req.TakeProfitAtRetrace
                ? DiagonalTakeProfitMode.DIAGONAL_RETRACE
                : DiagonalTakeProfitMode.RISK_RATIO,
            req.MinConvergence,
            req.RequireInsideWedge,
            req.MaxSpillAreaRatio,
            req.MinWave3Penetration,
            req.MaxWaveDurationRatio);

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

    private static string ResolveSymbolName(string csvPath)
    {
        string name = Path.GetFileNameWithoutExtension(csvPath);
        int us = name.IndexOf('_');
        return us > 0 ? name[..us] : name;
    }

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

/// <summary>Parameters for a diagonal scan (page inputs + indicator defaults).</summary>
public sealed record DiagonalScanRequest(
    string? File,
    string? FromDate = null,
    string? ToDate = null,
    int Period = 0,
    double MinSizePercent = 0.1,
    int BarsCount = (int)Helper.MINIMUM_BARS_IN_IMPULSE,
    double TakeProfitRatio = 1.0,
    bool RequireWave5Ratio = false,
    bool RequireWave4Ratio = false,
    bool RequireInitialMovement = false,
    bool TakeProfitAtRetrace = false,
    double MinConvergence = 0,
    bool RequireInsideWedge = true,
    double MaxSpillAreaRatio = 0.005,
    double MinWave3Penetration = 0.03,
    double MaxWaveDurationRatio = 8.0);
