using System.Diagnostics;
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

    /// <summary>
    /// Bars of history replayed before the requested window. The finder is causal and needs
    /// a zigzag/candidate warm-up; replaying the whole 2017 history for a 2026 window made
    /// the panel look frozen. 2,000 bars covers the auto period and the scale ladder; a
    /// manually larger period gets a proportional warm-up.
    /// </summary>
    private const int MIN_WARMUP_BARS = 2000;

    /// <summary>Runs a scan over the requested file and date range.</summary>
    public TriangleScanResult Scan(string csvPath, DiagonalScanRequest req)
    {
        var totalWatch = Stopwatch.StartNew();

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

        // Replay only a bounded warm-up before the requested window. A full-range scan
        // still starts at bar 0; a 2026 window on a 2017 file no longer replays 2017-2025.
        int warmupBars = Math.Max(MIN_WARMUP_BARS, Math.Max(0, req.Period) * 20);
        int warmupStart = Math.Max(0, startBar - warmupBars);

        // ── 2. Build the finder from the page inputs ──
        var ewParams = new EWParams(req.Period, req.MinSizePercent, req.BarsCount);
        // The panel exposes the exact corridor, the TP/SL handles, the leading/ending
        // policy and the wave-5 spill / hard R:R gates (DIAGONAL.md §9.36–§9.37).
        var finder = new DiagonalSetupFinder(
            provider, symbol, ewParams,
            takeProfitRatio: 1.0,
            requireWave5Ratio: false,
            requireWave4Ratio: false,
            requireInitialDiagonal: false,
            takeProfitMode: DiagonalTakeProfitMode.DIAGONAL_RETRACE,
            minConvergence: 0,
            maxConvergence: 0,
            requireInsideWedge: false,
            maxSpillAreaRatio: 1.0,
            minWave3Penetration: 0.03,
            maxWaveDurationRatio: 8.0,
            minWave4Wave2Level: 0,
            minWave4RetraceW3: 0,
            maxWave4RetraceW3: req.MaxWave4RetraceW3,
            requireWave4Shorter: true,
            requireWave2Shorter: false,
            minWave2Retrace: req.MinWave2Retrace,
            maxWave2Retrace: 0,
            maxWave5SpillRatio: 0,
            takeProfitRetraceRatio: req.TakeProfitRetraceRatio,
            stopWave3Ratio: req.StopWave3Ratio,
            wave5SpillMode: req.Wave5SpillMode,
            wave5SpillThreshold: req.Wave5SpillThreshold,
            minSignalRiskRewardRatio: req.MinSignalRiskRewardRatio,
            initialMode: req.InitialMode);

        Console.WriteLine(
            $"[diagonal] init {totalWatch.Elapsed.TotalSeconds:F1}s " +
            $"(bars {n}, window {startBar}..{endBar}, warmup {warmupStart})");

        var setups = new List<TriangleSetupDto>();
        TriangleSetupDto? pending = null;
        int enterCount = 0;

        finder.OnEnter += (_, args) =>
        {
            if (args.Level.BarIndex >= startBar)
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

        // ── 3. Drive the finder bar-by-bar (causal) from the bounded warm-up start;
        //       stop once past the window with no open trade. ──
        var replayWatch = Stopwatch.StartNew();
        for (int bar = warmupStart; bar < n; bar++)
        {
            if (bar > endBar && !finder.IsInSetup)
                break;
            finder.CheckBar(provider.GetOpenTime(bar));
        }

        Console.WriteLine(
            $"[diagonal] replay {replayWatch.Elapsed.TotalSeconds:F1}s " +
            $"(bars {n - warmupStart})");

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

        Console.WriteLine(
            $"[diagonal] done {totalWatch.Elapsed.TotalSeconds:F1}s " +
            $"(kept {kept.Count}, resolved {setups.Count}, candles {candles.Count})");

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
            ResolvedCount = kept.Count,
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
    bool RequireInitialDiagonal = false,
    bool TakeProfitAtRetrace = false,
    double MinConvergence = 0,
    double MaxConvergence = 0,
    bool RequireInsideWedge = true,
    double MaxSpillAreaRatio = 0.005,
    double MinWave3Penetration = 0.03,
    double MaxWaveDurationRatio = 8.0,
    bool RequireWave2Shorter = false,
    double MinWave2Retrace = 0.5,
    double MaxWave2Retrace = 0,
    double MaxWave5SpillRatio = 0,
    double TakeProfitRetraceRatio = 0.236,
    double StopWave3Ratio = 1.0,
    double MaxWave4RetraceW3 = 0.382,
    DiagonalInitialMode InitialMode = DiagonalInitialMode.ALL,
    DiagonalWave5SpillMode Wave5SpillMode = DiagonalWave5SpillMode.ALL,
    double Wave5SpillThreshold = 0.005,
    double MinSignalRiskRewardRatio = 0);
