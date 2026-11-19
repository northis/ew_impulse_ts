using System.Diagnostics;
using System.Text.RegularExpressions;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;

namespace TradeKit.ReplayViewer.Services;

/// <summary>
/// Scans a CSV for the "diagonal as the entry impulse" setups (DIAGONAL.md §9.43): a
/// completed leading 0-1-2-3-4-5 diagonal, then a correction of <c>EnterRatio</c> of its
/// height, then an entry in the direction of the diagonal. The stop is the diagonal start
/// (P0) and the target is a measured move of the diagonal height (by default
/// <c>P0 + 1.0*D</c>, the diagonal end). The simulation is shared with the research harness
/// through <see cref="DiagonalContinuationSimulator"/>, so the chart and the report cannot
/// drift apart.
/// </summary>
public sealed class DiagonalContinuationScanEngine
{
    private const int RIGHT_PADDING_BARS = 8;
    private const int MIN_WARMUP_BARS = 2000;

    public DiagonalContinuationScanResult Scan(
        string csvPath,
        DiagonalContinuationScanRequest req)
    {
        var totalWatch = Stopwatch.StartNew();

        ITimeFrame tf = ResolveTimeFrame(csvPath);
        int maxHold = tf.Name == TimeFrameHelper.Minute15.Name
            ? Math.Max(req.MaxHoldBars, 1000)
            : req.MaxHoldBars;
        int maxWait = tf.Name == TimeFrameHelper.Minute15.Name
            ? Math.Max(req.MaxWaitBars, 600)
            : req.MaxWaitBars;

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
            return new DiagonalContinuationScanResult
            {
                Symbol = symbolName,
                Timeframe = tf.Name
            };

        (int startBar, int endBar) = provider.ResolveDateRange(
            req.FromDate ?? string.Empty, req.ToDate ?? string.Empty);
        if (endBar <= 0 || endBar >= n) endBar = n - 1;
        if (startBar < 0) startBar = 0;

        int warmupBars = Math.Max(MIN_WARMUP_BARS, Math.Max(0, req.Period) * 20);
        int warmupStart = Math.Max(0, startBar - warmupBars);

        // The same finder gates as the "exact corridor" of the research report.
        var ewParams = new EWParams(req.Period, req.MinSizePercent, req.BarsCount);
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
            minWave4RetraceW3: req.MinWave4RetraceW3,
            maxWave4RetraceW3: req.MaxWave4RetraceW3,
            requireWave4Shorter: true,
            requireWave2Shorter: false,
            minWave2Retrace: req.MinWave2Retrace,
            maxWave2Retrace: 0,
            maxWave5SpillRatio: 0,
            takeProfitRetraceRatio: 0.236,
            stopWave3Ratio: 1.0,
            initialMode: req.InitialMode);

        var parameters = new DiagonalContinuationParams(
            Variant: req.Variant,
            EnterRatio: req.EnterRatio,
            TargetKind: req.TargetKind,
            TargetRatio: req.TargetRatio,
            ConfirmRatio: req.ConfirmRatio,
            StopBuffer: req.StopBuffer,
            Ladder: req.Ladder,
            LadderTp2Ratio: req.LadderTp2Ratio,
            MaxHoldBars: maxHold,
            MaxWaitBars: maxWait,
            CostRate: req.CostRate,
            EntryAtNextOpen: req.EntryAtNextOpen);

        var setups = new List<DiagonalContinuationSetupDto>();
        var seen = new HashSet<(int, int, bool)>();
        // Signals are stored as raw wave points. The post-hoc re-mark needs the finished
        // zigzag, so each diagonal is finalised after the replay (DIAGONAL.md 9.46).
        var anchors = new List<ContinuationAnchor>();

        finder.OnEnter += (_, args) =>
        {
            BarPoint[] points = args.WavePoints;
            if (points.Length < 6)
                return;

            // The finder's own trade is counter-trend: a bullish diagonal is a SELL.
            bool isUpDiagonal = args.TakeProfit.Value < args.StopLoss.Value;
            int p0Bar = points[0].BarIndex;
            int signalBar = args.Level.BarIndex;
            if (!seen.Add((p0Bar, signalBar, isUpDiagonal)))
                return;

            anchors.Add(new ContinuationAnchor
            {
                IsUp = isUpDiagonal,
                SignalBar = signalBar,
                Points = points
            });
        };

