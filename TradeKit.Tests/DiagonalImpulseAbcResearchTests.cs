using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Phase 1 of the "diagonal → impulse" research (DIAGONAL.md §9.41). The diagonal is only
    /// CONTEXT: after it, the first impulse A against the diagonal is found, its pullback B is
    /// waited for, and the second impulse C is traded. The stop is behind the start of A (the
    /// diagonal's actual extreme), the target is the end of A (conservative). This mirrors
    /// <c>ImpulseSetupFinder</c> with <c>TakeRatio = 1.0</c>, but the A-B-C leg is simulated
    /// directly on the zigzag so the sample is not limited by the finder's single-setup slot.
    /// <para>
    /// The harness measures the base edge (A size >= 23.6% of the diagonal, A >= 3 bars,
    /// EnterRatio 0.35 and 0.50), the statistical <c>MovementStatistic</c> features of A, and
    /// the V1 Elliott markup label of A. h1, 8 tuning + 8 fresh pairs, exact corridor.
    /// Writes <c>reports/diagonal_impulse_abc.md</c> and
    /// <c>reports/diagonal_impulse_abc_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalImpulseAbc"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string IMPULSE_REPORT = "diagonal_impulse_abc.md";
        private const string IMPULSE_CSV = "diagonal_impulse_abc_samples.csv";

        private const double A_MIN_DIAGONAL_RATIO = 0.236d;
        private const int A_MIN_BARS = 3;
        private const int MAX_WAIT_BARS = 100;
        private const int MAX_HOLD_BARS = 200;
        private const double A_START_TOLERANCE_W3 = 0.10d;
        private const int A_START_BAR_TOLERANCE = 2;

        private static readonly double[] ABC_ENTER_RATIOS = { 0.35d, 0.50d };

        private static readonly string[] ABC_EXECUTIONS = { "trigger", "close", "nextopen" };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_impulse_abc.md")]
        [Category("Research")]
        public void DiagonalImpulseAbc_Report()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            string? onlyFile = Environment.GetEnvironmentVariable("IMPULSE_ABC_FILE");
            if (!string.IsNullOrEmpty(onlyFile))
            {
                tuningFiles = tuningFiles.Where(f => f == onlyFile).ToList();
                freshFiles = freshFiles.Where(f => f == onlyFile).ToList();
                Assert.That(tuningFiles.Count + freshFiles.Count, Is.GreaterThan(0),
                    $"'{onlyFile}' is not in the h1 tuning/fresh sets.");
            }
            else
            {
                Assert.That(tuningFiles, Is.Not.Empty, "No tuning h1 archive file was found.");
                Assert.That(freshFiles, Is.Not.Empty, "No fresh h1 archive file was found.");
            }

            var runs = new List<AbcRun>();
            foreach (DiagonalInitialMode mode in new[]
                     {
                         DiagonalInitialMode.ALL, DiagonalInitialMode.ENDING
                     })
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                List<AbcAnchorFile> tuningAnchors = LoadAbcAnchors(dataDir, mode, tuningFiles);
                List<AbcAnchorFile> freshAnchors = LoadAbcAnchors(dataDir, mode, freshFiles);

                foreach (string execution in ABC_EXECUTIONS)
                {
                    foreach (double enterRatio in ABC_ENTER_RATIOS)
                    {
                        var run = new AbcRun(mode, enterRatio, execution);
                        SimulateAbcSet(tuningAnchors, enterRatio, execution, run.Tuning);
                        SimulateAbcSet(freshAnchors, enterRatio, execution, run.Fresh);
                        runs.Add(run);
                    }
                }

                TestContext.Out.WriteLine(
                    $"{mode}: anchors tuning={tuningAnchors.Sum(a => a.Anchors.Count)}, " +
                    $"fresh={freshAnchors.Sum(a => a.Anchors.Count)}, " +
                    $"{watch.Elapsed.TotalSeconds:F0}s");

                foreach (AbcAnchorFile file in tuningAnchors.Concat(freshAnchors))
                    file.Provider = null!;
            }

            Assert.That(runs.Sum(r => r.Tuning.Trades.Count + r.Fresh.Trades.Count),
                Is.GreaterThan(0), "The A-B-C simulation produced no trades.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, IMPULSE_REPORT);
            File.WriteAllText(reportPath, BuildAbcReport(
                runs, tuningFiles.Count, freshFiles.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, IMPULSE_CSV);
            File.WriteAllText(csvPath, BuildAbcCsv(runs));
            TestContext.Out.WriteLine($"Wrote {csvPath}");
        }

        #region Replay

        /// <summary>
        /// Runs the diagonal finder once per file and keeps the anchors plus the zigzag needed
        /// for the A-B-C simulation. The same anchors serve both enter ratios.
        /// </summary>
        private static List<AbcAnchorFile> LoadAbcAnchors(
            string dataDir,
            DiagonalInitialMode mode,
            IReadOnlyList<string> files)
        {
            var result = new List<AbcAnchorFile>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 12 },
                file =>
                {
                    // The harness is shared by h1 and m15 sets; the file name is the only
                    // place where the timeframe is recorded.
                    ITimeFrame timeFrame = file.Contains("_m15_")
                        ? TimeFrameHelper.Minute15
                        : TimeFrameHelper.Hour1;
                    var provider = new TestBarsProvider(timeFrame);
                    provider.LoadCandles(Path.Combine(dataDir, file));

                    DiagonalRunConfig config = EXACT_CORRIDOR with
                    {
                        Name = mode.ToString(),
                        InitialMode = mode
                    };
                    List<Sample> anchors = RunDiagonalSamples(provider, file, config);
                    int period = AutoPeriodEstimator.EstimateImpulsePeriod(provider);

                    var anchorFile = new AbcAnchorFile
                    {
                        File = file,
                        Provider = provider,
                        Anchors = anchors,
                        Timeline = ZigzagTimeline.Build(provider, period),
                        FinePivots = BuildFinePivots(provider, period)
                    };

                    lock (sync)
                    {
                        result.Add(anchorFile);
                    }
                });

            return result;
        }

        private static List<BarPoint> BuildFinePivots(TestBarsProvider provider, int period)
        {
            // The A leg is short (3–20 bars); the diagonal's own ladder is too coarse to
            // resolve its internal structure. A quarter of the base period gives 4–10 pivots
            // on the same range without turning the zigzag into noise.
            int finePeriod = Math.Max(3, period / 4);
            var finder = new DeviationExtremumFinder(finePeriod, provider);
            for (int i = 0; i < provider.Count; i++)
                finder.OnCalculate(provider.GetOpenTime(i));

            return finder.ToChronologicalExtremaList();
        }

        private static void SimulateAbcSet(
            IReadOnlyList<AbcAnchorFile> files,
            double enterRatio,
            string execution,
            AbcSet set)
        {
            foreach (AbcAnchorFile file in files)
            {
                set.Anchors += file.Anchors.Count;
                var seen = new HashSet<(int, int, bool)>();
                foreach (Sample anchor in file.Anchors)
                {
                    AbcTrade? trade = SimulateAbc(
                        anchor, file.Provider, file.Timeline, file.FinePivots, enterRatio,
                        execution, file.V1Cache, out bool aLegFound);
                    if (aLegFound)
                        set.ALegs++;
                    if (trade == null)
                        continue;

                    if (!seen.Add((trade.AStartBar, trade.AEndBar, trade.IsUpDiagonal)))
                        continue;

                    trade.File = file.File;
                    set.Trades.Add(trade);
                }
            }
        }

        /// <summary>
        /// The A-B-C simulation for one diagonal anchor. The running extreme after the anchor
        /// is the diagonal's actual end; A is the first zigzag leg against the diagonal from
        /// that extreme. The pullback is tracked on raw bars exactly like
        /// <c>ImpulseSetupFinder</c>: a limit at <c>EnterRatio</c> of A, invalidation beyond
        /// A's start, stop at A's start, target at A's end.
        /// </summary>
        private static AbcTrade? SimulateAbc(
            Sample anchor,
            IBarsProvider provider,
            ZigzagTimeline timeline,
            IReadOnlyList<BarPoint> finePivots,
            double enterRatio,
            string execution,
            IDictionary<(int, int), bool> v1Cache,
            out bool aLegFound)
        {
            aLegFound = false;
            if (anchor.W3 <= 1e-12)
                return null;

            bool isUp = anchor.IsUpDiagonal;
            int sgn = isUp ? 1 : -1;
            int fromBar = Math.Max(anchor.SignalBar, anchor.P5Bar) + 1;
            int contextBars = Math.Clamp(3 * (anchor.P4Bar - anchor.P0Bar), 50, 500);
            int lastBar = Math.Min(provider.Count - 1, fromBar + contextBars);
            double extreme = anchor.V5;
            int extremeBar = anchor.P5Bar;
            double startTolerance = A_START_TOLERANCE_W3 * anchor.W3;

            void UpdateExtreme(int index)
            {
                if (isUp)
                {
                    double high = provider.GetHighPrice(index);
                    if (high > extreme)
                    {
                        extreme = high;
                        extremeBar = index;
                    }
                }
                else
                {
                    double low = provider.GetLowPrice(index);
                    if (low < extreme)
                    {
                        extreme = low;
                        extremeBar = index;
                    }
                }
            }

            int bar = fromBar;
            while (bar <= lastBar)
            {
                UpdateExtreme(bar);

                if (timeline.Count[bar] < 2)
                {
                    bar++;
                    continue;
                }

                BarPoint prev = timeline.Prev[bar];
                BarPoint last = timeline.Last[bar];
                if (prev == null || last == null ||
                    sgn * (prev.Value - last.Value) <= 0 ||
                    Math.Abs(prev.Value - extreme) > startTolerance ||
                    Math.Abs(prev.BarIndex - extremeBar) > A_START_BAR_TOLERANCE)
                {
                    bar++;
                    continue;
                }

                double aStart = extreme;
                int aStartBar = extremeBar;
                double aEnd = last.Value;
                int aEndBar = last.BarIndex;

                if (Math.Abs(aStart - aEnd) < A_MIN_DIAGONAL_RATIO * Math.Abs(extreme - anchor.V0) ||
                    aEndBar - aStartBar < A_MIN_BARS)
                {
                    bar++;
                    continue;
                }

                aLegFound = true;

                // A is found. Wait for the B pullback; the A end may still move while the
                // counter-leg extends, exactly like the floating last pivot of the finder.
                int waitLimit = Math.Min(lastBar, aEndBar + MAX_WAIT_BARS);
                int waitBar = bar;
                bool resumed = false;

                while (waitBar <= waitLimit)
                {
                    UpdateExtreme(waitBar);

                    if (timeline.Count[waitBar] >= 2 &&
                        timeline.Prev[waitBar] != null &&
                        timeline.Last[waitBar] != null &&
                        Math.Abs(timeline.Prev[waitBar].Value - aStart) <= startTolerance &&
                        Math.Abs(timeline.Prev[waitBar].BarIndex - aStartBar) <=
                            A_START_BAR_TOLERANCE)
                    {
                        double moved = timeline.Last[waitBar].Value;
                        if (sgn * (aEnd - moved) > 0)
                        {
                            aEnd = moved;
                            aEndBar = timeline.Last[waitBar].BarIndex;
                        }
                    }

                    double barHigh = provider.GetHighPrice(waitBar);
                    double barLow = provider.GetLowPrice(waitBar);

                    // A pullback beyond A's start means the diagonal resumed; the next A
                    // candidate starts from the new extreme.
                    if (isUp ? barHigh >= aStart : barLow <= aStart)
                    {
                        resumed = true;
                        break;
                    }

                    double trigger = aEnd + sgn * enterRatio * Math.Abs(aStart - aEnd);
                    bool filled = isUp
                        ? barHigh >= trigger
                        : barLow <= trigger;
                    if (!filled)
                    {
                        waitBar++;
                        continue;
                    }

                    double entry;
                    int entryBar = waitBar;
                    if (execution == "nextopen")
                    {
                        entryBar = waitBar + 1;
                        if (entryBar >= provider.Count)
                        {
                            waitBar++;
                            continue;
                        }

                        entry = provider.GetOpenPrice(entryBar);
                    }
                    else if (execution == "close")
                    {
                        entry = provider.GetClosePrice(waitBar);
                    }
                    else
                    {
                        entry = isUp
                            ? Math.Max(trigger, provider.GetOpenPrice(waitBar))
                            : Math.Min(trigger, provider.GetOpenPrice(waitBar));
                    }

                    // The entry must stay between the target and the start of A. A gap
                    // through either level invalidates the trade instead of booking a
                    // synthetic fill.
                    if (isUp
                            ? entry >= aStart || entry <= aEnd
                            : entry <= aStart || entry >= aEnd)
                    {
                        waitBar++;
                        continue;
                    }

                    double allowance =
                        Math.Abs(entry - aStart) * Helper.PERCENT_ALLOWANCE_SL / 100;
                    double sl = aStart + sgn * allowance;
                    double tp = aEnd;
                    double risk = Math.Abs(entry - sl);
                    if (risk <= 1e-12)
                    {
                        waitBar++;
                        continue;
                    }

                    AbcOutcome outcome = WalkAbcOutcome(
                        provider, entryBar, isUp, entry, sl, tp, risk);

                    var stats = MovementStatistic.GetMovementStatistic(
                        new BarPoint(aStart, aStartBar, provider),
                        new BarPoint(aEnd, aEndBar, provider),
                        provider);

                    bool v1 = ValidateElliottV1(
                        provider, finePivots, aStart, aStartBar, aEnd, aEndBar, v1Cache);

                    return new AbcTrade
                    {
                        IsUpDiagonal = isUp,
                        Symbol = anchor.Symbol,
                        SignalTime = anchor.EntryTime,
                        SignalBar = anchor.SignalBar,
                        DiagonalHeight = Math.Abs(extreme - anchor.V0),
                        AStartBar = aStartBar,
                        AEndBar = aEndBar,
                        AStart = aStart,
                        AEnd = aEnd,
                        BarsA = aEndBar - aStartBar,
                        AToDiagonal = Math.Abs(aStart - aEnd) /
                                      Math.Max(1e-12, Math.Abs(extreme - anchor.V0)),
                        EntryBar = entryBar,
                        EntryTime = provider.GetOpenTime(entryBar),
                        Entry = entry,
                        StopLoss = sl,
                        TakeProfit = tp,
                        Risk = risk,
                        EnterRatio = enterRatio,
                        Execution = execution,
                        EntryDelayBars = entryBar - aEndBar,
                        BRatio = Math.Abs(entry - aEnd) / Math.Max(1e-12, Math.Abs(aStart - aEnd)),
                        Outcome = outcome.Outcome,
                        ExitPrice = outcome.ExitPrice,
                        OutcomeBar = outcome.OutcomeBar,
                        Resolved = outcome.Resolved,
                        GrossR = outcome.GrossR,
                        CostR = outcome.CostR,
                        NetR = outcome.NetR,
                        Profitable = outcome.Resolved && outcome.NetR > 0,
                        MfeR = outcome.MfeR,
                        MaeR = outcome.MaeR,
                        OverlapDepth = stats.OverlapseMaxDepth,
                        OverlapDegree = stats.OverlapDegree,
                        RatioZigzag = stats.RatioZigzag,
                        Heterogeneity = stats.HeterogeneityMax,
                        Area = stats.Area,
                        MaxDistance = stats.MaxDistance,
                        Size = stats.Size,
                        Candles = stats.CandlesCount,
                        ElliottV1 = v1
                    };
                }

                bar = resumed ? waitBar + 1 : Math.Max(waitBar, bar + 1);
            }

            return null;
        }

        private static AbcOutcome WalkAbcOutcome(
            IBarsProvider provider,
            int entryBar,
            bool isUpDiagonal,
            double entry,
            double sl,
            double tp,
            double risk)
        {
            int dir = isUpDiagonal ? -1 : 1;
            double mfeR = 0;
            double maeR = 0;
            int end = Math.Min(provider.Count - 1, entryBar + MAX_HOLD_BARS);

            for (int bar = entryBar + 1; bar <= end; bar++)
            {
                double high = provider.GetHighPrice(bar);
                double low = provider.GetLowPrice(bar);

                double favorable = isUpDiagonal ? entry - low : high - entry;
                double adverse = isUpDiagonal ? high - entry : entry - low;
                mfeR = Math.Max(mfeR, favorable / risk);
                maeR = Math.Max(maeR, adverse / risk);

                bool stopHit = isUpDiagonal ? high >= sl : low <= sl;
                bool targetHit = isUpDiagonal ? low <= tp : high >= tp;

                if (stopHit)
                {
                    double exit = sl;
                    double grossR = dir * (exit - entry) / risk;
                    double costR = entry * COST_RATE / risk;
                    return new AbcOutcome(
                        "SL", exit, bar, true, grossR, costR, grossR - costR, mfeR, maeR);
                }

                if (targetHit)
                {
                    double exit = tp;
                    double grossR = dir * (exit - entry) / risk;
                    double costR = entry * COST_RATE / risk;
                    return new AbcOutcome(
                        "TP", exit, bar, true, grossR, costR, grossR - costR, mfeR, maeR);
                }
            }

            return new AbcOutcome("NONE", 0, -1, false, 0, 0, 0, mfeR, maeR);
        }

        /// <summary>
        /// V1 Elliott label of the A leg: the pivot range of A is parsed and accepted when a
        /// complete IMPULSE (or leading contracting diagonal) covers the whole range. The
        /// parse uses the pivots only (no bar repair), so it is a structural label, not a
        /// price-level gate.
        /// </summary>
        private static bool ValidateElliottV1(
            IBarsProvider provider,
            IReadOnlyList<BarPoint> finePivots,
            double aStart,
            int aStartBar,
            double aEnd,
            int aEndBar,
            IDictionary<(int, int), bool> cache)
        {
            if (cache.TryGetValue((aStartBar, aEndBar), out bool cached))
                return cached;

            bool result = false;
            try
            {
                var points = new List<BarPoint>
                {
                    new(aStart, aStartBar, provider)
                };
                List<BarPoint> inner = finePivots
                    .Where(p => p.BarIndex > aStartBar && p.BarIndex < aEndBar)
                    .ToList();

                // A five-wave structure rarely needs more than a dozen internal pivots;
                // longer lists make the V1 parser combinatorial without adding signal.
                if (inner.Count <= 12)
                {
                    points.AddRange(inner);
                    points.Add(new BarPoint(aEnd, aEndBar, provider));

                    if (points.Count >= 4)
                    {
                        var markup = new ElliottWaveExactMarkup();
                        List<ExactParsedNode> nodes = markup.Parse(points);

                        // The A leg is a complete Elliott structure of the correction's first
                        // leg: an impulse / leading diagonal, or a zigzag (A itself is often
                        // corrective).
                        result = nodes.Any(n =>
                            n.StartPoint != null &&
                            n.EndPoint != null &&
                            n.StartPoint.BarIndex == aStartBar &&
                            n.EndPoint.BarIndex == aEndBar &&
                            n.ModelType is ElliottModelType.IMPULSE
                                or ElliottModelType.DIAGONAL_CONTRACTING_INITIAL
                                or ElliottModelType.ZIGZAG
                                or ElliottModelType.DOUBLE_ZIGZAG);
                    }
                }
            }
            catch
            {
                result = false;
            }

            cache[(aStartBar, aEndBar)] = result;
            return result;
        }

        #endregion

        #region Report

        private static string BuildAbcReport(
            IReadOnlyList<AbcRun> runs,
            int tuningFileCount,
            int freshFileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal → impulse: A-B-C after the diagonal");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(
                $"Files: {tuningFileCount} tuning + {freshFileCount} fresh h1 pairs. Exact corridor " +
                "(`W2/W1 >= 0.5`, `W4/W3 <= 0.382`), 2 bp costs, stop-first on a tie. The diagonal " +
                "is only context: A is the first zigzag leg against it from its actual extreme, " +
                "B is the pullback, C is the traded second impulse. Stop = start of A, target = " +
                "end of A (TakeRatio 1.0), both modes and both enter ratios.");
            builder.AppendLine();
            builder.AppendLine(
                "A is accepted when it starts at the diagonal's running extreme (within " +
                $"`{A_START_TOLERANCE_W3:P0}` of `|W3|` and `{A_START_BAR_TOLERANCE}` bars), is at " +
                $"least `{A_MIN_DIAGONAL_RATIO:P1}` of the diagonal and lasts at least " +
                $"`{A_MIN_BARS}` bars. The pullback is tracked on raw bars; the limit is " +
                "`EnterRatio` of A, invalidated beyond A's start.");
            builder.AppendLine();

            AppendAbcMainTable(builder, runs);
            AppendAbcValidatorTable(builder, runs);
            AppendAbcPairTable(builder, runs);
            AppendAbcFeatureTable(builder, runs);
            AppendAbcVerdict(builder, runs);

            return builder.ToString();
        }

        private static void AppendAbcMainTable(
            StringBuilder builder,
            IReadOnlyList<AbcRun> runs)
        {
            builder.AppendLine("## Main results");
            builder.AppendLine();
            builder.AppendLine("| Mode | Exec | Enter | Set | Diagonals | A legs | Entries | Fill | Resolved | profitable | avg netR | avg R:R | avg MFE/R | avg MAE/R |");
            builder.AppendLine("|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (AbcRun run in runs)
            {
                foreach ((string set, AbcSet data) in new[]
                         {
                             ("tuning", run.Tuning), ("fresh", run.Fresh)
                         })
                {
                    AbcStats stats = Stats(data.Trades);
                    int anchors = data.Anchors;
                    double fill = anchors == 0 ? 0 : (double)data.Trades.Count / anchors;
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Mode} | {run.Execution} | {run.EnterRatio:F2} | {set} | {anchors} | {data.ALegs} | {data.Trades.Count} | {fill:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {AverageAbcRr(data.Trades):F2} | {AverageAbc(data.Trades, t => t.MfeR):F2} | {AverageAbc(data.Trades, t => t.MaeR):F2} |"));
                }
            }
        }

        private static void AppendAbcValidatorTable(
            StringBuilder builder,
            IReadOnlyList<AbcRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## A validator: V1 Elliott markup");
            builder.AppendLine();
            builder.AppendLine("Trades split by the V1 label of the A leg (a complete impulse, " +
                               "leading contracting diagonal, zigzag or double zigzag over the A " +
                               "pivot range at the fine scale).");
            builder.AppendLine();
            builder.AppendLine("| Mode | Exec | Enter | Set | V1 | N | Resolved | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---|---|---:|---:|---:|---:|");

            foreach (AbcRun run in runs)
            {
                foreach ((string set, AbcSet data) in new[]
                         {
                             ("tuning", run.Tuning), ("fresh", run.Fresh)
                         })
                {
                    foreach (bool v1 in new[] { true, false })
                    {
                        List<AbcTrade> slice = data.Trades
                            .Where(t => t.ElliottV1 == v1)
                            .ToList();
                        if (slice.Count == 0)
                            continue;

                        AbcStats stats = Stats(slice);
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {run.Mode} | {run.Execution} | {run.EnterRatio:F2} | {set} | {(v1 ? "yes" : "no")} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                    }
                }
            }
        }

        private static void AppendAbcPairTable(
            StringBuilder builder,
            IReadOnlyList<AbcRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Fresh by pair");
            builder.AppendLine();
            builder.AppendLine("Only the `ALL` mode, `nextopen` execution, both enter ratios, fresh " +
                               "test entries. This is the honest execution table: no same-bar fill.");
            builder.AppendLine();
            builder.AppendLine("| Pair | Exec | Enter | N | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");

            foreach (AbcRun run in runs.Where(r =>
                         r.Mode == DiagonalInitialMode.ALL &&
                         r.Execution == "nextopen"))
            {
                List<AbcTrade> trades = run.Fresh.Trades
                    .Where(t => t.EntryTime.Year >= 2023)
                    .ToList();
                foreach (IGrouping<string, AbcTrade> pair in trades
                             .GroupBy(t => t.Symbol)
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    AbcStats stats = Stats(pair.ToList());
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {pair.Key} | {run.Execution} | {run.EnterRatio:F2} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                }
            }
        }

        private static void AppendAbcFeatureTable(
            StringBuilder builder,
            IReadOnlyList<AbcRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Feature audit of A");
            builder.AppendLine();
            builder.AppendLine("AUC of each `MovementStatistic` feature (and the A geometry) for " +
                               "`netR > 0` among resolved trades. 0.5 = no separation. Only the " +
                               "`ALL / 0.35` run is shown; the other slices are in the CSV.");
            builder.AppendLine();

            AbcRun run = runs.First(r => r.Mode == DiagonalInitialMode.ALL &&
                                         r.Execution == "nextopen" &&
                                         Math.Abs(r.EnterRatio - 0.35d) < 1e-9);
            var features = new (string Name, Func<AbcTrade, double> Value)[]
            {
                ("A / diagonal", t => t.AToDiagonal),
                ("bars A", t => t.BarsA),
                ("overlap depth", t => t.OverlapDepth),
                ("overlap degree", t => t.OverlapDegree),
                ("ratio zigzag", t => t.RatioZigzag),
                ("heterogeneity", t => t.Heterogeneity),
                ("area", t => t.Area),
                ("max distance", t => t.MaxDistance),
                ("size", t => t.Size),
                ("candles", t => t.Candles),
                ("entry delay", t => t.EntryDelayBars),
                ("B ratio", t => t.BRatio)
            };

            builder.AppendLine("| Feature | Tuning AUC | Fresh AUC | Tuning q1 netR | Tuning q5 netR | Fresh q1 netR | Fresh q5 netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");

            List<AbcTrade> tuning = run.Tuning.Trades;
            List<AbcTrade> fresh = run.Fresh.Trades;

            foreach ((string name, Func<AbcTrade, double> value) in features)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {name} | {AucOf(tuning, value):F3} | {AucOf(fresh, value):F3} | {QuantileNetR(tuning, value, 0.2):F3} | {QuantileNetR(tuning, value, 0.8):F3} | {QuantileNetR(fresh, value, 0.2):F3} | {QuantileNetR(fresh, value, 0.8):F3} |"));
            }
        }

        private static void AppendAbcVerdict(
            StringBuilder builder,
            IReadOnlyList<AbcRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Verdict");
            builder.AppendLine();

            foreach (AbcRun run in runs)
            {
                AbcStats tuning = Stats(run.Tuning.Trades);
                AbcStats fresh = Stats(run.Fresh.Trades);
                AbcStats tuningTest = Stats(run.Tuning.Trades
                    .Where(t => t.EntryTime.Year >= 2023).ToList());
                AbcStats freshTest = Stats(run.Fresh.Trades
                    .Where(t => t.EntryTime.Year >= 2023).ToList());

                builder.AppendLine(FormattableString.Invariant(
                    $"- **{run.Mode} / {run.Execution} / {run.EnterRatio:F2}.** Tuning: {tuning.N} trades, {tuning.Profitable:P1} profitable, {tuning.AvgNetR:F3}R. Fresh: {fresh.N}, {fresh.Profitable:P1}, {fresh.AvgNetR:F3}R. Fresh test (2023+): {freshTest.N}, {freshTest.Profitable:P1}, {freshTest.AvgNetR:F3}R. Tuning test: {tuningTest.N}, {tuningTest.AvgNetR:F3}R."));
            }

            builder.AppendLine();
            builder.AppendLine("**How to read this.** This is a structural A-B-C simulation, not a " +
                               "post-hoc level grid: A, B and the entry are causal, the stop is " +
                               "the start of A and the target is the end of A. A positive cell " +
                               "must hold on both tuning and fresh. The feature AUC is a " +
                               "separator audit, not a filter: no threshold is applied in this " +
                               "run.");
        }

        #endregion

        #region Helpers

        private static double AverageAbc(
            IReadOnlyList<AbcTrade> trades,
            Func<AbcTrade, double> value)
        {
            return trades.Count == 0 ? 0 : trades.Average(value);
        }

        private static double AverageAbcRr(IReadOnlyList<AbcTrade> trades)
        {
            List<AbcTrade> resolved = trades.Where(t => t.Resolved).ToList();
            return resolved.Count == 0
                ? 0
                : resolved.Average(t =>
                    Math.Abs(t.TakeProfit - t.Entry) / Math.Max(1e-12, t.Risk));
        }

        /// <summary>AUC of a feature for the binary target `netR > 0` among resolved trades.</summary>
        private static double AucOf(
            IReadOnlyList<AbcTrade> trades,
            Func<AbcTrade, double> value)
        {
            List<AbcTrade> resolved = trades
                .Where(t => t.Resolved && IsFinite(value(t)))
                .ToList();
            int positives = resolved.Count(t => t.NetR > 0);
            int negatives = resolved.Count - positives;
            if (positives == 0 || negatives == 0)
                return 0.5;

            double sum = 0;
            foreach (AbcTrade positive in resolved.Where(t => t.NetR > 0))
            {
                foreach (AbcTrade negative in resolved.Where(t => t.NetR <= 0))
                {
                    double p = value(positive);
                    double n = value(negative);
                    sum += p > n ? 1 : p < n ? 0 : 0.5;
                }
            }

            return sum / (positives * (double)negatives);
        }

        /// <summary>Mean netR of the quantile bucket containing <paramref name="quantile"/>.</summary>
        private static double QuantileNetR(
            IReadOnlyList<AbcTrade> trades,
            Func<AbcTrade, double> value,
            double quantile)
        {
            List<AbcTrade> resolved = trades
                .Where(t => t.Resolved && IsFinite(value(t)))
                .ToList();
            if (resolved.Count < 5)
                return 0;

            List<AbcTrade> ordered = resolved.OrderBy(value).ToList();
            int from = (int)(quantile * ordered.Count);
            int to = Math.Min(ordered.Count, from + Math.Max(1, ordered.Count / 5));
            from = Math.Min(from, ordered.Count - 1);
            List<AbcTrade> slice = ordered.Skip(from).Take(to - from).ToList();
            return slice.Count == 0 ? 0 : slice.Average(t => t.NetR);
        }

        private static AbcStats Stats(IReadOnlyList<AbcTrade> trades)
        {
            List<AbcTrade> resolved = trades.Where(t => t.Resolved).ToList();
            return new AbcStats(
                trades.Count,
                resolved.Count,
                resolved.Count == 0
                    ? 0
                    : resolved.Count(t => t.Profitable) / (double)resolved.Count,
                resolved.Count == 0 ? 0 : resolved.Average(t => t.NetR));
        }

        private static string BuildAbcCsv(IReadOnlyList<AbcRun> runs)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "mode,execution,enter,set,file,symbol,signal_time,signal_bar,is_up,diagonal_height," +
                "a_start_bar,a_end_bar,a_start,a_end,bars_a,a_to_diagonal,entry_time,entry_bar," +
                "entry,stop,tp,risk,enter_ratio,entry_delay,b_ratio,outcome,exit_price,outcome_bar," +
                "net_r,resolved,profitable,mfe_r,mae_r,overlap_depth,overlap_degree,ratio_zigzag," +
                "heterogeneity,area,max_distance,size,candles,elliott_v1");

            foreach (AbcRun run in runs)
            {
                foreach ((string set, AbcSet data) in new[]
                         {
                             ("tuning", run.Tuning), ("fresh", run.Fresh)
                         })
                {
                    foreach (AbcTrade t in data.Trades
                                 .OrderBy(t => t.Symbol, StringComparer.Ordinal)
                                 .ThenBy(t => t.EntryTime))
                    {
                        builder.AppendLine(string.Join(",",
                            Csv(run.Mode.ToString()),
                            Csv(run.Execution),
                            F(run.EnterRatio),
                            Csv(set),
                            Csv(t.File),
                            Csv(t.Symbol),
                            Csv(t.SignalTime.ToString("o", CultureInfo.InvariantCulture)),
                            t.SignalBar.ToString(CultureInfo.InvariantCulture),
                            B(t.IsUpDiagonal),
                            F(t.DiagonalHeight),
                            t.AStartBar.ToString(CultureInfo.InvariantCulture),
                            t.AEndBar.ToString(CultureInfo.InvariantCulture),
                            F(t.AStart),
                            F(t.AEnd),
                            t.BarsA.ToString(CultureInfo.InvariantCulture),
                            F(t.AToDiagonal),
                            Csv(t.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
                            t.EntryBar.ToString(CultureInfo.InvariantCulture),
                            F(t.Entry),
                            F(t.StopLoss),
                            F(t.TakeProfit),
                            F(t.Risk),
                            F(t.EnterRatio),
                            t.EntryDelayBars.ToString(CultureInfo.InvariantCulture),
                            F(t.BRatio),
                            Csv(t.Outcome),
                            F(t.ExitPrice),
                            t.OutcomeBar.ToString(CultureInfo.InvariantCulture),
                            F(t.NetR),
                            B(t.Resolved),
                            B(t.Profitable),
                            F(t.MfeR),
                            F(t.MaeR),
                            F(t.OverlapDepth),
                            F(t.OverlapDegree),
                            F(t.RatioZigzag),
                            F(t.Heterogeneity),
                            F(t.Area),
                            F(t.MaxDistance),
                            F(t.Size),
                            t.Candles.ToString(CultureInfo.InvariantCulture),
                            B(t.ElliottV1)));
                    }
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Types

        private sealed class ZigzagTimeline
        {
            public int[] Count { get; private init; } = Array.Empty<int>();
            public BarPoint[] Prev { get; private init; } = Array.Empty<BarPoint>();
            public BarPoint[] Last { get; private init; } = Array.Empty<BarPoint>();

            public static ZigzagTimeline Build(TestBarsProvider provider, int period)
            {
                int n = provider.Count;
                var timeline = new ZigzagTimeline
                {
                    Count = new int[n],
                    Prev = new BarPoint[n],
                    Last = new BarPoint[n]
                };

                var finder = new DeviationExtremumFinder(period, provider);
                for (int i = 0; i < n; i++)
                {
                    finder.OnCalculate(provider.GetOpenTime(i));
                    int count = finder.Extrema.Count;
                    timeline.Count[i] = count;
                    if (count >= 1)
                        timeline.Last[i] = finder.Extrema.Values[count - 1];
                    if (count >= 2)
                        timeline.Prev[i] = finder.Extrema.Values[count - 2];
                }

                return timeline;
            }
        }

        private sealed class AbcAnchorFile
        {
            public string File { get; init; } = string.Empty;
            public TestBarsProvider Provider { get; set; } = null!;
            public List<Sample> Anchors { get; init; } = new();
            public ZigzagTimeline Timeline { get; init; } = null!;
            public List<BarPoint> FinePivots { get; init; } = new();
            public Dictionary<(int, int), bool> V1Cache { get; } = new();
        }

        private sealed class AbcSet
        {
            public List<AbcTrade> Trades { get; } = new();
            public int Anchors { get; set; }
            public int ALegs { get; set; }
        }

        private sealed class AbcRun
        {
            public AbcRun(DiagonalInitialMode mode, double enterRatio, string execution)
            {
                Mode = mode;
                EnterRatio = enterRatio;
                Execution = execution;
            }

            public DiagonalInitialMode Mode { get; }
            public double EnterRatio { get; }
            public string Execution { get; }
            public AbcSet Tuning { get; } = new();
            public AbcSet Fresh { get; } = new();
        }

        private sealed class AbcTrade
        {
            public string File { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public string Set { get; set; } = string.Empty;
            public bool IsUpDiagonal { get; set; }
            public DateTime SignalTime { get; set; }
            public int SignalBar { get; set; }
            public double DiagonalHeight { get; set; }
            public int AStartBar { get; set; }
            public int AEndBar { get; set; }
            public double AStart { get; set; }
            public double AEnd { get; set; }
            public int BarsA { get; set; }
            public double AToDiagonal { get; set; }
            public int EntryBar { get; set; }
            public DateTime EntryTime { get; set; }
            public double Entry { get; set; }
            public double StopLoss { get; set; }
            public double TakeProfit { get; set; }
            public double Risk { get; set; }
            public double EnterRatio { get; set; }
            public string Execution { get; set; } = string.Empty;
            public int EntryDelayBars { get; set; }
            public double BRatio { get; set; }
            public string Outcome { get; set; } = "NONE";
            public double ExitPrice { get; set; }
            public int OutcomeBar { get; set; } = -1;
            public bool Resolved { get; set; }
            public bool Profitable { get; set; }
            public double GrossR { get; set; }
            public double CostR { get; set; }
            public double NetR { get; set; }
            public double MfeR { get; set; }
            public double MaeR { get; set; }
            public double OverlapDepth { get; set; }
            public double OverlapDegree { get; set; }
            public double RatioZigzag { get; set; }
            public double Heterogeneity { get; set; }
            public double Area { get; set; }
            public double MaxDistance { get; set; }
            public double Size { get; set; }
            public int Candles { get; set; }
            public bool ElliottV1 { get; set; }
        }

        private readonly record struct AbcOutcome(
            string Outcome,
            double ExitPrice,
            int OutcomeBar,
            bool Resolved,
            double GrossR,
            double CostR,
            double NetR,
            double MfeR,
            double MaeR);

        private sealed record AbcStats(
            int N,
            int Resolved,
            double Profitable,
            double AvgNetR);

        #endregion
    }
}
