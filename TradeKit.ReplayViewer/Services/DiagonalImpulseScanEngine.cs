using System.Diagnostics;
using System.Text.RegularExpressions;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;

namespace TradeKit.ReplayViewer.Services;

/// <summary>
/// Scans a CSV for the "diagonal → reversal impulse" setups (DIAGONAL.md §9.44): a
/// completed 0-1-2-3-4-5 diagonal, then an impulse A against it, then a correction B of
/// <c>EnterRatio</c> of A. The second impulse C is traded in the direction of A (against
/// the diagonal), the stop is behind the start of A (= the diagonal end) and the targets
/// are 1.0 (the end of A) and 1.272 of A from its start. Used by the
/// "diagonal impulse" page.
/// </summary>
public sealed class DiagonalImpulseScanEngine
{
    private const int RIGHT_PADDING_BARS = 8;
    private const int MIN_WARMUP_BARS = 2000;

    public DiagonalImpulseScanResult Scan(
        string csvPath,
        DiagonalImpulseScanRequest req)
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
            return new DiagonalImpulseScanResult
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
            minWave4RetraceW3: 0,
            maxWave4RetraceW3: req.MaxWave4RetraceW3,
            requireWave4Shorter: true,
            requireWave2Shorter: false,
            minWave2Retrace: req.MinWave2Retrace,
            maxWave2Retrace: 0,
            maxWave5SpillRatio: 0,
            takeProfitRetraceRatio: 0.236,
            stopWave3Ratio: 1.0,
            initialMode: req.InitialMode);

        // The A leg is a short swing; the diagonal's own zigzag is too coarse for it.
        int impulsePeriod = AutoPeriodEstimator.EstimateImpulsePeriod(provider);
        DiagonalAbcPivotTimeline timeline =
            DiagonalAbcPivotTimeline.Build(provider, impulsePeriod);

        var parameters = new DiagonalAbcParams(
            EnterRatio: req.EnterRatio,
            MinARatio: req.MinARatio,
            MinABars: req.MinABars,
            MaxABars: req.MaxABars,
            RequireSmoothA: req.RequireSmoothA,
            AStartToleranceW3: req.AStartToleranceW3,
            AStartBarTolerance: req.AStartBarTolerance,
            StopBuffer: req.StopBuffer,
            Target1Ratio: req.Target1Ratio,
            Ladder: req.Ladder,
            Target2Ratio: req.Target2Ratio,
            CostRate: req.CostRate,
            Execution: req.Execution,
            MaxWaitBars: maxWait,
            MaxHoldBars: maxHold,
            RequireAInsideP0: req.RequireAInsideP0);

        var setups = new List<DiagonalImpulseSetupDto>();
        var seen = new HashSet<(int, int, bool)>();
        var anchors = new List<DiagonalAnchor>();
        var rsi = new RelativeStrengthIndexFinder(
            provider, Math.Max(2, req.RsiPeriod), useAutoCalculateEvent: false);

        finder.OnEnter += (_, args) =>
        {
            BarPoint[] points = args.WavePoints;
            if (points.Length < 6)
                return;

            // The diagonal trade itself is counter-trend: a bullish diagonal is a SELL.
            bool isUpDiagonal = args.TakeProfit.Value < args.StopLoss.Value;
            int p0Bar = points[0].BarIndex;
            int signalBar = args.Level.BarIndex;
            if (!seen.Add((p0Bar, signalBar, isUpDiagonal)))
                return;

            int rsiP3 = rsi.GetResultValue(points[3].BarIndex);
            int rsiP5 = rsi.GetResultValue(points[5].BarIndex);
            bool hasDivergence = isUpDiagonal
                ? points[5].Value > points[3].Value && rsiP5 < rsiP3
                : points[5].Value < points[3].Value && rsiP5 > rsiP3;

            var setup = new DiagonalAbcSetup(
                isUpDiagonal,
                signalBar,
                args.Level.OpenTime,
                p0Bar,
                points[0].Value,
                points[4].BarIndex,
                points[4].Value,
                points[5].BarIndex,
                points[5].Value,
                Math.Abs(points[3].Value - points[2].Value));

            int aScanBars = Math.Clamp(3 * (setup.P4Bar - setup.P0Bar), 50, 500);
            anchors.Add(new DiagonalAnchor
            {
                Setup = setup,
                Points = points,
                RsiP3 = rsiP3,
                RsiP5 = rsiP5,
                HasDivergence = hasDivergence,
                State = new DiagonalWave5State(
                    points[0], points[1], points[2], points[3], points[4],
                    isUpDiagonal, points[5].Value, points[5].BarIndex),
                Wave5 = new Dictionary<int, DiagonalWave5State>(),
                LastBar = Math.Min(n - 1,
                    Math.Max(signalBar, setup.P5Bar) + aScanBars + maxWait + 20)
            });
        };

        finder.MarkAsInitialized();
        for (int bar = warmupStart; bar < n; bar++)
        {
            if (bar > endBar && !finder.IsInSetup)
                break;
            finder.CheckBar(provider.GetOpenTime(bar));
            rsi.OnCalculate(provider.GetOpenTime(bar));

            // Keep the diagonal valid while we wait for the impulse: wave 5 may move to the
            // right, but the same D-W5-CAP / V(4) / duration rules must still hold. The
            // state is advanced on the same bar the finder just closed, so the print filter
            // and every input stay causal.
            foreach (DiagonalAnchor anchor in anchors)
            {
                if (!anchor.Alive || bar <= anchor.SignalBar || bar > anchor.LastBar)
                    continue;

                if (anchor.Frozen)
                {
                    anchor.Wave5[bar] = anchor.State;
                    continue;
                }

                // A move past V(4) against the diagonal after the signal is the impulse A
                // itself, not a broken diagonal: freeze wave 5 at its last valid end and
                // let the A/B/C simulation take over. The D-W5-CAP/duration checks still
                // apply while the price has not started the counter-move.
                int sgn = anchor.Setup.IsUpDiagonal ? 1 : -1;
                double counter = anchor.Setup.IsUpDiagonal
                    ? provider.GetLowPrice(bar)
                    : provider.GetHighPrice(bar);
                if (sgn * (counter - anchor.Setup.P4) < 0)
                {
                    anchor.Frozen = true;
                    anchor.Wave5[bar] = anchor.State;
                    continue;
                }

                if (!finder.TryAdvanceWave5(anchor.State, bar, out DiagonalWave5State next))
                {
                    anchor.Alive = false;
                    anchor.BrokenBar = bar;
                    continue;
                }

                anchor.State = next;
                anchor.Wave5[bar] = next;
            }
        }

        foreach (DiagonalAnchor anchor in anchors)
        {
            DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                provider, anchor.Setup, parameters, timeline, anchor.Wave5);

            setups.Add(new DiagonalImpulseSetupDto
            {
                IsUpDiagonal = anchor.Setup.IsUpDiagonal,
                WavePoints = anchor.Points
                    .Select(p => new TriangleWavePointDto { Bar = p.BarIndex, Price = p.Value })
                    .ToList(),
                SignalBar = anchor.Setup.SignalBar,
                SignalTime = anchor.Setup.SignalTime.ToString("o"),
                DiagonalHeight = trade.DiagonalHeight,
                AStartBar = trade.AStartBar,
                AStartPrice = trade.AStart,
                AEndBar = trade.AEndBar,
                AEndPrice = trade.AEnd,
                AHeight = trade.AHeight,
                BEndBar = trade.BEndBar,
                BEndPrice = trade.BEnd,
                BDepth = trade.BDepth,
                SetupWorked = trade.SetupWorked,
                WorkedBar = trade.WorkedBar,
                WorkedPrice = trade.WorkedPrice,
                EntryBar = trade.EntryBar,
                EntryPrice = trade.Entry,
                StopLoss = trade.Stop,
                TakeProfit1 = trade.Target1,
                TakeProfit2 = trade.Target2,
                Risk = trade.Risk,
                PlannedRr = trade.PlannedRr,
                EntryDepth = trade.EntryDepth,
                BarsToFill = trade.BarsToFill,
                BarsToEnd = trade.BarsToEnd,
                Outcome = trade.Outcome,
                OutcomeBar = trade.ExitBar,
                OutcomePrice = trade.ExitPrice,
                NetR = trade.NetR,
                StressNetR = trade.StressNetR,
                MfeR = trade.MfeR,
                MaeR = trade.MaeR,
                BarsHeld = trade.BarsHeld,
                Target1Hit = trade.Target1Hit,
                Target2Hit = trade.Target2Hit,
                NoFillReason = trade.NoFillReason,
                DiagonalBrokenBar = trade.DiagonalBrokenBar,
                RsiP3 = anchor.RsiP3,
                RsiP5 = anchor.RsiP5,
                HasDivergence = anchor.HasDivergence
            });
        }

        // Keep the setups whose signal or entry is inside the requested window. No-fills
        // have no entry bar, so they are kept when the signal is inside the window. The
        // optional 3-5 RSI divergence filter applies after that.
        List<DiagonalImpulseSetupDto> kept = setups
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
            int maxBar = kept.Max(s => s.OutcomeBar >= 0 ? s.OutcomeBar : s.SignalBar);
            minBar = Math.Max(0, minBar);
            maxBar = Math.Min(n - 1, maxBar + RIGHT_PADDING_BARS);
            candles = provider.GetAllCandles()
                .Skip(minBar).Take(maxBar - minBar + 1).ToList();
        }

        Console.WriteLine(
            $"[diagonal-impulse] done {totalWatch.Elapsed.TotalSeconds:F1}s " +
            $"(signals {setups.Count}, kept {kept.Count}, candles {candles.Count})");

        List<DiagonalImpulseSetupDto> filled = kept
            .Where(s => s.EntryBar >= 0)
            .ToList();
        List<DiagonalImpulseSetupDto> resolved = filled
            .Where(s => s.Outcome is "TP" or "SL")
            .ToList();
        List<DiagonalImpulseSetupDto> skipped = kept
            .Where(s => s.EntryBar < 0 && s.SetupWorked)
            .ToList();
        List<DiagonalImpulseSetupDto> broken = kept
            .Where(s => s.DiagonalBrokenBar >= 0)
            .ToList();
        List<DiagonalImpulseSetupDto> impulseBroken = kept
            .Where(s => s.NoFillReason == "impulseBreaksP0")
            .ToList();

        return new DiagonalImpulseScanResult
        {
            Symbol = symbolName,
            Timeframe = tf.Name,
            PriceDecimals = digits,
            UsedPeriod = finder.ZigzagPeriod,
            ImpulsePeriod = impulsePeriod,
            MedianBarBps = AutoPeriodEstimator.MedianBarBps(provider),
            StartBar = startBar,
            EndBar = endBar,
            Candles = candles,
            Setups = kept,
            SignalCount = setups.Count,
            FilledCount = filled.Count,
            ResolvedCount = resolved.Count,
            SkippedCount = skipped.Count,
            BrokenCount = broken.Count,
            ImpulseBrokenCount = impulseBroken.Count,
            DivergenceCount = kept.Count(s => s.HasDivergence),
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

    /// <summary>One diagonal signal waiting for its impulse, with the causal wave-5 timeline.</summary>
    private sealed class DiagonalAnchor
    {
        public DiagonalAbcSetup Setup { get; init; }
        public int SignalBar => Setup.SignalBar;
        public BarPoint[] Points { get; init; } = Array.Empty<BarPoint>();
        public DiagonalWave5State State { get; set; }
        public Dictionary<int, DiagonalWave5State> Wave5 { get; init; } = new();
        public int LastBar { get; init; }
        public int BrokenBar { get; set; } = -1;
        public bool Alive { get; set; } = true;
        public bool Frozen { get; set; }
        public int RsiP3 { get; init; }
        public int RsiP5 { get; init; }
        public bool HasDivergence { get; init; }
    }
}