        finder.MarkAsInitialized();
        // The RSI is driven bar by bar from the warm-up: GetResultValue lazily computes a
        // single bar when it is missing, which seeds from one change and is not usable.
        var rsi = new RelativeStrengthIndexFinder(
            provider, Math.Max(2, req.RsiPeriod), useAutoCalculateEvent: false);
        for (int bar = warmupStart; bar < n; bar++)
        {
            if (bar > endBar && !finder.IsInSetup)
                break;
            finder.CheckBar(provider.GetOpenTime(bar));
            rsi.OnCalculate(provider.GetOpenTime(bar));
        }

        IReadOnlyList<BarPoint> pivots = req.RemarkWaves345
            ? finder.GetRemarkPivots()
            : Array.Empty<BarPoint>();

        foreach (ContinuationAnchor anchor in anchors)
        {
            BarPoint[] points = anchor.Points;
            int signalBar = Math.Max(anchor.SignalBar, points[5].BarIndex);

            // Post-hoc re-mark (DIAGONAL.md 9.46): the finished chart shows the pivots to the
            // right of the diagonal. When the option is on, try later ends for waves 3-5; the
            // re-marked 0-1-2-3-4 must pass the same gates and its 1-3-5 must be collinear.
            bool remarked = false;
            if (req.RemarkWaves345 &&
                finder.TryRemarkWaves345(pivots, points[0], points[1], points[2],
                    anchor.IsUp, req.Remark135Tolerance,
                    out BarPoint rp3, out BarPoint rp4, out BarPoint rp5,
                    afterBar: points[3].BarIndex))
            {
                points = new[] { points[0], points[1], points[2], rp3, rp4, rp5 };
                signalBar = Math.Max(anchor.SignalBar, rp5.BarIndex);
                remarked = true;
            }

            // RSI divergence between the ends of waves 3 and 5: price makes a new extreme
            // with a weaker oscillator, the classic exhaustion cue for the diagonal.
            int rsiP3 = rsi.GetResultValue(points[3].BarIndex);
            int rsiP5 = rsi.GetResultValue(points[5].BarIndex);
            bool hasDivergence = anchor.IsUp
                ? points[5].Value > points[3].Value && rsiP5 < rsiP3
                : points[5].Value < points[3].Value && rsiP5 > rsiP3;

            var setup = new DiagonalContinuationSetup(
                anchor.IsUp,
                signalBar,
                provider.GetOpenTime(signalBar),
                points[0].BarIndex,
                points[0].Value,
                points[5].BarIndex,
                points[5].Value);

            Dictionary<int, DiagonalWave5State> wave5 = BuildWave5Timeline(
                finder, provider, points, anchor.IsUp, signalBar, n, maxWait);

            DiagonalContinuationTrade trade = DiagonalContinuationSimulator.Simulate(
                provider, setup, parameters, wave5);

            // The correction gives us the whole diagonal: re-classify it by its actual end.
            // A wedge that was LEADING at the V(3) break becomes ENDING once wave 5 leaves
            // the preceding counter-move; this page trades initial diagonals only.
            bool isLeadingAtEnd =
                finder.IsLeadingMovement(points[0], trade.EndPrice);
            bool becameEnding = req.InitialMode == DiagonalInitialMode.LEADING &&
                                trade.EntryBar >= 0 &&
                                !isLeadingAtEnd;
            bool isFilled = trade.EntryBar >= 0 && !becameEnding;

            setups.Add(new DiagonalContinuationSetupDto
            {
                IsUpDiagonal = trade.IsUp,
                WavePoints = points
                    .Select(p => new TriangleWavePointDto { Bar = p.BarIndex, Price = p.Value })
                    .ToList(),
                SignalBar = trade.SignalBar,
                SignalTime = trade.SignalTime.ToString("o"),
                P0Bar = trade.P0Bar,
                P0Price = trade.P0,
                DiagonalHeight = trade.D,
                W3Height = Math.Abs(points[3].Value - points[2].Value),
                W5Height = Math.Abs(trade.EndPrice - points[4].Value),
                Line135Error = Line135Error(points),
                RsiP3 = rsiP3,
                RsiP5 = rsiP5,
                HasDivergence = hasDivergence,
                EndBar = trade.EndBar,
                EndPrice = trade.EndPrice,
                EntryBar = isFilled ? trade.EntryBar : -1,
                EntryPrice = isFilled ? trade.Entry : 0,
                StopLoss = isFilled ? trade.Stop : 0,
                TakeProfit1 = isFilled ? trade.Target1 : 0,
                TakeProfit2 = isFilled ? trade.Target2 : 0,
                Risk = isFilled ? trade.Risk : 0,
                PlannedRr = isFilled ? trade.PlannedRr : 0,
                EntryDepth = isFilled ? trade.EntryDepth : 0,
                BarsToFill = isFilled ? trade.BarsToFill : 0,
                BarsToEnd = trade.BarsToEnd,
                Reanchors = trade.Reanchors,
                Confirmations = trade.Confirmations,
                Outcome = becameEnding ? "NOFILL" : trade.Outcome,
                OutcomeBar = isFilled ? trade.ExitBar : -1,
                OutcomePrice = isFilled ? trade.ExitPrice : 0,
                NetR = isFilled ? trade.NetR : 0,
                MfeR = isFilled ? trade.MfeR : 0,
                MaeR = isFilled ? trade.MaeR : 0,
                BarsHeld = isFilled ? trade.BarsHeld : 0,
                NoFillReason = becameEnding ? "becameEnding" : trade.NoFillReason,
                DiagonalBrokenBar = trade.DiagonalBrokenBar,
                BecameEnding = becameEnding,
                IsLeadingAtEnd = isLeadingAtEnd,
                Remarked = remarked
            });
        }

