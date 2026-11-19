using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for the "diagonal as the entry impulse" hypothesis: after a leading
    /// contracting diagonal the market corrects, and the continuation in the diagonal's
    /// direction is traded. The stop is the start of the diagonal (P0), the conservative
    /// target is the end of the diagonal, and the extension targets are measured from P0,
    /// from the end, or from the actual entry.
    /// <para>
    /// The primary anchor is V3 (dynamic re-price): the limit is re-priced every bar at
    /// <c>k</c> of the running diagonal height, so the deep correction itself is the entry
    /// trigger and no separate bar-based end confirmation is required. V2 (a 10% retrace
    /// confirmation before the limit is placed) and V1 (the signal's P5 as the end) are kept
    /// as robustness checks.
    /// </para>
    /// <para>
    /// Writes <c>reports/diagonal_continuation.md</c> and
    /// <c>reports/diagonal_continuation_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalContinuation"</c>. Scope and
    /// subset are controlled by <c>CONT_SCOPE</c> (primary/full), <c>CONT_SET</c> and
    /// <c>CONT_FILE</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string CONT_REPORT = "diagonal_continuation.md";
        private const string CONT_CSV = "diagonal_continuation_samples.csv";

        private const double CONT_COST_RATE = 0.0002d;
        private const double CONT_STRESS_COST_RATE = 0.0005d;

        private const int CONT_MAX_HOLD_H1 = 500;
        private const int CONT_MAX_HOLD_M15 = 1000;
        private const int CONT_MAX_TOTAL_WAIT_H1 = 300;
        private const int CONT_MAX_TOTAL_WAIT_M15 = 600;

        private const double CONT_CONFIRM_RATIO = 0.10d;
        private const double CONT_LADDER_TP2_RATIO = 1.618d;

        private static readonly string[] CONT_FRESH_H1_FILES =
        {
            "AUDCAD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "CADJPY_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "EURCHF_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "EURGBP_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "GBPCHF_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "NZDUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv",
            "USDCAD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv",
            "XAGUSD_h1_2017-12-27T18-00-00_2026-05-31T23-00-00.csv"
        };

        private static readonly string[] CONT_HOLDOUT_H1_FILES =
        {
            "AUDCHF_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "CADCHF_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "CHFJPY_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "EURAUD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "EURCAD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "EURNZD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "GBPAUD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "GBPNZD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv"
        };

        private static readonly string[] CONT_FRESH_M15_FILES =
        {
            "AUDCAD_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "CADJPY_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "EURCHF_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "EURGBP_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "GBPCHF_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "NZDUSD_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv",
            "USDCAD_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv",
            "XAGUSD_m15_2017-12-27T18-00-00_2026-05-31T23-00-00.csv"
        };

        private static readonly double[] CONT_ENTER_RATIOS = { 0.236d, 0.382d, 0.5d, 0.618d, 0.786d };
        private static readonly double[] CONT_P0_TARGETS = { 1.0d, 1.272d, 1.618d, 2.0d, 2.618d };
        private static readonly double[] CONT_END_TARGETS = { 0.618d, 1.0d, 1.618d };
        private static readonly double[] CONT_ENTRY_TARGETS = { 1.0d, 1.618d };

        private static readonly DiagonalInitialMode[] CONT_MODES =
        {
            DiagonalInitialMode.LEADING,
            DiagonalInitialMode.ALL,
            DiagonalInitialMode.ENDING
        };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_continuation.md")]
        [Category("Research")]
        public void DiagonalContinuation_Report()
        {
            string dataDir = FindDataDir();
            string scope = (Environment.GetEnvironmentVariable("CONT_SCOPE") ?? "primary")
                .Trim().ToLowerInvariant();
            string? onlySet = Environment.GetEnvironmentVariable("CONT_SET");
            string? onlyFile = Environment.GetEnvironmentVariable("CONT_FILE");
            string? onlyCorridor = Environment.GetEnvironmentVariable("CONT_CORRIDOR");

            var sets = new List<(string Name, IReadOnlyList<string> Files)>();
            AddSet(sets, dataDir, "tuning", H1_FILES, onlySet);
            AddSet(sets, dataDir, "fresh", CONT_FRESH_H1_FILES, onlySet);
            AddSet(sets, dataDir, "holdout", CONT_HOLDOUT_H1_FILES, onlySet);
            if (scope == "full")
                AddSet(sets, dataDir, "m15-fresh", CONT_FRESH_M15_FILES, onlySet);

            if (onlyFile != null)
            {
                foreach ((string name, IReadOnlyList<string> files) in sets.ToList())
                {
                    var filtered = files.Where(f => f == onlyFile).ToList();
                    if (filtered.Count == 0)
                        sets.RemoveAll(s => s.Name == name);
                    else
                        sets[sets.FindIndex(s => s.Name == name)] = (name, filtered);
                }
            }

            Assert.That(sets, Is.Not.Empty, "No archive file was found under data/.");

            List<ContSpec> specs = BuildContinuationSpecs(scope);
            var results = new List<ContFileResult>();
            var sync = new object();

            Parallel.ForEach(
                sets.SelectMany(s => s.Files.Select(f => (Set: s.Name, File: f))),
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                item =>
                {
                    ContFileResult result = RunContinuationFile(
                        dataDir, item.Set, item.File, scope, onlyCorridor, specs);
                    lock (sync)
                    {
                        results.Add(result);
                    }

                    TestContext.Out.WriteLine(
                        $"{item.Set}/{item.File}: bars={result.Bars}, " +
                        $"signals={result.Samples}, attempts={result.Trades.Count}");
                });

            Assert.That(results, Is.Not.Empty, "The continuation replay produced no results.");
            Assert.That(results.Sum(r => r.Trades.Count), Is.GreaterThan(0),
                "The continuation simulation produced no attempts.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, CONT_REPORT);
            File.WriteAllText(reportPath, BuildContinuationReport(results, scope, sets.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, CONT_CSV);
            File.WriteAllText(csvPath, BuildContinuationCsv(results));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            // The providers are only needed for the simulation; drop them before the report
            // lists are handed to the GC.
            foreach (ContFileResult result in results)
                result.Provider = null!;
        }

        private static void AddSet(
            List<(string Name, IReadOnlyList<string> Files)> sets,
            string dataDir,
            string name,
            IReadOnlyList<string> files,
            string? onlySet)
        {
            if (onlySet != null)
            {
                HashSet<string> allowed = onlySet
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!allowed.Contains(name))
                    return;
            }

            List<string> existing = Existing(dataDir, files);
            if (existing.Count > 0)
                sets.Add((name, existing));
        }

        #region Replay and simulation

        private static ContFileResult RunContinuationFile(
            string dataDir,
            string set,
            string file,
            string scope,
            string? onlyCorridor,
            IReadOnlyList<ContSpec> specs)
        {
            ITimeFrame timeFrame = file.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;

            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(Path.Combine(dataDir, file));

            var result = new ContFileResult
            {
                Set = set,
                File = file,
                Bars = provider.Count,
                Provider = provider
            };

            var corridors = scope == "full"
                ? new[]
                {
                    ("exact", EXACT_CORRIDOR),
                    ("permissive", PERMISSIVE)
                }
                : new[] { ("exact", EXACT_CORRIDOR) };

            if (!string.IsNullOrEmpty(onlyCorridor))
            {
                HashSet<string> allowed = onlyCorridor
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                corridors = corridors
                    .Where(c => allowed.Contains(c.Item1))
                    .ToArray();
            }

            foreach ((string corridorName, DiagonalRunConfig config) in corridors)
            {
                foreach (DiagonalInitialMode mode in CONT_MODES)
                {
                    DiagonalRunConfig runConfig = config with
                    {
                        Name = $"{corridorName}/{mode}",
                        InitialMode = mode
                    };

                    List<Sample> samples = RunDiagonalSamples(provider, file, runConfig);
                    var seen = new HashSet<(int, int, bool)>();
                    samples = samples
                        .Where(s => seen.Add((s.P0Bar, s.SignalBar, s.IsUpDiagonal)))
                        .ToList();

                    result.Samples += samples.Count;
                    string key = $"{corridorName}/{mode}";
                    result.SamplesByMode.TryGetValue(key, out int current);
                    result.SamplesByMode[key] = current + samples.Count;

                    foreach (Sample sample in samples)
                    {
                        foreach (ContSpec spec in specs)
                        {
                            ContTrade trade = SimulateContinuation(
                                sample, set, corridorName, mode, spec);
                            result.Trades.Add(trade);
                        }
                    }
                }
            }

            return result;
        }

        private static ContTrade SimulateContinuation(
            Sample sample,
            string set,
            string corridor,
            DiagonalInitialMode mode,
            ContSpec spec)
        {
            return spec.Variant switch
            {
                "V1" => SimulateSignalAnchor(sample, set, corridor, mode, spec),
                "V2" => SimulateConfirmedEnd(sample, set, corridor, mode, spec),
                "V3" => SimulateDynamic(sample, set, corridor, mode, spec),
                _ => throw new ArgumentOutOfRangeException(nameof(spec.Variant))
            };
        }

        /// <summary>
        /// V1: the diagonal end is the signal's P5. A fixed limit at <c>P5 - kD</c> is placed
        /// after the signal and never re-priced. This is the earliest and simplest anchor, but
        /// the actual end can be much later.
        /// </summary>
        private static ContTrade SimulateSignalAnchor(
            Sample sample,
            string set,
            string corridor,
            DiagonalInitialMode mode,
            ContSpec spec)
        {
            TestBarsProvider provider = sample.Provider;
            bool isUp = sample.IsUpDiagonal;
            int sgn = isUp ? 1 : -1;
            double p0 = sample.V0;
            double end = sample.V5;
            double d = Math.Abs(end - p0);
            if (d <= 1e-12)
                return NoFill(sample, set, corridor, mode, spec, "degenerateDiagonal");

            int signalBar = sample.SignalBar;
            int lastBar = Math.Min(
                provider.Count - 1,
                signalBar + TotalWaitBars(sample.TimeFrame));
            double entryLevel = end - sgn * spec.K * d;

            for (int i = signalBar + 1; i <= lastBar; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                bool filled = isUp ? low <= entryLevel : high >= entryLevel;
                if (!filled)
                    continue;

                double open = provider.GetOpenPrice(i);
                double entry = isUp
                    ? Math.Min(open, entryLevel)
                    : Math.Max(open, entryLevel);
                return WalkContinuation(
                    sample, set, corridor, mode, spec,
                    d, end, sample.P5Bar, i, entry,
                    confirmations: 0, reanchors: 0);
            }

            return NoFill(sample, set, corridor, mode, spec, "noFill");
        }

        /// <summary>
        /// V2: track the running extreme after the signal; confirm the end when price retraces
        /// <c>c</c> of the current height; freeze the end and place a limit at <c>end - kD</c>.
        /// A new extreme before the fill cancels the confirmation and re-anchors the end.
        /// This variant exists only to measure what a separate end confirmation costs or adds.
        /// </summary>
        private static ContTrade SimulateConfirmedEnd(
            Sample sample,
            string set,
            string corridor,
            DiagonalInitialMode mode,
            ContSpec spec)
        {
            TestBarsProvider provider = sample.Provider;
            bool isUp = sample.IsUpDiagonal;
            int sgn = isUp ? 1 : -1;
            double p0 = sample.V0;
            double extreme = sample.V5;
            int extremeBar = sample.P5Bar;
            int signalBar = sample.SignalBar;
            int lastBar = Math.Min(
                provider.Count - 1,
                signalBar + TotalWaitBars(sample.TimeFrame));

            bool confirmed = false;
            double end = extreme;
            int endBar = extremeBar;
            int confirmations = 0;
            int reanchors = 0;

            for (int i = signalBar + 1; i <= lastBar; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                bool confirmedThisBar = false;

                bool newExtreme = false;
                if (isUp && high > extreme)
                {
                    extreme = high;
                    extremeBar = i;
                    newExtreme = true;
                }
                else if (!isUp && low < extreme)
                {
                    extreme = low;
                    extremeBar = i;
                    newExtreme = true;
                }

                if (newExtreme && confirmed)
                {
                    confirmed = false;
                    reanchors++;
                }

                double height = Math.Abs(extreme - p0);
                if (height <= 1e-12)
                    continue;

                if (!confirmed)
                {
                    double retrace = isUp
                        ? (extreme - low) / height
                        : (high - extreme) / height;
                    if (retrace >= spec.ConfirmRatio)
                    {
                        confirmed = true;
                        confirmedThisBar = true;
                        end = extreme;
                        endBar = extremeBar;
                        confirmations++;
                    }
                }

                if (!confirmed || confirmedThisBar)
                    continue;

                double d = Math.Abs(end - p0);
                if (d <= 1e-12)
                    continue;

                double entryLevel = end - sgn * spec.K * d;
                bool filled = isUp ? low <= entryLevel : high >= entryLevel;
                if (!filled)
                    continue;

                double open = provider.GetOpenPrice(i);
                double entry = isUp
                    ? Math.Min(open, entryLevel)
                    : Math.Max(open, entryLevel);
                return WalkContinuation(
                    sample, set, corridor, mode, spec,
                    d, end, endBar, i, entry,
                    confirmations, reanchors);
            }

            return NoFill(sample, set, corridor, mode, spec,
                confirmed ? "noFill" : "noEndConfirmation");
        }

        /// <summary>
        /// V3 (primary): the limit is re-priced every bar at <c>k</c> below the running
        /// extreme. The fill at <c>k</c> is itself the deep-correction trigger; no separate
        /// bar-based confirmation is used. The target uses the height known at the fill.
        /// </summary>
        private static ContTrade SimulateDynamic(
            Sample sample,
            string set,
            string corridor,
            DiagonalInitialMode mode,
            ContSpec spec)
        {
            TestBarsProvider provider = sample.Provider;
            bool isUp = sample.IsUpDiagonal;
            int sgn = isUp ? 1 : -1;
            double p0 = sample.V0;
            double extreme = sample.V5;
            int extremeBar = sample.P5Bar;
            int signalBar = sample.SignalBar;
            int lastBar = Math.Min(
                provider.Count - 1,
                signalBar + TotalWaitBars(sample.TimeFrame));
            int reanchors = 0;

            for (int i = signalBar + 1; i <= lastBar; i++)
            {
                // The limit for this bar is based on the extreme known at the previous close.
                double height = Math.Abs(extreme - p0);
                if (height <= 1e-12)
                    continue;

                double entryLevel = extreme - sgn * spec.K * height;
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                bool filled = isUp ? low <= entryLevel : high >= entryLevel;

                if (filled)
                {
                    double open = provider.GetOpenPrice(i);
                    double entry = isUp
                        ? Math.Min(open, entryLevel)
                        : Math.Max(open, entryLevel);
                    return WalkContinuation(
                        sample, set, corridor, mode, spec,
                        height, extreme, extremeBar, i, entry,
                        confirmations: 0, reanchors: reanchors);
                }

                bool newExtreme = false;
                if (isUp && high > extreme)
                {
                    extreme = high;
                    extremeBar = i;
                    newExtreme = true;
                }
                else if (!isUp && low < extreme)
                {
                    extreme = low;
                    extremeBar = i;
                    newExtreme = true;
                }

                if (newExtreme)
                    reanchors++;
            }

            return NoFill(sample, set, corridor, mode, spec, "noFill");
        }

        /// <summary>
        /// Resolves one filled continuation trade. The entry bar is handled conservatively:
        /// only the stop is checked there, because the target may have been hit before the
        /// limit fill. From the next bar both the target and the stop are checked, stop-first
        /// on a tie.
        /// </summary>
        private static ContTrade WalkContinuation(
            Sample sample,
            string set,
            string corridor,
            DiagonalInitialMode mode,
            ContSpec spec,
            double d,
            double end,
            int endBar,
            int entryBar,
            double entry,
            int confirmations,
            int reanchors)
        {
            TestBarsProvider provider = sample.Provider;
            bool isUp = sample.IsUpDiagonal;
            int sgn = isUp ? 1 : -1;
            double p0 = sample.V0;
            double stop = p0 - sgn * spec.StopBuffer * d;
            double originalStop = stop;
            double risk = Math.Abs(entry - stop);
            if (risk <= 1e-12)
                return NoFill(sample, set, corridor, mode, spec, "degenerateStop");

            double target = spec.TargetKind switch
            {
                "p0" => p0 + sgn * spec.F * d,
                "end" => end + sgn * spec.F * d,
                "entry" => entry + sgn * spec.F * d,
                _ => throw new ArgumentOutOfRangeException(nameof(spec.TargetKind))
            };

            if (sgn * (target - entry) <= 0)
                return NoFill(sample, set, corridor, mode, spec, "targetBehindEntry");

            double target1 = target;
            double target2 = spec.Ladder
                ? p0 + sgn * CONT_LADDER_TP2_RATIO * d
                : target;
            if (spec.Ladder && sgn * (target2 - target1) <= 0)
                return NoFill(sample, set, corridor, mode, spec, "degenerateLadder");

            int maxHold = HoldBars(sample.TimeFrame);
            int lastBar = Math.Min(provider.Count - 1, entryBar + maxHold);
            double mfeR = 0;
            double maeR = 0;
            double exitPrice = 0;
            int exitBar = -1;
            string outcome = "CENSOR";
            double grossR = 0;
            bool tp1Hit = false;

            // Entry bar: stop only. The target is not checked because the bar's favourable
            // extreme may have happened before the limit was filled.
            double high = provider.GetHighPrice(entryBar);
            double low = provider.GetLowPrice(entryBar);
            TrackExcursion(isUp, entry, high, low, risk, ref mfeR, ref maeR);

            bool stopHit = isUp ? low <= stop : high >= stop;
            if (stopHit)
            {
                outcome = "SL";
                exitBar = entryBar;
                exitPrice = stop;
                grossR = -1;
            }
            else
            {
                for (int i = entryBar + 1; i <= lastBar; i++)
                {
                    high = provider.GetHighPrice(i);
                    low = provider.GetLowPrice(i);
                    TrackExcursion(isUp, entry, high, low, risk, ref mfeR, ref maeR);

                    bool stopNow = isUp ? low <= stop : high >= stop;
                    bool tp1Now = isUp ? high >= target1 : low <= target1;
                    bool tp2Now = spec.Ladder && (isUp ? high >= target2 : low <= target2);

                    if (spec.Ladder && tp1Now && !tp1Hit)
                    {
                        tp1Hit = true;
                        // The runner's stop moves to the entry (breakeven).
                        stop = entry;
                    }

                    if (stopNow)
                    {
                        outcome = "SL";
                        exitBar = i;
                        exitPrice = stop;
                        grossR = tp1Hit
                            ? 0.5 * (sgn * (target1 - entry) / risk) +
                              0.5 * (sgn * (stop - entry) / risk)
                            : -1;
                        break;
                    }

                    if (spec.Ladder && tp1Hit && tp2Now)
                    {
                        outcome = "TP";
                        exitBar = i;
                        exitPrice = target2;
                        grossR = 0.5 * (sgn * (target1 - entry) / risk) +
                                0.5 * (sgn * (target2 - entry) / risk);
                        break;
                    }

                    if (!spec.Ladder && tp1Now)
                    {
                        outcome = "TP";
                        exitBar = i;
                        exitPrice = target1;
                        grossR = sgn * (target1 - entry) / risk;
                        break;
                    }
                }
            }

            if (outcome is not ("TP" or "SL"))
            {
                int markBar = Math.Min(provider.Count - 1, entryBar + maxHold);
                exitBar = markBar;
                exitPrice = provider.GetClosePrice(markBar);
                outcome = markBar < entryBar + maxHold ? "CENSOR" : "TIME";
                double markR = sgn * (exitPrice - entry) / risk;
                grossR = tp1Hit
                    ? 0.5 * (sgn * (target1 - entry) / risk) + 0.5 * markR
                    : markR;
            }

            double costR = entry * CONT_COST_RATE / risk;
            double stressCostR = entry * CONT_STRESS_COST_RATE / risk;
            double netR = grossR - costR;
            double stressNetR = grossR - stressCostR;
            bool resolved = outcome is "TP" or "SL";

            return new ContTrade
            {
                Set = set,
                Corridor = corridor,
                Mode = mode.ToString(),
                Variant = spec.Variant,
                File = sample.File,
                Symbol = sample.Symbol,
                TimeFrame = sample.TimeFrame,
                IsUp = isUp,
                SignalBar = sample.SignalBar,
                SignalTime = sample.EntryTime,
                P0Bar = sample.P0Bar,
                P0 = p0,
                EndBar = endBar,
                EndPrice = end,
                D = d,
                DPrice = entry > 1e-12 ? d / entry : double.NaN,
                BarsDiagonal = sample.P5Bar - sample.P0Bar,
                W2Retr = sample.W2Retr,
                W4W3 = sample.W4W3,
                W5W3 = sample.W5W3,
                SpillA234 = sample.A234,
                ApexRatio = sample.ApexRatio,
                EntryBar = entryBar,
                EntryTime = provider.GetOpenTime(entryBar),
                Entry = entry,
                Stop = originalStop,
                Target = target,
                Risk = risk,
                PlannedRr = Math.Abs(target - entry) / risk,
                K = spec.K,
                F = spec.F,
                TargetKind = spec.TargetKind,
                ConfirmRatio = spec.ConfirmRatio,
                StopBuffer = spec.StopBuffer,
                Ladder = spec.Ladder,
                Confirmations = confirmations,
                Reanchors = reanchors,
                EntryDepth = sgn * (end - entry) / d,
                BarsToFill = entryBar - sample.SignalBar,
                BarsToEnd = endBar - sample.SignalBar,
                Outcome = outcome,
                ExitBar = exitBar,
                ExitTime = exitBar >= 0 ? provider.GetOpenTime(exitBar) : default,
                ExitPrice = exitPrice,
                GrossR = grossR,
                CostR = costR,
                NetR = netR,
                StressCostR = stressCostR,
                StressNetR = stressNetR,
                Resolved = resolved,
                Profitable = resolved && netR > 0,
                MfeR = mfeR,
                MaeR = maeR,
                BarsHeld = exitBar >= 0 ? exitBar - entryBar : 0
            };
        }

        private static void TrackExcursion(
            bool isUp,
            double entry,
            double high,
            double low,
            double risk,
            ref double mfeR,
            ref double maeR)
        {
            double favorable = isUp ? high - entry : entry - low;
            double adverse = isUp ? entry - low : high - entry;
            mfeR = Math.Max(mfeR, favorable / risk);
            maeR = Math.Max(maeR, adverse / risk);
        }

        private static ContTrade NoFill(
            Sample sample,
            string set,
            string corridor,
            DiagonalInitialMode mode,
            ContSpec spec,
            string reason)
        {
            return new ContTrade
            {
                Set = set,
                Corridor = corridor,
                Mode = mode.ToString(),
                Variant = spec.Variant,
                File = sample.File,
                Symbol = sample.Symbol,
                TimeFrame = sample.TimeFrame,
                IsUp = sample.IsUpDiagonal,
                SignalBar = sample.SignalBar,
                SignalTime = sample.EntryTime,
                P0Bar = sample.P0Bar,
                P0 = sample.V0,
                EndBar = sample.P5Bar,
                EndPrice = sample.V5,
                D = Math.Abs(sample.V5 - sample.V0),
                DPrice = sample.V5 > 1e-12
                    ? Math.Abs(sample.V5 - sample.V0) / sample.V5
                    : double.NaN,
                BarsDiagonal = sample.P5Bar - sample.P0Bar,
                W2Retr = sample.W2Retr,
                W4W3 = sample.W4W3,
                W5W3 = sample.W5W3,
                SpillA234 = sample.A234,
                ApexRatio = sample.ApexRatio,
                K = spec.K,
                F = spec.F,
                TargetKind = spec.TargetKind,
                ConfirmRatio = spec.ConfirmRatio,
                StopBuffer = spec.StopBuffer,
                Ladder = spec.Ladder,
                EntryBar = -1,
                Outcome = "NOFILL",
                NoFillReason = reason,
                Resolved = false,
                Profitable = false
            };
        }

        private static int TotalWaitBars(string timeFrame)
        {
            return timeFrame == "m15"
                ? CONT_MAX_TOTAL_WAIT_M15
                : CONT_MAX_TOTAL_WAIT_H1;
        }

        private static int HoldBars(string timeFrame)
        {
            return timeFrame == "m15"
                ? CONT_MAX_HOLD_M15
                : CONT_MAX_HOLD_H1;
        }

        private static List<ContSpec> BuildContinuationSpecs(string scope)
        {
            var specs = new List<ContSpec>();
            string[] variants = { "V1", "V2", "V3" };
            double[] ks = scope == "full"
                ? CONT_ENTER_RATIOS
                : new[] { 0.5d, 0.618d };
            double[] p0Targets = scope == "full"
                ? CONT_P0_TARGETS
                : new[] { 1.0d, 1.618d };

            foreach (string variant in variants)
            {
                foreach (double k in ks)
                {
                    foreach (double f in p0Targets)
                    {
                        specs.Add(new ContSpec(
                            variant, k, "p0", f, CONT_CONFIRM_RATIO, 0, false));
                    }

                    if (scope != "full")
                        continue;

                    foreach (double g in CONT_END_TARGETS)
                    {
                        specs.Add(new ContSpec(
                            variant, k, "end", g, CONT_CONFIRM_RATIO, 0, false));
                    }

                    foreach (double f in CONT_ENTRY_TARGETS)
                    {
                        specs.Add(new ContSpec(
                            variant, k, "entry", f, CONT_CONFIRM_RATIO, 0, false));
                    }
                }
            }

            if (scope != "full")
                return specs;

            // V2 confirmation sensitivity: the limit is placed after c of the height.
            foreach (double k in new[] { 0.5d, 0.618d })
            {
                foreach (double f in new[] { 1.0d, 1.618d })
                {
                    foreach (double c in new[] { 0.05d, 0.15d, 0.20d })
                    {
                        specs.Add(new ContSpec(
                            "V2", k, "p0", f, c, 0, false));
                    }
                }
            }

            // Stop buffer sensitivity: P0 plus a share of D.
            foreach (double k in new[] { 0.5d, 0.618d })
            {
                foreach (double f in new[] { 1.0d, 1.618d })
                {
                    foreach (double buffer in new[] { 0.05d, 0.10d })
                    {
                        specs.Add(new ContSpec(
                            "V3", k, "p0", f, CONT_CONFIRM_RATIO, buffer, false));
                    }
                }
            }

            // Ladder: 50% at the diagonal end, 50% at P0 + 1.618D, runner stop at entry.
            foreach (double k in new[] { 0.5d, 0.618d })
            {
                specs.Add(new ContSpec(
                    "V3", k, "p0", 1.0d, CONT_CONFIRM_RATIO, 0, true));
            }

            return specs;
        }

        #endregion

        #region Report

        private static string BuildContinuationReport(
            IReadOnlyList<ContFileResult> results,
            string scope,
            int setCount)
        {
            var builder = new StringBuilder();
            List<ContTrade> trades = results.SelectMany(r => r.Trades).ToList();

            builder.AppendLine("# Diagonal as the entry impulse: continuation after a leading diagonal");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(
                "Pre-registered primary cells (fixed before this run, not re-tuned): " +
                "**P1** = exact corridor, `LEADING`, **V3** dynamic re-price, `k = 0.5`, " +
                "target `P0 + 1.0*D` (the diagonal end), stop `P0`, limit entry, 2 bp, " +
                "stop-first. **P2** = the same cell with `P0 + 1.618*D`. " +
                "The primary anchor is V3 because the deep correction itself is the entry " +
                "trigger: the limit is re-priced at `k` below the running extreme, and no " +
                "separate bar-based end confirmation is required. V1 (signal P5) and V2 " +
                "(10% retrace confirmation) are robustness checks.");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Scope: `{scope}`. Sets: {setCount}. Files: {results.Count}. Bars: {results.Sum(r => r.Bars)}. Signals: {results.Sum(r => r.Samples)}. Attempts: {trades.Count}."));
            builder.AppendLine();

            builder.AppendLine("## Method");
            builder.AppendLine();
            builder.AppendLine(
                "- The diagonal is the finder's emitted 0-1-2-3-4-5 setup. `P0` is the " +
                "diagonal start, `D` is the diagonal height, and the entry is a limit at " +
                "`P_end - k*D` in the diagonal's direction.");
            builder.AppendLine(
                "- **V3 (primary):** the limit is re-priced every bar at `k` below the " +
                "running extreme; the fill at `k` is the deep-correction trigger. The target " +
                "uses the height known at the fill. This is the 'no separate wait' variant.");
            builder.AppendLine(
                "- **V2 (robustness):** the end is confirmed when price retraces 10% of the " +
                "current height; the limit is then placed at `end - k*D`; a new extreme before " +
                "the fill re-anchors the end. This measures what a separate confirmation costs.");
            builder.AppendLine(
                "- **V1 (baseline):** the end is the signal's P5; the limit is fixed there and " +
                "never re-priced.");
            builder.AppendLine(
                "- Targets: `P0 + f*D` (from the start), `end + g*D` (from the end), and " +
                "`entry + f*D` (from the actual entry). The conservative target is `f = 1.0` " +
                "from P0, i.e. the diagonal end. The 1.618 extension is tested from all three " +
                "anchors because '1.618 of the diagonal' is ambiguous.");
            builder.AppendLine(
                "- Execution: limit fill, no fill on the signal bar, stop-first on a tie; the " +
                "entry bar checks the stop only (the target may have been hit before the fill). " +
                "2 bp round-trip cost; a 5 bp stress cost is stored for every trade. " +
                "Stop = P0 (a 5%/10% buffer is a full-scope sensitivity). " +
                $"Hold: {CONT_MAX_HOLD_H1} h1 / {CONT_MAX_HOLD_M15} m15 bars; " +
                $"entry validity: {CONT_MAX_TOTAL_WAIT_H1} h1 / {CONT_MAX_TOTAL_WAIT_M15} m15 bars.");
            builder.AppendLine(
                "- R is measured from the actual entry-to-stop risk. The planned R:R is not " +
                "the realized payoff: deep entries have a smaller risk and a larger R:R, but " +
                "also a lower fill probability.");
            builder.AppendLine();

            AppendContinuationSample(builder, results);
            AppendContinuationPrimary(builder, trades);
            AppendContinuationVariantComparison(builder, trades);
            AppendContinuationModes(builder, trades);
            AppendContinuationEntryCurve(builder, trades);
            AppendContinuationTargetCurve(builder, trades);
            AppendContinuationAnchorCurve(builder, trades);
            AppendContinuationSensitivity(builder, trades);
            AppendContinuationSplit(builder, trades);
            AppendContinuationPairs(builder, trades);
            AppendContinuationSides(builder, trades);
            AppendContinuationFeatures(builder, trades);
            AppendContinuationVerdict(builder, trades);

            return builder.ToString();
        }

        private static void AppendContinuationSample(
            StringBuilder builder,
            IReadOnlyList<ContFileResult> results)
        {
            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Set | File | Bars | Signals | Attempts |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach (ContFileResult result in results
                         .OrderBy(r => r.Set, StringComparer.Ordinal)
                         .ThenBy(r => r.File, StringComparer.Ordinal))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {result.Set} | {result.File} | {result.Bars} | {result.Samples} | {result.Trades.Count} |"));
            }

            builder.AppendLine();
            builder.AppendLine("Signals by mode and corridor:");
            builder.AppendLine();
            builder.AppendLine("| Set | File | exact/LEADING | exact/ALL | exact/ENDING | permissive/LEADING | permissive/ALL | permissive/ENDING |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (ContFileResult result in results
                         .OrderBy(r => r.Set, StringComparer.Ordinal)
                         .ThenBy(r => r.File, StringComparer.Ordinal))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {result.Set} | {result.File} | {GetSampleCount(result, "exact", "LEADING")} | {GetSampleCount(result, "exact", "ALL")} | {GetSampleCount(result, "exact", "ENDING")} | {GetSampleCount(result, "permissive", "LEADING")} | {GetSampleCount(result, "permissive", "ALL")} | {GetSampleCount(result, "permissive", "ENDING")} |"));
            }

            builder.AppendLine(
                "Permissive columns are zero when the run was limited to the exact corridor " +
                "(`CONT_SCOPE=primary`).");
            builder.AppendLine();
        }

        private static int GetSampleCount(ContFileResult result, string corridor, string mode)
        {
            result.SamplesByMode.TryGetValue($"{corridor}/{mode}", out int value);
            return value;
        }

        private static void AppendContinuationPrimary(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## Primary cells");
            builder.AppendLine();
            builder.AppendLine(
                "V3, exact corridor, `LEADING`, `k = 0.5`, stop `P0`, 2 bp. " +
                "P1 = `f=1.0` and P2 = `f=1.618` are pre-registered; `f=1.272` is the best " +
                "exploratory extension from the full grid. `N` = attempts, `fill` = limit " +
                "filled, `res` = TP/SL, `win` = profitable among resolved, `netR` = average " +
                "among resolved, `netR all` = average over all attempts (no-fill = 0, " +
                "time/censor marked to market).");
            builder.AppendLine();
            builder.AppendLine("| Target | Set | N | fill | res | win | netR | netR 5bp | netR all | avg R:R | MFE/R | MAE/R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (string target in new[] { "1.000", "1.272", "1.618" })
            {
                foreach (string set in new[] { "tuning", "fresh", "holdout", "m15-fresh" })
                {
                    List<ContTrade> slice = trades
                        .Where(t => t.Variant == "V3" &&
                                    t.Corridor == "exact" &&
                                    t.Mode == "LEADING" &&
                                    Math.Abs(t.K - 0.5d) < 1e-9 &&
                                    t.TargetKind == "p0" && IsBaseSpec(t) &&
                                    Math.Abs(t.F - double.Parse(target, CultureInfo.InvariantCulture)) < 1e-9 &&
                                    t.Set == set)
                        .ToList();
                    if (slice.Count == 0)
                        continue;

                    ContStats stats = ContStatsOf(slice);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {target} | {set} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgStressNetR:F3} | {stats.AvgNetRAll:F3} | {stats.AvgRr:F2} | {stats.AvgMfeR:F2} | {stats.AvgMaeR:F2} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine(
                "The same P1/P2 cells for V2 (10% confirmation) and V1 (signal P5) — " +
                "does the anchor change the answer?");
            builder.AppendLine();
            builder.AppendLine("| Variant | Target | Set | N | fill | res | win | netR | netR 5bp | netR all |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (string variant in new[] { "V3", "V2", "V1" })
            {
                foreach (string target in new[] { "1.000", "1.272", "1.618" })
                {
                    foreach (string set in new[] { "tuning", "fresh", "holdout" })
                    {
                        List<ContTrade> slice = trades
                            .Where(t => t.Variant == variant &&
                                        t.Corridor == "exact" &&
                                        t.Mode == "LEADING" &&
                                        Math.Abs(t.K - 0.5d) < 1e-9 &&
                                        t.TargetKind == "p0" && IsBaseSpec(t) &&
                                        Math.Abs(t.F - double.Parse(target, CultureInfo.InvariantCulture)) < 1e-9 &&
                                        t.Set == set)
                            .ToList();
                        if (slice.Count == 0)
                            continue;

                        ContStats stats = ContStatsOf(slice);
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {variant} | {target} | {set} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgStressNetR:F3} | {stats.AvgNetRAll:F3} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendContinuationVariantComparison(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## Anchor comparison");
            builder.AppendLine();
            builder.AppendLine(
                "V3 vs V2 vs V1, exact corridor, `LEADING`, stop `P0`, 2 bp, all sets pooled. " +
                "If V2 and V3 are nearly identical, the 10% confirmation adds nothing at " +
                "`k >= 0.5`; the fill itself is the deep-correction trigger.");
            builder.AppendLine();
            builder.AppendLine("| k | Target | Variant | N | fill | res | win | netR | netR all |");
            builder.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (double k in new[] { 0.382d, 0.5d, 0.618d })
            {
                foreach (string target in new[] { "1.000", "1.272", "1.618" })
                {
                    foreach (string variant in new[] { "V3", "V2", "V1" })
                    {
                        List<ContTrade> slice = trades
                            .Where(t => t.Variant == variant &&
                                        t.Corridor == "exact" &&
                                        t.Mode == "LEADING" &&
                                        Math.Abs(t.K - k) < 1e-9 &&
                                        t.TargetKind == "p0" && IsBaseSpec(t) &&
                                        Math.Abs(t.F - double.Parse(target, CultureInfo.InvariantCulture)) < 1e-9)
                            .ToList();
                        if (slice.Count == 0)
                            continue;

                        ContStats stats = ContStatsOf(slice);
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {k:F3} | {target} | {variant} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgNetRAll:F3} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendContinuationModes(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## Leading vs all vs ending");
            builder.AppendLine();
            builder.AppendLine(
                "V3, exact corridor, `k = 0.5`, stop `P0`, 2 bp. The structural test: if the " +
                "continuation is a property of the leading diagonal, `LEADING` must beat " +
                "`ENDING`; if they are equal, the trade is just a generic diagonal retracement.");
            builder.AppendLine();
            builder.AppendLine("| Target | Mode | Set | N | fill | res | win | netR | netR 5bp |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");

            foreach (string target in new[] { "1.000", "1.272", "1.618" })
            {
                double f = double.Parse(target, CultureInfo.InvariantCulture);
                foreach (string mode in new[] { "LEADING", "ALL", "ENDING" })
                {
                    foreach (string set in new[] { "tuning", "fresh", "holdout", "all-h1" })
                    {
                        List<ContTrade> slice = trades
                            .Where(t => t.Variant == "V3" &&
                                        t.Corridor == "exact" &&
                                        t.Mode == mode &&
                                        Math.Abs(t.K - 0.5d) < 1e-9 &&
                                        t.TargetKind == "p0" && IsBaseSpec(t) &&
                                        Math.Abs(t.F - f) < 1e-9 &&
                                        (set == "all-h1" ? t.Set != "m15-fresh" : t.Set == set))
                            .ToList();
                        if (slice.Count == 0)
                            continue;

                        ContStats stats = ContStatsOf(slice);
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {target} | {mode} | {set} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgStressNetR:F3} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendContinuationEntryCurve(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## Entry depth curve");
            builder.AppendLine();
            builder.AppendLine(
                "V3, exact corridor, `LEADING`, stop `P0`, 2 bp. `netR` is resolved-only; " +
                "`fill` is the share of attempts that reached the limit. The deeper the entry, " +
                "the better the R:R to a fixed target, but the lower the fill rate.");
            builder.AppendLine();
            builder.AppendLine("| Target | k | N | fill | res | win | netR | netR all | avg R:R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (string target in new[] { "1.000", "1.272", "1.618" })
            {
                foreach (double k in CONT_ENTER_RATIOS)
                {
                    List<ContTrade> slice = trades
                        .Where(t => t.Variant == "V3" &&
                                    t.Corridor == "exact" &&
                                    t.Mode == "LEADING" &&
                                    Math.Abs(t.K - k) < 1e-9 &&
                                    t.TargetKind == "p0" && IsBaseSpec(t) &&
                                    Math.Abs(t.F - double.Parse(target, CultureInfo.InvariantCulture)) < 1e-9)
                        .ToList();
                    if (slice.Count == 0)
                        continue;

                    ContStats stats = ContStatsOf(slice);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {target} | {k:F3} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgNetRAll:F3} | {stats.AvgRr:F2} |"));
                }
            }

            builder.AppendLine();
        }

        private static void AppendContinuationTargetCurve(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## Target curve from P0");
            builder.AppendLine();
            builder.AppendLine(
                "V3, exact corridor, `LEADING`, stop `P0`, 2 bp. `f = 1.0` is the diagonal " +
                "end; the rest are extensions from the diagonal start. This is the direct test " +
                "of 'the leading diagonal predicts an extension'.");
            builder.AppendLine();
            builder.AppendLine("| k | f | N | fill | res | win | netR | netR all | avg R:R |");
            builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (double k in new[] { 0.382d, 0.5d, 0.618d })
            {
                foreach (double f in CONT_P0_TARGETS)
                {
                    List<ContTrade> slice = trades
                        .Where(t => t.Variant == "V3" &&
                                    t.Corridor == "exact" &&
                                    t.Mode == "LEADING" &&
                                    Math.Abs(t.K - k) < 1e-9 &&
                                    t.TargetKind == "p0" && IsBaseSpec(t) &&
                                    Math.Abs(t.F - f) < 1e-9)
                        .ToList();
                    if (slice.Count == 0)
                        continue;

                    ContStats stats = ContStatsOf(slice);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {k:F3} | {f:F3} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgNetRAll:F3} | {stats.AvgRr:F2} |"));
                }
            }

            builder.AppendLine();
        }

        private static void AppendContinuationAnchorCurve(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            if (!trades.Any(t => t.TargetKind != "p0"))
                return;

            builder.AppendLine("## Target anchor: from the end and from the entry");
            builder.AppendLine();
            builder.AppendLine(
                "V3, exact corridor, `LEADING`, stop `P0`, 2 bp. 'From the end' is " +
                "`end + g*D`; 'from the entry' is `entry + f*D`. The latter is the closest " +
                "causal proxy for the classic wave-3 projection measured from the end of the " +
                "correction when the correction ends at the entry.");
            builder.AppendLine();
            builder.AppendLine("| k | Anchor | Ratio | N | fill | res | win | netR | netR all | avg R:R |");
            builder.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (double k in new[] { 0.5d, 0.618d })
            {
                foreach ((string kind, double[] values) in new[]
                         {
                             ("end", CONT_END_TARGETS),
                             ("entry", CONT_ENTRY_TARGETS)
                         })
                {
                    foreach (double f in values)
                    {
                        List<ContTrade> slice = trades
                            .Where(t => t.Variant == "V3" &&
                                        t.Corridor == "exact" &&
                                        t.Mode == "LEADING" &&
                                        Math.Abs(t.K - k) < 1e-9 &&
                                        t.TargetKind == kind &&
                                        IsBaseSpec(t) &&
                                        Math.Abs(t.F - f) < 1e-9)
                            .ToList();
                        if (slice.Count == 0)
                            continue;

                        ContStats stats = ContStatsOf(slice);
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {k:F3} | {kind} | {f:F3} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgNetRAll:F3} | {stats.AvgRr:F2} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendContinuationSensitivity(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## Sensitivity: confirmation, stop buffer, ladder, costs");
            builder.AppendLine();
            builder.AppendLine(
                "V3/V2, exact corridor, `LEADING`, `k = 0.5` and `0.618`, `f = 1.0/1.618`, " +
                "2 bp. `V2 c` is the retrace needed before the limit is placed; `buffer` is " +
                "`P0 - buffer*D`; `ladder` is 50% at the end and 50% at `P0 + 1.618D` with the " +
                "runner stop at the entry.");
            builder.AppendLine();
            builder.AppendLine("| Variant | k | f | c | buffer | ladder | N | fill | res | win | netR | netR 5bp |");
            builder.AppendLine("|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|");
            var sensitivity = trades
                .Where(t => t.Corridor == "exact" &&
                            t.Mode == "LEADING" &&
                            (t.Variant == "V2" || t.Variant == "V3") &&
                            (Math.Abs(t.K - 0.5d) < 1e-9 ||
                             Math.Abs(t.K - 0.618d) < 1e-9))
                .GroupBy(t => new
                {
                    t.Variant,
                    t.K,
                    t.F,
                    t.ConfirmRatio,
                    t.StopBuffer,
                    t.Ladder
                })
                .OrderBy(g => g.Key.Variant, StringComparer.Ordinal)
                .ThenBy(g => g.Key.K)
                .ThenBy(g => g.Key.F)
                .ThenBy(g => g.Key.ConfirmRatio)
                .ThenBy(g => g.Key.StopBuffer)
                .ThenBy(g => g.Key.Ladder);

            foreach (var group in sensitivity)
            {
                List<ContTrade> slice = group.ToList();
                if (slice.Count == 0)
                    continue;

                ContStats stats = ContStatsOf(slice);
                double avgStress = slice.Where(t => t.Resolved)
                    .Select(t => t.StressNetR)
                    .DefaultIfEmpty(0)
                    .Average();
                builder.AppendLine(FormattableString.Invariant(
                    $"| {group.Key.Variant} | {group.Key.K:F3} | {group.Key.F:F3} | {group.Key.ConfirmRatio:F2} | {group.Key.StopBuffer:F2} | {(group.Key.Ladder ? "yes" : "no")} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {avgStress:F3} |"));
            }

            builder.AppendLine();
        }

        private static void AppendContinuationSplit(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## Train / test and by year");
            builder.AppendLine();
            builder.AppendLine(
                "Primary cells (V3, exact, `LEADING`, `k = 0.5`, `f = 1.0/1.618`, stop P0, " +
                "2 bp). Train 2019–2022, test 2023–2026; pre-2019 is shown separately.");
            builder.AppendLine();
            builder.AppendLine("| Target | Set | Period | N | fill | res | win | netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");
            foreach (string target in new[] { "1.000", "1.272", "1.618" })
            {
                foreach (string set in new[] { "tuning", "fresh", "holdout" })
                {
                    List<ContTrade> slice = trades
                        .Where(t => t.Variant == "V3" &&
                                    t.Corridor == "exact" &&
                                    t.Mode == "LEADING" &&
                                    Math.Abs(t.K - 0.5d) < 1e-9 &&
                                    t.TargetKind == "p0" && IsBaseSpec(t) &&
                                    Math.Abs(t.F - double.Parse(target, CultureInfo.InvariantCulture)) < 1e-9 &&
                                    t.Set == set)
                        .ToList();
                    if (slice.Count == 0)
                        continue;

                    foreach ((string period, Func<ContTrade, bool> predicate) in new (string, Func<ContTrade, bool>)[]
                             {
                                 ("pre-2019", t => t.SignalTime.Year < 2019),
                                 ("2019–2022", t => t.SignalTime.Year is >= 2019 and <= 2022),
                                 ("2023–2026", t => t.SignalTime.Year >= 2023)
                             })
                    {
                        List<ContTrade> part = slice.Where(predicate).ToList();
                        if (part.Count == 0)
                            continue;

                        ContStats stats = ContStatsOf(part);
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {target} | {set} | {period} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} |"));
                    }
                }
            }

            builder.AppendLine();
            builder.AppendLine("By year, primary P1/P2, all h1 sets pooled:");
            builder.AppendLine();
            builder.AppendLine("| Target | Year | N | fill | res | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (string target in new[] { "1.000", "1.272", "1.618" })
            {
                foreach (IGrouping<int, ContTrade> year in trades
                             .Where(t => t.Variant == "V3" &&
                                         t.Corridor == "exact" &&
                                         t.Mode == "LEADING" &&
                                         Math.Abs(t.K - 0.5d) < 1e-9 &&
                                         t.TargetKind == "p0" && IsBaseSpec(t) &&
                                         Math.Abs(t.F - double.Parse(target, CultureInfo.InvariantCulture)) < 1e-9 &&
                                         t.Set != "m15-fresh")
                             .GroupBy(t => t.SignalTime.Year)
                             .OrderBy(g => g.Key))
                {
                    ContStats stats = ContStatsOf(year.ToList());
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {target} | {year.Key} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
        }

        private static void AppendContinuationPairs(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## By pair");
            builder.AppendLine();
            builder.AppendLine(
                "Primary cells (V3, exact, `LEADING`, `k = 0.5`, stop P0, 2 bp), " +
                "all h1 sets pooled. A single pair must not carry the result.");
            builder.AppendLine();
            builder.AppendLine("| Target | Pair | N | fill | res | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (string target in new[] { "1.000", "1.272", "1.618" })
            {
                foreach (IGrouping<string, ContTrade> pair in trades
                             .Where(t => t.Variant == "V3" &&
                                         t.Corridor == "exact" &&
                                         t.Mode == "LEADING" &&
                                         Math.Abs(t.K - 0.5d) < 1e-9 &&
                                         t.TargetKind == "p0" && IsBaseSpec(t) &&
                                         Math.Abs(t.F - double.Parse(target, CultureInfo.InvariantCulture)) < 1e-9 &&
                                         t.Set != "m15-fresh")
                             .GroupBy(t => t.Symbol)
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    ContStats stats = ContStatsOf(pair.ToList());
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {target} | {pair.Key} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
        }

        private static void AppendContinuationSides(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## By direction");
            builder.AppendLine();
            builder.AppendLine("Primary cells, all h1 sets pooled.");
            builder.AppendLine();
            builder.AppendLine("| Target | Side | N | fill | res | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (string target in new[] { "1.000", "1.272", "1.618" })
            {
                foreach (IGrouping<string, ContTrade> side in trades
                             .Where(t => t.Variant == "V3" &&
                                         t.Corridor == "exact" &&
                                         t.Mode == "LEADING" &&
                                         Math.Abs(t.K - 0.5d) < 1e-9 &&
                                         t.TargetKind == "p0" && IsBaseSpec(t) &&
                                         Math.Abs(t.F - double.Parse(target, CultureInfo.InvariantCulture)) < 1e-9 &&
                                         t.Set != "m15-fresh")
                             .GroupBy(t => t.IsUp ? "long" : "short")
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    ContStats stats = ContStatsOf(side.ToList());
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {target} | {side.Key} | {stats.N} | {stats.FillRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
        }

        private static void AppendContinuationFeatures(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## Feature audit");
            builder.AppendLine();
            builder.AppendLine(
                "AUC for `netR > 0` among resolved trades, V3, exact, `LEADING`, " +
                "`k = 0.5`, `f = 1.272`, all h1 sets pooled. 0.5 = no separation. " +
                "This is a separator audit, not a filter.");
            builder.AppendLine();
            builder.AppendLine("| Feature | AUC | q1 netR | q5 netR |");
            builder.AppendLine("|---|---:|---:|---:|");

            List<ContTrade> resolved = trades
                .Where(t => t.Variant == "V3" &&
                            t.Corridor == "exact" &&
                            t.Mode == "LEADING" &&
                            Math.Abs(t.K - 0.5d) < 1e-9 &&
                            t.TargetKind == "p0" && IsBaseSpec(t) &&
                            Math.Abs(t.F - 1.272d) < 1e-9 &&
                            t.Set != "m15-fresh" &&
                            t.Resolved)
                .ToList();

            var features = new (string Name, Func<ContTrade, double> Value)[]
            {
                ("D / price", t => t.DPrice),
                ("bars diagonal", t => t.BarsDiagonal),
                ("W2/W1", t => t.W2Retr),
                ("W4/W3", t => t.W4W3),
                ("W5/W3", t => t.W5W3),
                ("spill A(2-4)", t => t.SpillA234),
                ("apex ratio", t => t.ApexRatio),
                ("entry depth", t => t.EntryDepth),
                ("bars to fill", t => t.BarsToFill),
                ("reanchors", t => t.Reanchors)
            };

            foreach ((string name, Func<ContTrade, double> value) in features)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {name} | {ContAucOf(resolved, value):F3} | {ContQuantileNetR(resolved, value, 0.2):F3} | {ContQuantileNetR(resolved, value, 0.8):F3} |"));
            }

            builder.AppendLine();
        }

        private static void AppendContinuationVerdict(
            StringBuilder builder,
            IReadOnlyList<ContTrade> trades)
        {
            builder.AppendLine("## Verdict");
            builder.AppendLine();
            builder.AppendLine(
                "A primary cell passes only when it is positive on tuning train " +
                "(2019–2022), tuning test (2023–2026), fresh, and holdout, with at least 25 " +
                "resolved trades in each set and no single pair carrying more than half of " +
                "the profit. This is a screening rule, not a significance test.");
            builder.AppendLine();
            builder.AppendLine("| Cell | Tuning 2019–22 | Tuning 2023–26 | Fresh | Holdout | +pairs | max pair | 5bp fresh+holdout | Pass |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");

            foreach ((string cell, string target, bool preRegistered) in new[]
                     {
                         ("P1 f=1.000", "1.000", true),
                         ("P2 f=1.618", "1.618", true),
                         ("E1 f=1.272", "1.272", false)
                     })
            {
                (int N, double NetR) tuningTrain = ContCell(trades, target, "tuning", 2019, 2022);
                (int N, double NetR) tuningTest = ContCell(trades, target, "tuning", 2023, 2026);
                (int N, double NetR) fresh = ContCell(trades, target, "fresh", 0, 9999);
                (int N, double NetR) holdout = ContCell(trades, target, "holdout", 0, 9999);
                (int Positive, int Total, double MaxShare, double StressFreshHoldout) pair =
                    ContPairCheck(trades, target);

                bool enough = tuningTrain.N >= 25 && tuningTest.N >= 25 &&
                              fresh.N >= 25 && holdout.N >= 25;
                bool pass = enough &&
                            tuningTrain.NetR > 0 && tuningTest.NetR > 0 &&
                            fresh.NetR > 0 && holdout.NetR > 0 &&
                            pair.Positive >= 3 && pair.MaxShare <= 0.5 &&
                            pair.StressFreshHoldout > 0;

                string passText = preRegistered
                    ? pass ? "**yes**" : enough ? "no" : "no (N<25)"
                    : pass ? "**yes** (exploratory)" : "no (exploratory)";

                builder.AppendLine(FormattableString.Invariant(
                    $"| {cell} | {tuningTrain.N} / {tuningTrain.NetR:F3} | {tuningTest.N} / {tuningTest.NetR:F3} | {fresh.N} / {fresh.NetR:F3} | {holdout.N} / {holdout.NetR:F3} | {pair.Positive}/{pair.Total} | {pair.MaxShare:P0} | {pair.StressFreshHoldout:F3} | {passText} |"));
            }

            builder.AppendLine();
            builder.AppendLine("### What would falsify the hypothesis");
            builder.AppendLine();
            builder.AppendLine(
                "- A primary cell is negative on any of the four checks, or is carried by one " +
                "pair/year, or fails the 5 bp stress.");
            builder.AppendLine(
                "- `LEADING` is not better than `ENDING`: then the continuation is not a " +
                "property of the leading diagonal, just a generic diagonal retracement.");
            builder.AppendLine(
                "- The ex-post/confirmed anchor is positive but V3 is negative: the deep " +
                "correction is not enough, and the end detector is the bottleneck.");
            builder.AppendLine(
                "- The conservative target is positive but the extension is negative: the " +
                "'leading diagonal predicts an extension' part is false; only the return to " +
                "the old end is supported.");
            builder.AppendLine(
                "- The extension is positive but the conservative target is negative: the edge " +
                "is in the tail; the ladder is the practical implementation.");
            builder.AppendLine();
            builder.AppendLine("### Caveats");
            builder.AppendLine();
            builder.AppendLine(
                "- **E1 is exploratory.** `1.272` was selected from the full grid, so its " +
                "pass is a candidate for a new pre-registered validation, not a production " +
                "default.");
            builder.AppendLine(
                "- **Side bias.** E1 is long-biased: pooled long +0.282R vs short +0.001R, " +
                "and the 2023–2026 short side is negative. The conservative P1 is more " +
                "balanced but also has a weak short side in 2023–2026. A production rule " +
                "would need a side filter or a new validation.");
            builder.AppendLine(
                "- The finder's signal is the V(3) break; the diagonal may extend after it. " +
                "V3 re-prices the limit, but the target is the running extreme at the fill, " +
                "not necessarily the final end.");
            builder.AppendLine(
                "- The archive is bid-only; the 2 bp cost is a flat round-trip charge and the " +
                "5 bp stress is the conservative check. The cTrader backtest must add real " +
                "spread, swap and slippage.");
            builder.AppendLine(
                "- The sample is pair-holdout, not a new time period. The train/test split " +
                "inside each set is the time check.");
            builder.AppendLine(
                "- The grid is wide; only the pre-registered P1/P2 cells are evidence. The " +
                "rest is exploratory and must not be promoted without a new pre-registered run.");
            builder.AppendLine();
        }

        private static (int N, double NetR) ContCell(
            IReadOnlyList<ContTrade> trades,
            string target,
            string set,
            int yearFrom,
            int yearTo)
        {
            List<ContTrade> slice = ContPrimarySlice(trades, target)
                .Where(t => t.Set == set &&
                            t.SignalTime.Year >= yearFrom &&
                            t.SignalTime.Year <= yearTo &&
                            t.Resolved)
                .ToList();
            return (slice.Count, slice.Count == 0 ? 0 : slice.Average(t => t.NetR));
        }

        private static IEnumerable<ContTrade> ContPrimarySlice(
            IReadOnlyList<ContTrade> trades,
            string target)
        {
            double f = double.Parse(target, CultureInfo.InvariantCulture);
            return trades.Where(t => t.Variant == "V3" &&
                                     t.Corridor == "exact" &&
                                     t.Mode == "LEADING" &&
                                     Math.Abs(t.K - 0.5d) < 1e-9 &&
                                     t.TargetKind == "p0" && IsBaseSpec(t) &&
                                     Math.Abs(t.F - f) < 1e-9);
        }

        private static (int Positive, int Total, double MaxShare, double StressFreshHoldout)
            ContPairCheck(IReadOnlyList<ContTrade> trades, string target)
        {
            List<ContTrade> pooled = ContPrimarySlice(trades, target)
                .Where(t => t.Set != "m15-fresh" && t.Resolved)
                .ToList();

            var byPair = pooled
                .GroupBy(t => t.Symbol)
                .Select(g => new { Symbol = g.Key, Net = g.Sum(t => t.NetR) })
                .ToList();

            int positive = byPair.Count(x => x.Net > 0);
            double positiveSum = byPair.Where(x => x.Net > 0).Sum(x => x.Net);
            double maxShare = positiveSum > 0
                ? byPair.Where(x => x.Net > 0).Max(x => x.Net) / positiveSum
                : 0;

            List<ContTrade> freshHoldout = pooled
                .Where(t => t.Set is "fresh" or "holdout")
                .ToList();
            double stress = freshHoldout.Count == 0
                ? 0
                : freshHoldout.Average(t => t.StressNetR);

            return (positive, byPair.Count, maxShare, stress);
        }

        private static bool IsBaseSpec(ContTrade trade) =>
            Math.Abs(trade.StopBuffer) < 1e-9 &&
            !trade.Ladder &&
            Math.Abs(trade.ConfirmRatio - CONT_CONFIRM_RATIO) < 1e-9;

        private static ContStats ContStatsOf(IReadOnlyList<ContTrade> trades)
        {
            int n = trades.Count;
            List<ContTrade> filled = trades.Where(t => t.EntryBar >= 0).ToList();
            List<ContTrade> resolved = trades.Where(t => t.Resolved).ToList();
            List<ContTrade> censored = trades
                .Where(t => t.Outcome is "TIME" or "CENSOR")
                .ToList();

            return new ContStats(
                n,
                filled.Count,
                resolved.Count,
                censored.Count,
                n == 0 ? 0 : filled.Count / (double)n,
                resolved.Count == 0
                    ? 0
                    : resolved.Count(t => t.Profitable) / (double)resolved.Count,
                resolved.Count == 0 ? 0 : resolved.Average(t => t.NetR),
                resolved.Count == 0 ? 0 : resolved.Average(t => t.StressNetR),
                n == 0 ? 0 : trades.Average(t => t.NetR),
                filled.Count == 0 ? 0 : filled.Average(t => t.PlannedRr),
                filled.Count == 0 ? 0 : filled.Average(t => t.MfeR),
                filled.Count == 0 ? 0 : filled.Average(t => t.MaeR));
        }

        private static double ContAucOf(
            IReadOnlyList<ContTrade> trades,
            Func<ContTrade, double> value)
        {
            List<ContTrade> valid = trades
                .Where(t => t.Resolved && ContIsFinite(value(t)))
                .ToList();
            int positives = valid.Count(t => t.NetR > 0);
            int negatives = valid.Count - positives;
            if (positives == 0 || negatives == 0)
                return 0.5;

            double sum = 0;
            foreach (ContTrade positive in valid.Where(t => t.NetR > 0))
            {
                foreach (ContTrade negative in valid.Where(t => t.NetR <= 0))
                {
                    double p = value(positive);
                    double n = value(negative);
                    sum += p > n ? 1 : p < n ? 0 : 0.5;
                }
            }

            return sum / (positives * (double)negatives);
        }

        private static double ContQuantileNetR(
            IReadOnlyList<ContTrade> trades,
            Func<ContTrade, double> value,
            double quantile)
        {
            List<ContTrade> valid = trades
                .Where(t => t.Resolved && ContIsFinite(value(t)))
                .OrderBy(value)
                .ToList();
            if (valid.Count == 0)
                return 0;

            int index = (int)Math.Round(quantile * (valid.Count - 1));
            index = Math.Clamp(index, 0, valid.Count - 1);
            return valid[index].NetR;
        }

        private static bool ContIsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static string BuildContinuationCsv(IReadOnlyList<ContFileResult> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "set,corridor,mode,variant,file,symbol,timeframe,is_up,signal_time," +
                "signal_bar,p0_bar,p0,end_bar,end_price,d,d_price,bars_diagonal," +
                "w2_retr,w4_w3,w5_w3,spill_a234,apex_ratio,k,f,target_kind,confirm_ratio," +
                "stop_buffer,ladder,confirmations,reanchors,entry_time,entry_bar,entry,stop," +
                "target,risk,planned_rr,entry_depth,bars_to_fill,bars_to_end,outcome,exit_time," +
                "exit_bar,exit_price,gross_r,cost_r,net_r,stress_cost_r,stress_net_r,resolved," +
                "profitable,mfe_r,mae_r,bars_held,no_fill_reason");

            foreach (ContTrade t in results
                         .SelectMany(r => r.Trades)
                         .Where(t => t.Variant is "V1" or "V2" or "V3" &&
                                     t.TargetKind == "p0" && IsBaseSpec(t) &&
                                     (Math.Abs(t.K - 0.5d) < 1e-9 ||
                                      Math.Abs(t.K - 0.618d) < 1e-9) &&
                                     (Math.Abs(t.F - 1.0d) < 1e-9 ||
                                      Math.Abs(t.F - 1.272d) < 1e-9 ||
                                      Math.Abs(t.F - 1.618d) < 1e-9))
                         .OrderBy(t => t.Set, StringComparer.Ordinal)
                         .ThenBy(t => t.File, StringComparer.Ordinal)
                         .ThenBy(t => t.Variant, StringComparer.Ordinal)
                         .ThenBy(t => t.K)
                         .ThenBy(t => t.F))
            {
                builder.AppendLine(string.Join(",",
                    ContCsv(t.Set),
                    ContCsv(t.Corridor),
                    ContCsv(t.Mode),
                    ContCsv(t.Variant),
                    ContCsv(t.File),
                    ContCsv(t.Symbol),
                    ContCsv(t.TimeFrame),
                    ContB(t.IsUp),
                    ContCsv(t.SignalTime.ToString("o", CultureInfo.InvariantCulture)),
                    ContI(t.SignalBar),
                    ContI(t.P0Bar),
                    ContF(t.P0),
                    ContI(t.EndBar),
                    ContF(t.EndPrice),
                    ContF(t.D),
                    ContF(t.DPrice),
                    ContI(t.BarsDiagonal),
                    ContF(t.W2Retr),
                    ContF(t.W4W3),
                    ContF(t.W5W3),
                    ContF(t.SpillA234),
                    ContF(t.ApexRatio),
                    ContF(t.K),
                    ContF(t.F),
                    ContCsv(t.TargetKind),
                    ContF(t.ConfirmRatio),
                    ContF(t.StopBuffer),
                    ContB(t.Ladder),
                    ContI(t.Confirmations),
                    ContI(t.Reanchors),
                    ContCsv(t.EntryBar >= 0
                        ? t.EntryTime.ToString("o", CultureInfo.InvariantCulture)
                        : string.Empty),
                    ContI(t.EntryBar),
                    ContF(t.Entry),
                    ContF(t.Stop),
                    ContF(t.Target),
                    ContF(t.Risk),
                    ContF(t.PlannedRr),
                    ContF(t.EntryDepth),
                    ContI(t.BarsToFill),
                    ContI(t.BarsToEnd),
                    ContCsv(t.Outcome),
                    ContCsv(t.ExitBar >= 0
                        ? t.ExitTime.ToString("o", CultureInfo.InvariantCulture)
                        : string.Empty),
                    ContI(t.ExitBar),
                    ContF(t.ExitPrice),
                    ContF(t.GrossR),
                    ContF(t.CostR),
                    ContF(t.NetR),
                    ContF(t.StressCostR),
                    ContF(t.StressNetR),
                    ContB(t.Resolved),
                    ContB(t.Profitable),
                    ContF(t.MfeR),
                    ContF(t.MaeR),
                    ContI(t.BarsHeld),
                    ContCsv(t.NoFillReason)));
            }

            return builder.ToString();
        }

        private static string ContCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Contains(',') || value.Contains('"')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }

        private static string ContF(double value) =>
            value.ToString("F6", CultureInfo.InvariantCulture);

        private static string ContI(int value) =>
            value.ToString(CultureInfo.InvariantCulture);

        private static string ContB(bool value) => value ? "1" : "0";

        #endregion

        #region Records

        private sealed record ContSpec(
            string Variant,
            double K,
            string TargetKind,
            double F,
            double ConfirmRatio,
            double StopBuffer,
            bool Ladder);

        private sealed record ContStats(
            int N,
            int Fills,
            int Resolved,
            int Censored,
            double FillRate,
            double WinRate,
            double AvgNetR,
            double AvgStressNetR,
            double AvgNetRAll,
            double AvgRr,
            double AvgMfeR,
            double AvgMaeR);

        private sealed class ContFileResult
        {
            public string Set { get; init; } = string.Empty;
            public string File { get; init; } = string.Empty;
            public int Bars { get; init; }
            public int Samples { get; set; }
            public TestBarsProvider Provider { get; set; } = null!;
            public Dictionary<string, int> SamplesByMode { get; } = new();
            public List<ContTrade> Trades { get; } = new();
        }

        private sealed class ContTrade
        {
            public string Set { get; init; } = string.Empty;
            public string Corridor { get; init; } = string.Empty;
            public string Mode { get; init; } = string.Empty;
            public string Variant { get; init; } = string.Empty;
            public string File { get; init; } = string.Empty;
            public string Symbol { get; init; } = string.Empty;
            public string TimeFrame { get; init; } = string.Empty;
            public bool IsUp { get; init; }
            public int SignalBar { get; init; }
            public DateTime SignalTime { get; init; }
            public int P0Bar { get; init; }
            public double P0 { get; init; }
            public int EndBar { get; init; }
            public double EndPrice { get; init; }
            public double D { get; init; }
            public double DPrice { get; init; }
            public int BarsDiagonal { get; init; }
            public double W2Retr { get; init; }
            public double W4W3 { get; init; }
            public double W5W3 { get; init; }
            public double SpillA234 { get; init; }
            public double ApexRatio { get; init; }
            public int EntryBar { get; init; } = -1;
            public DateTime EntryTime { get; init; }
            public double Entry { get; init; }
            public double Stop { get; init; }
            public double Target { get; init; }
            public double Risk { get; init; }
            public double PlannedRr { get; init; }
            public double K { get; init; }
            public double F { get; init; }
            public string TargetKind { get; init; } = string.Empty;
            public double ConfirmRatio { get; init; }
            public double StopBuffer { get; init; }
            public bool Ladder { get; init; }
            public int Confirmations { get; init; }
            public int Reanchors { get; init; }
            public double EntryDepth { get; init; }
            public int BarsToFill { get; init; }
            public int BarsToEnd { get; init; }
            public string Outcome { get; init; } = "NOFILL";
            public int ExitBar { get; init; } = -1;
            public DateTime ExitTime { get; init; }
            public double ExitPrice { get; init; }
            public double GrossR { get; init; }
            public double CostR { get; init; }
            public double NetR { get; init; }
            public double StressCostR { get; init; }
            public double StressNetR { get; init; }
            public bool Resolved { get; init; }
            public bool Profitable { get; init; }
            public double MfeR { get; init; }
            public double MaeR { get; init; }
            public int BarsHeld { get; init; }
            public string NoFillReason { get; init; } = string.Empty;
        }

        #endregion
    }
}