/// <summary>Parameters for a diagonal → reversal-impulse scan.</summary>
public sealed record DiagonalImpulseScanRequest(
    string? File,
    string? FromDate = null,
    string? ToDate = null,
    int Period = 0,
    double MinSizePercent = 0.1,
    int BarsCount = 10,
    double MinWave2Retrace = 0.5,
    double MaxWave4RetraceW3 = 0.382,
    DiagonalInitialMode InitialMode = DiagonalInitialMode.LEADING,
    double EnterRatio = 0.5,
    double MinARatio = 0.236,
    int MinABars = 10,
    int MaxABars = 0,
    bool RequireSmoothA = false,
    double AStartToleranceW3 = 0.10,
    int AStartBarTolerance = 2,
    double StopBuffer = 0,
    double Target1Ratio = 1.0,
    bool Ladder = true,
    double Target2Ratio = 1.272,
    double CostRate = 0.0002,
    DiagonalAbcExecution Execution = DiagonalAbcExecution.TRIGGER,
    int MaxHoldBars = 500,
    int MaxWaitBars = 300,
    bool RequireAInsideP0 = true,
    int RsiPeriod = 14,
    RsiDivergenceFilter RsiDivergence = RsiDivergenceFilter.OFF);

/// <summary>A single diagonal → impulse setup with its simulated outcome.</summary>
public sealed class DiagonalImpulseSetupDto
{
    public int Id { get; set; }