        // Keep the setups whose entry or signal is inside the requested window, then apply
        // the optional 3-5 RSI divergence filter.
        List<DiagonalContinuationSetupDto> kept = setups
            .Where(s => (s.EntryBar >= 0
                            ? s.EntryBar >= startBar && s.EntryBar <= endBar
                            : s.SignalBar >= startBar && s.SignalBar <= endBar))
            .Where(s => req.RsiDivergence switch
            {
                RsiDivergenceFilter.REQUIRE => s.HasDivergence,
                RsiDivergenceFilter.EXCLUDE => !s.HasDivergence,
                _ => true
            })
            .OrderBy(s => s.EntryBar >= 0 ? s.EntryBar : s.SignalBar)
            .ToList();

        for (int i = 0; i < kept.Count; i++)
            kept[i].Id = i;

        var candles = new List<CandleBar>();
        if (kept.Count > 0)
        {
            int minBar = kept.Min(s =>
            {
                int waveStart = s.WavePoints.Count > 0 ? s.WavePoints[0].Bar : s.SignalBar;
                int span = Math.Max(0, (s.EntryBar >= 0 ? s.EntryBar : s.SignalBar) - waveStart);
                return Math.Max(0, waveStart - span);
            });
            int maxBar = kept.Max(s => s.OutcomeBar >= 0
                ? s.OutcomeBar
                : (s.DiagonalBrokenBar >= 0
                    ? s.DiagonalBrokenBar
                    : Math.Max(s.SignalBar, s.EndBar)));
            minBar = Math.Max(0, minBar);
            maxBar = Math.Min(n - 1, maxBar + RIGHT_PADDING_BARS);
            candles = provider.GetAllCandles()
                .Skip(minBar).Take(maxBar - minBar + 1).ToList();
        }

        Console.WriteLine(
            $"[diagonal-continuation] done {totalWatch.Elapsed.TotalSeconds:F1}s " +
            $"(signals {setups.Count}, kept {kept.Count}, candles {candles.Count})");

        List<DiagonalContinuationSetupDto> filled = kept
            .Where(s => s.EntryBar >= 0)
            .ToList();
        List<DiagonalContinuationSetupDto> resolved = filled
            .Where(s => s.Outcome is "TP" or "SL")
            .ToList();

        return new DiagonalContinuationScanResult
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
            SignalCount = setups.Count,
            FilledCount = filled.Count,
            NoFillCount = kept.Count(s => s.EntryBar < 0 &&
                                          s.DiagonalBrokenBar < 0 &&
                                          !s.BecameEnding),
            BrokenCount = kept.Count(s => s.DiagonalBrokenBar >= 0),
            EndingFlipCount = kept.Count(s => s.BecameEnding),
            RemarkedCount = kept.Count(s => s.Remarked),
            DivergenceCount = kept.Count(s => s.HasDivergence),
            ResolvedCount = resolved.Count,
            TpCount = resolved.Count(s => s.Outcome == "TP"),
            SlCount = resolved.Count(s => s.Outcome == "SL"),
            WinCount = resolved.Count(s => s.NetR > 0),
            AvgNetR = resolved.Count == 0 ? 0 : resolved.Average(s => s.NetR),
            AvgMfeR = filled.Count == 0 ? 0 : filled.Average(s => s.MfeR),
            AvgMaeR = filled.Count == 0 ? 0 : filled.Average(s => s.MaeR)
        };
    }

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

    /// <summary>Distance of wave 5 from the 1-3 line, as a share of |W3|.</summary>
    private static double Line135Error(BarPoint[] points)
    {
        int span = points[3].BarIndex - points[1].BarIndex;
        double w3 = Math.Abs(points[3].Value - points[2].Value);
        if (span <= 0 || w3 <= 1e-12)
            return 0;

        double line = points[1].Value +
                      (points[3].Value - points[1].Value) *
                      (points[5].BarIndex - points[1].BarIndex) / span;
        return Math.Abs(points[5].Value - line) / w3;
    }

    /// <summary>
    /// Rebuilds the causal wave-5 validity timeline for a finished diagonal. A move past
    /// V(4) against the diagonal is the correction this setup trades, so the end is frozen
    /// there and the checks resume when the price returns inside the wedge.
    /// </summary>
    private static Dictionary<int, DiagonalWave5State> BuildWave5Timeline(
        DiagonalSetupFinder finder, IBarsProvider provider, BarPoint[] points,
        bool isUp, int signalBar, int n, int maxWait)
    {
        var timeline = new Dictionary<int, DiagonalWave5State>();
        var state = new DiagonalWave5State(
            points[0], points[1], points[2], points[3], points[4],
            isUp, points[5].Value, points[5].BarIndex);
        double p4 = points[4].Value;
        bool frozen = false;
        int sgn = isUp ? 1 : -1;
        int lastBar = Math.Min(n - 1, signalBar + maxWait);

        for (int bar = Math.Max(signalBar, points[5].BarIndex) + 1;
             bar <= lastBar;
             bar++)
        {
            double counter = isUp
                ? provider.GetLowPrice(bar)
                : provider.GetHighPrice(bar);

            if (frozen)
            {
                if (sgn * (counter - p4) < 0)
                {
                    timeline[bar] = state;
                    continue;
                }

                frozen = false;
            }

            if (sgn * (counter - p4) < 0)
            {
                frozen = true;
                timeline[bar] = state;
                continue;
            }

            if (!finder.TryAdvanceWave5(state, bar, out DiagonalWave5State next))
                break;

            state = next;
            timeline[bar] = next;
        }

        return timeline;
    }

    /// <summary>One diagonal signal waiting for its retracement. The wave-5 timeline and
    /// the optional re-mark are built after the replay.</summary>
    private sealed class ContinuationAnchor
    {
        public bool IsUp { get; init; }
        public int SignalBar { get; init; }
        public BarPoint[] Points { get; init; } = Array.Empty<BarPoint>();
    }
}

/// <summary>Parameters for a diagonal → continuation scan.</summary>
public enum RsiDivergenceFilter
{
    OFF,
    REQUIRE,
    EXCLUDE
}

public sealed record DiagonalContinuationScanRequest(
    string? File,
    string? FromDate = null,
    string? ToDate = null,
    int Period = 0,
    double MinSizePercent = 0.1,
    int BarsCount = 10,
    double MinWave2Retrace = 0.5,
    double MinWave4RetraceW3 = 0,
    double MaxWave4RetraceW3 = 0.382,
    bool RemarkWaves345 = false,
    double Remark135Tolerance = 0.1,
    DiagonalInitialMode InitialMode = DiagonalInitialMode.LEADING,
    DiagonalContinuationVariant Variant = DiagonalContinuationVariant.DYNAMIC_REPRICE,
    double EnterRatio = 0.5,
    DiagonalContinuationTargetKind TargetKind = DiagonalContinuationTargetKind.DIAGONAL_START,
    double TargetRatio = 1.0,
    double ConfirmRatio = 0.1,
    double StopBuffer = 0,
    bool Ladder = false,
    double LadderTp2Ratio = 1.618,
    bool EntryAtNextOpen = false,
    double CostRate = 0.0002,
    int MaxHoldBars = 500,
    int MaxWaitBars = 300,
    int RsiPeriod = 14,
    RsiDivergenceFilter RsiDivergence = RsiDivergenceFilter.OFF);