    /// <summary>Direction of the diagonal; the trade runs against it.</summary>
    public bool IsUpDiagonal { get; set; }

    public IReadOnlyList<TriangleWavePointDto> WavePoints { get; set; } =
        Array.Empty<TriangleWavePointDto>();

    public int SignalBar { get; set; }
    public string SignalTime { get; set; } = string.Empty;
    public double DiagonalHeight { get; set; }

    public int AStartBar { get; set; }
    public double AStartPrice { get; set; }
    public int AEndBar { get; set; }
    public double AEndPrice { get; set; }
    public double AHeight { get; set; }

    /// <summary>End of the correction B (the C extreme); may be above/below the entry.</summary>
    public int BEndBar { get; set; }
    public double BEndPrice { get; set; }
    public double BDepth { get; set; }

    /// <summary>The second impulse C broke the end of A: the setup worked.</summary>
    public bool SetupWorked { get; set; }
    public int WorkedBar { get; set; } = -1;
    public double WorkedPrice { get; set; }

    /// <summary>The bar where the live diagonal was invalidated (wave 5 reached |W3|).</summary>
    public int DiagonalBrokenBar { get; set; } = -1;

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

    public string Outcome { get; set; } = "NOFILL";
    public int OutcomeBar { get; set; } = -1;
    public double OutcomePrice { get; set; }
    public double NetR { get; set; }
    public double StressNetR { get; set; }
    public double MfeR { get; set; }
    public double MaeR { get; set; }
    public int BarsHeld { get; set; }
    public bool Target1Hit { get; set; }
    public bool Target2Hit { get; set; }
    public string NoFillReason { get; set; } = string.Empty;

    public int RsiP3 { get; set; }
    public int RsiP5 { get; set; }
    public bool HasDivergence { get; set; }
}

/// <summary>Full result of a diagonal → reversal-impulse scan.</summary>
public sealed class DiagonalImpulseScanResult
{
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public int PriceDecimals { get; set; } = 5;
    public int UsedPeriod { get; set; }
    public int ImpulsePeriod { get; set; }
    public double MedianBarBps { get; set; }
    public int StartBar { get; set; }
    public int EndBar { get; set; }
    public IReadOnlyList<CandleBar> Candles { get; set; } = Array.Empty<CandleBar>();
    public IReadOnlyList<DiagonalImpulseSetupDto> Setups { get; set; } =
        Array.Empty<DiagonalImpulseSetupDto>();

    public int SignalCount { get; set; }
    public int FilledCount { get; set; }
    public int ResolvedCount { get; set; }
    public int SkippedCount { get; set; }
    public int BrokenCount { get; set; }
    public int ImpulseBrokenCount { get; set; }
    public int DivergenceCount { get; set; }
    public int WinCount { get; set; }
    public double AvgNetR { get; set; }
    public double AvgMfeR { get; set; }
    public double AvgMaeR { get; set; }
}