/// <summary>A single diagonal → continuation setup with its simulated outcome.</summary>
public sealed class DiagonalContinuationSetupDto
{
    public int Id { get; set; }

    /// <summary>Direction of the diagonal; the trade runs with it.</summary>
    public bool IsUpDiagonal { get; set; }

    public IReadOnlyList<TriangleWavePointDto> WavePoints { get; set; } =
        Array.Empty<TriangleWavePointDto>();

    public int SignalBar { get; set; }
    public string SignalTime { get; set; } = string.Empty;
    public int P0Bar { get; set; }
    public double P0Price { get; set; }
    public double DiagonalHeight { get; set; }

    /// <summary>|W3| of the diagonal, the D-W5-CAP denominator.</summary>
    public double W3Height { get; set; }

    /// <summary>|W5| to the end used at the fill (or to the signal's P5 on no-fill).</summary>
    public double W5Height { get; set; }

    /// <summary>The running diagonal end the limit was measured from at the fill.</summary>
    public int EndBar { get; set; }
    public double EndPrice { get; set; }

    public int EntryBar { get; set; } = -1;
    public double EntryPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit1 { get; set; }
    public double TakeProfit2 { get; set; }
    public double Risk { get; set; }
    public double PlannedRr { get; set; }
    public double EntryDepth { get; set; }
    public int BarsToFill { get; set; }
    public int BarsToEnd { get; set; }
    public int Reanchors { get; set; }
    public int Confirmations { get; set; }

    public string Outcome { get; set; } = "NOFILL";
    public int OutcomeBar { get; set; } = -1;
    public double OutcomePrice { get; set; }
    public double NetR { get; set; }
    public double MfeR { get; set; }
    public double MaeR { get; set; }
    public int BarsHeld { get; set; }
    public string NoFillReason { get; set; } = string.Empty;

    /// <summary>The bar where the wave-5 validity rules killed the diagonal, or -1.</summary>
    public int DiagonalBrokenBar { get; set; } = -1;

    /// <summary>The completed diagonal is ENDING, so the LEADING-only page skipped it.</summary>
    public bool BecameEnding { get; set; }

    /// <summary>D-INIT at the completed wave-5 end: LEADING vs ENDING.</summary>
    public bool IsLeadingAtEnd { get; set; }

    /// <summary>Waves 3-5 were re-marked post-hoc from later pivots.</summary>
    public bool Remarked { get; set; }

    /// <summary>Distance of the final wave 5 from the 1-3 line, as a share of |W3|.</summary>
    public double Line135Error { get; set; }

    /// <summary>RSI at the end of wave 3 (the configured period).</summary>
    public int RsiP3 { get; set; }

    /// <summary>RSI at the end of wave 5.</summary>
    public int RsiP5 { get; set; }

    /// <summary>Price extreme at wave 5 not confirmed by the RSI: 3-5 divergence.</summary>
    public bool HasDivergence { get; set; }
}

/// <summary>Full result of a diagonal → continuation scan.</summary>
public sealed class DiagonalContinuationScanResult
{
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public int PriceDecimals { get; set; } = 5;
    public int UsedPeriod { get; set; }
    public double MedianBarBps { get; set; }
    public int StartBar { get; set; }
    public int EndBar { get; set; }
    public IReadOnlyList<CandleBar> Candles { get; set; } = Array.Empty<CandleBar>();
    public IReadOnlyList<DiagonalContinuationSetupDto> Setups { get; set; } =
        Array.Empty<DiagonalContinuationSetupDto>();

    public int SignalCount { get; set; }
    public int FilledCount { get; set; }
    public int NoFillCount { get; set; }
    public int BrokenCount { get; set; }
    public int EndingFlipCount { get; set; }
    public int RemarkedCount { get; set; }
    public int DivergenceCount { get; set; }
    public int ResolvedCount { get; set; }
    public int TpCount { get; set; }
    public int SlCount { get; set; }
    public int WinCount { get; set; }
    public double AvgNetR { get; set; }
    public double AvgMfeR { get; set; }
    public double AvgMaeR { get; set; }
}
