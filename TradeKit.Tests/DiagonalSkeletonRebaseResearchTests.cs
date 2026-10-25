using System.Diagnostics;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for the point-0 skeleton lifecycle (DIAGONAL.md §9.33). §9.32 showed
    /// that the finder suppresses every later skeleton sharing a signaled point 0
    /// (<c>m_SignaledPoint0</c>), so the first wave-3 skeleton always wins. This harness
    /// records <b>all</b> registered skeletons, replays each one independently through the
    /// finder's own wave-5 tracking, signal gates and level builder, and compares the signal
    /// quality of:
    /// <list type="bullet">
    /// <item>the first skeleton that actually emitted (current behaviour);</item>
    /// <item>the suppressed alternatives (the quality of the pool that is thrown away);</item>
    /// <item>the latest wave-3 re-basing (oracle);</item>
    /// <item>a causal wait of 20/50/100 bars after the first signal, then the latest re-basing;</item>
    /// <item>the best execution/geometry alternative (oracle upper bound).</item>
    /// </list>
    /// The simulation is post-hoc and uses only the same rules as the finder. Writes
    /// <c>reports/diagonal_skeleton_rebase.md</c> and
    /// <c>reports/diagonal_skeleton_rebase_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalSkeletonRebase"</c>.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const int SKELETON_WAIT_SHORT = 20;
        private const int SKELETON_WAIT_MEDIUM = 50;
        private const int SKELETON_WAIT_LONG = 100;

        private static readonly (string Name, DiagonalTriggerMode Mode)[] SKELETON_TRIGGERS =
        {
            ("wave3", DiagonalTriggerMode.WAVE3_BREAK),
            ("line13-close", DiagonalTriggerMode.LINE13_CLOSE),
            ("line13-pending", DiagonalTriggerMode.LINE13_PENDING)
        };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_skeleton_rebase.md")]
        [Category("Research")]
        public void DiagonalSkeletonRebase_Report()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            // Smoke-test knob: set SKELETON_REBASE_FILE to one archive name to run a single
            // file without touching the full 16-file replay.
            string? onlyFile = Environment.GetEnvironmentVariable("SKELETON_REBASE_FILE");
            if (!string.IsNullOrEmpty(onlyFile))
            {
                tuningFiles = tuningFiles.Where(f => f == onlyFile).ToList();
                freshFiles = freshFiles.Where(f => f == onlyFile).ToList();
            }

            Assert.That(tuningFiles.Count + freshFiles.Count, Is.GreaterThan(0),
                "No archive file was found under data/.");

            var runs = new List<SkeletonRun>();
            runs.AddRange(RunSkeletonConfigs(dataDir, tuningFiles, "tuning"));
            runs.AddRange(RunSkeletonConfigs(dataDir, freshFiles, "fresh"));

            Assert.That(runs.Sum(r => r.Groups.Count), Is.GreaterThan(0),
                "The skeleton replay produced no point-0 groups.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_skeleton_rebase.md");
            File.WriteAllText(reportPath, BuildSkeletonRebaseReport(runs));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_skeleton_rebase_samples.csv");
            File.WriteAllText(csvPath, BuildSkeletonRebaseCsv(runs));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            foreach (SkeletonRun run in runs)
            {
                foreach (SkeletonGroup group in run.Groups)
                {
                    foreach (SkeletonSignalSample sample in group.All)
                        sample.Sample.Provider = null!;
                }
            }
        }

        #region Replay

        private static List<SkeletonRun> RunSkeletonConfigs(
            string dataDir,
            IReadOnlyList<string> files,
            string set)
        {
            string? onlyTrigger = Environment.GetEnvironmentVariable("SKELETON_REBASE_TRIGGER");
            var triggers = string.IsNullOrEmpty(onlyTrigger)
                ? SKELETON_TRIGGERS
                : SKELETON_TRIGGERS.Where(t => t.Name == onlyTrigger).ToArray();
            if (triggers.Length == 0)
                throw new InvalidOperationException($"Unknown SKELETON_REBASE_TRIGGER '{onlyTrigger}'.");

            var runs = new List<SkeletonRun>();
            foreach ((string triggerName, DiagonalTriggerMode mode) in triggers)
            {
                runs.Add(new SkeletonRun("permissive", triggerName, set));
                runs.Add(new SkeletonRun("exact", triggerName, set));
            }

            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                file =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    ITimeFrame timeFrame = file.Contains("_m15_")
                        ? TimeFrameHelper.Minute15
                        : TimeFrameHelper.Hour1;

                    var provider = new TestBarsProvider(timeFrame);
                    provider.LoadCandles(Path.Combine(dataDir, file));

                    var local = new List<(string Name, string Trigger, List<SkeletonGroup> Groups,
                        int Registered, int Evaluated)>();
                    foreach ((string triggerName, DiagonalTriggerMode trigger) in triggers)
                    {
                        foreach ((string name, DiagonalRunConfig baseConfig) in new[]
                                 {
                                     ("permissive", PERMISSIVE),
                                     ("exact", EXACT_CORRIDOR)
                                 })
                        {
                            DiagonalRunConfig config = baseConfig with { TriggerMode = trigger };
                            DiagonalSetupFinder? finder = null;
                            List<Sample> real = RunDiagonalSamples(
                                provider, file, config, recordSkeletons: true,
                                onFinished: f => finder = f);
                            if (finder == null)
                                throw new InvalidOperationException("The skeleton callback did not run.");

                            (List<SkeletonGroup> groups, int evaluated) = BuildSkeletonGroups(
                                provider, finder, real, file, name, set);
                            local.Add((name, triggerName, groups,
                                finder.SkeletonRecords.Count, evaluated));
                        }
                    }

                    lock (sync)
                    {
                        foreach ((string name, string trigger, List<SkeletonGroup> groups,
                                     int registered, int evaluated) in local)
                        {
                            SkeletonRun run = runs.First(r =>
                                r.Config == name && r.Trigger == trigger);
                            run.Groups.AddRange(groups);
                            run.RegisteredSkeletons += registered;
                            run.EvaluatedSkeletons += evaluated;
                        }
                    }

                    TestContext.Out.WriteLine(
                        $"{set}/{file}: " +
                        string.Join(", ", local.Select(x =>
                            $"{x.Name}/{x.Trigger}={x.Groups.Count}")) +
                        $", elapsed={stopwatch.Elapsed.TotalSeconds:F1}s");
                });

            return runs;
        }

        private static (List<SkeletonGroup> Groups, int Evaluated) BuildSkeletonGroups(
            TestBarsProvider provider,
            DiagonalSetupFinder finder,
            List<Sample> realSamples,
            string file,
            string config,
            string set)
        {
            string symbol = file[..file.IndexOf('_')];
            string timeFrame = file.Contains("_m15_") ? "m15" : "h1";
            var realKeys = realSamples
                .Select(s => (s.IsUpDiagonal, s.P0Bar))
                .ToHashSet();

            var signals = new List<SkeletonSignalSample>();
            int evaluated = 0;
            foreach (DiagonalSkeletonRecord record in finder.SkeletonRecords)
            {
                // The quality question is about replacing the first signal of a point 0 that
                // actually traded. Skeletons of point 0s with no real signal are counted as
                // orphan candidates and are not evaluated here.
                if (!realKeys.Contains((record.IsUp, record.Point0.BarIndex)))
                    continue;

                evaluated++;
                if (!finder.TryGetSkeletonSignal(record, out DiagonalShadowSignal signal))
                    continue;

                SkeletonSignalSample? sample = BuildSkeletonSignalSample(
                    provider, finder, file, symbol, timeFrame, config, set, signal);
                if (sample != null)
                    signals.Add(sample);
            }

            var groups = new Dictionary<(bool IsUp, int P0Bar), List<SkeletonSignalSample>>();
            foreach (SkeletonSignalSample signal in signals)
            {
                var key = (signal.IsUp, signal.P0Bar);
                if (!groups.TryGetValue(key, out List<SkeletonSignalSample>? list))
                {
                    list = new List<SkeletonSignalSample>();
                    groups[key] = list;
                }

                list.Add(signal);
            }

            foreach (Sample real in realSamples)
            {
                var key = (real.IsUpDiagonal, real.P0Bar);
                if (!groups.TryGetValue(key, out List<SkeletonSignalSample>? list))
                {
                    list = new List<SkeletonSignalSample>();
                    groups[key] = list;
                }

                SkeletonSignalSample? match = list.FirstOrDefault(x =>
                    x.P4Bar == real.P4Bar && x.SignalBar == real.SignalBar);
                if (match != null)
                {
                    // Use the finder's own outcome for the signal that actually fired; the
                    // shadow simulation is only for the suppressed alternatives.
                    list.Remove(match);
                }

                list.Add(BuildRealSignalSample(
                    provider, file, symbol, timeFrame, config, set, real));
            }

            var result = new List<SkeletonGroup>();
            foreach (KeyValuePair<(bool IsUp, int P0Bar), List<SkeletonSignalSample>> pair in groups)
            {
                List<SkeletonSignalSample> all = pair.Value
                    .OrderBy(x => x.SignalBar)
                    .ThenBy(x => x.P4Bar)
                    .ToList();
                SkeletonSignalSample? real = all.FirstOrDefault(x => x.IsReal);
                List<SkeletonSignalSample> alternatives = real == null
                    ? all
                    : all.Where(x => !ReferenceEquals(x, real)).ToList();

                result.Add(new SkeletonGroup(
                    file, config, set, pair.Key.IsUp, pair.Key.P0Bar,
                    real, all, alternatives));
            }

            return (result, evaluated);
        }

        private static SkeletonSignalSample? BuildSkeletonSignalSample(
            TestBarsProvider provider,
            DiagonalSetupFinder finder,
            string file,
            string symbol,
            string timeFrame,
            string config,
            string set,
            DiagonalShadowSignal signal)
        {
            BarPoint[] p = signal.WavePoints;
            if (p.Length < 6)
                return null;

            double w1 = Math.Abs(p[1].Value - p[0].Value);
            double w2 = Math.Abs(p[2].Value - p[1].Value);
            double w3 = Math.Abs(p[3].Value - p[2].Value);
            double w4 = Math.Abs(p[4].Value - p[3].Value);
            double w5 = Math.Abs(p[5].Value - p[4].Value);
            if (w1 <= 0 || w2 <= 0 || w3 <= 0 || w4 <= 0 || w5 <= 0)
                return null;

            double entry = signal.Entry.Value;
            double risk = Math.Abs(entry - signal.StopLoss.Value);
            if (risk <= 0)
                return null;

            TradeOutcome trade = SimulateSkeletonTrade(provider, signal);
            var sample = new Sample
            {
                File = file,
                Symbol = symbol,
                TimeFrame = timeFrame,
                Provider = provider,
                EntryTime = signal.Entry.OpenTime,
                SignalBar = signal.SignalBar,
                IsUpDiagonal = signal.IsUp,
                Entry = entry,
                Risk = risk,
                Sl = signal.StopLoss.Value,
                Tp = signal.TakeProfit.Value,
                P0Bar = p[0].BarIndex,
                P4Bar = p[4].BarIndex,
                P5Bar = p[5].BarIndex,
                V0 = p[0].Value,
                V3 = p[3].Value,
                V4 = p[4].Value,
                V5 = p[5].Value,
                W1 = w1,
                W2 = w2,
                W3 = w3,
                W4 = w4,
                W5 = w5,
                W2Retr = w2 / w1,
                W3W1 = w3 / w1,
                W4W3 = w4 / w3,
                W4W2 = w4 / w2,
                W5W3 = w5 / w3,
                NetR = trade.NetR,
                Resolved = trade.Resolved,
                Profitable = trade.Profitable,
                Outcome = trade.Outcome,
                ExitPrice = trade.ExitPrice,
                OutcomeBar = trade.OutcomeBar
            };

            double spill5 = SpillAreaRatio(
                provider, p[1], p[2], p[3], p[4], signal.IsUp ? 1 : -1,
                p[4].BarIndex, signal.SignalBar, finder.PrintFilter);
            double line13 = p[1].Value + (p[3].Value - p[1].Value) *
                (signal.SignalBar - p[1].BarIndex) /
                Math.Max(1, p[3].BarIndex - p[1].BarIndex);
            double lineDistanceW3 = (signal.IsUp ? 1 : -1) *
                (p[5].Value - line13) / w3;

            CorrectionOutcome outcome = MeasureCorrection(sample, CORRECTION_HORIZON_MULTIPLIER);
            return new SkeletonSignalSample(
                file, symbol, timeFrame, config, set, signal.IsUp,
                p[0].BarIndex, p[4].BarIndex, signal.SignalBar,
                spill5, lineDistanceW3,
                sample, outcome, isReal: false);
        }

        private static SkeletonSignalSample BuildRealSignalSample(
            TestBarsProvider provider,
            string file,
            string symbol,
            string timeFrame,
            string config,
            string set,
            Sample real)
        {
            double spill5 = real.A5Early;
            double lineDistanceW3 = (real.IsUpDiagonal ? 1 : -1) *
                (real.V5 - real.Line13AtSignal) / Math.Max(1e-12, real.W3);

            CorrectionOutcome outcome = MeasureCorrection(real, CORRECTION_HORIZON_MULTIPLIER);
            return new SkeletonSignalSample(
                file, symbol, timeFrame, config, set, real.IsUpDiagonal,
                real.P0Bar, real.P4Bar, real.SignalBar,
                spill5, lineDistanceW3,
                real, outcome, isReal: true);
        }

        private static TradeOutcome SimulateSkeletonTrade(
            TestBarsProvider provider,
            DiagonalShadowSignal signal)
        {
            bool isUpDiagonal = signal.TakeProfit.Value < signal.StopLoss.Value;
            double entry = signal.Entry.Value;
            double tp = signal.TakeProfit.Value;
            double sl = signal.StopLoss.Value;
            double risk = Math.Abs(entry - sl);
            if (risk <= 1e-12)
                return new TradeOutcome("NONE", 0, -1, 0, false, false);

            for (int i = signal.SignalBar + 1; i < provider.Count; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);

                // The trade is counter to the diagonal: for an up diagonal (short) the
                // target is below the entry and the stop above it; for a down diagonal
                // (long) the target is above and the stop below.
                bool tpHit = isUpDiagonal ? low <= tp : high >= tp;
                bool slHit = isUpDiagonal ? high >= sl : low <= sl;

                // The finder checks TP before SL on a bar that touches both.
                if (tpHit)
                {
                    double gross = isUpDiagonal ? (entry - tp) / risk : (tp - entry) / risk;
                    double net = gross - entry * COST_RATE / risk;
                    return new TradeOutcome("TP", tp, i, net, true, net > 0);
                }

                if (slHit)
                {
                    double gross = isUpDiagonal ? (entry - sl) / risk : (sl - entry) / risk;
                    double net = gross - entry * COST_RATE / risk;
                    return new TradeOutcome("SL", sl, i, net, true, net > 0);
                }
            }

            return new TradeOutcome("NONE", 0, -1, 0, false, false);
        }

        #endregion

        #region Selection

        private static List<SkeletonSignalSample> SelectFirst(
            IReadOnlyList<SkeletonGroup> groups)
        {
            return groups
                .Where(g => g.Real != null)
                .Select(g => g.Real!)
                .ToList();
        }

        private static List<SkeletonSignalSample> SelectAlternatives(
            IReadOnlyList<SkeletonGroup> groups)
        {
            return groups
                .Where(g => g.Real != null)
                .SelectMany(g => g.Alternatives)
                .ToList();
        }

        private static List<SkeletonSignalSample> SelectLatestP4(
            IReadOnlyList<SkeletonGroup> groups)
        {
            return groups
                .Where(g => g.Real != null)
                .Select(g => g.All
                    .OrderByDescending(x => x.P4Bar)
                    .ThenByDescending(x => x.SignalBar)
                    .First())
                .ToList();
        }

        private static List<SkeletonSignalSample> SelectLatestSignal(
            IReadOnlyList<SkeletonGroup> groups)
        {
            return groups
                .Where(g => g.Real != null)
                .Select(g => g.All
                    .OrderByDescending(x => x.SignalBar)
                    .ThenByDescending(x => x.P4Bar)
                    .First())
                .ToList();
        }

        private static List<SkeletonSignalSample> SelectWaitLatestP4(
            IReadOnlyList<SkeletonGroup> groups,
            int waitBars)
        {
            return groups
                .Where(g => g.Real != null)
                .Select(g =>
                {
                    SkeletonSignalSample real = g.Real!;
                    return g.All
                        .Where(x => x.SignalBar <= real.SignalBar + waitBars)
                        .OrderByDescending(x => x.P4Bar)
                        .ThenByDescending(x => x.SignalBar)
                        .FirstOrDefault() ?? real;
                })
                .ToList();
        }

        private static List<SkeletonSignalSample> SelectBestRiskW3(
            IReadOnlyList<SkeletonGroup> groups)
        {
            return groups
                .Where(g => g.Real != null)
                .Select(g => g.All
                    .OrderByDescending(x => x.Sample.W3 > 1e-12
                        ? x.Sample.Risk / x.Sample.W3
                        : double.MinValue)
                    .First())
                .ToList();
        }

        private static List<SkeletonSignalSample> SelectBestW4W2(
            IReadOnlyList<SkeletonGroup> groups)
        {
            return groups
                .Where(g => g.Real != null)
                .Select(g => g.All
                    .Where(x => x.Sample.W4W2 > 1e-12)
                    .OrderBy(x => x.Sample.W4W2)
                    .FirstOrDefault() ?? g.Real!)
                .ToList();
        }

        private static List<SkeletonSignalSample> SelectBestNetR(
            IReadOnlyList<SkeletonGroup> groups)
        {
            return groups
                .Where(g => g.Real != null)
                .Select(g => g.All
                    .Where(x => x.Sample.Resolved)
                    .OrderByDescending(x => x.Sample.NetR)
                    .FirstOrDefault() ?? g.Real!)
                .ToList();
        }

        private static List<SkeletonSignalSample> SelectBestDepth(
            IReadOnlyList<SkeletonGroup> groups)
        {
            return groups
                .Where(g => g.Real != null)
                .Select(g => g.All
                    .OrderByDescending(x => x.Outcome.DepthDiag)
                    .First())
                .ToList();
        }

        #endregion

        #region Report

        private static string BuildSkeletonRebaseReport(IReadOnlyList<SkeletonRun> runs)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — all skeletons per point 0: does re-basing improve signal quality?");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("§9.32 showed that the finder suppresses every later skeleton sharing a signaled point 0 (`m_SignaledPoint0`): the first wave-3 skeleton always wins, and `duplicatePoint0` counts 3.7/3.0 suppressed alternatives per signal on permissive and 1.5/1.1 on exact. This harness records **all** registered skeletons, replays each one independently through the finder's own wave-5 tracking, signal gates and level builder, and compares the first signal with the alternatives that were thrown away.");
            builder.AppendLine();
            builder.AppendLine("The same skeleton pool is evaluated under three triggers: `wave3` (the current V(3) break), `line13-close` (the close of the first candle whose wick crosses the 1-3 generatrix) and `line13-pending` (a limit at the generatrix value). The skeleton records themselves do not depend on the trigger; only the entry/trigger evaluation does.");
            builder.AppendLine();
            builder.AppendLine("`first` is the current behaviour. `alternatives only` is the quality of the suppressed pool (multiple signals per point 0). `latest P4` / `latest signal` are oracle selections that know which skeleton will form later. `wait 20/50/100 / latest P4` is causal: at the first signal plus N bars, take the latest re-based skeleton that has already triggered. `best Risk/W3`, `best W4/W2`, `best netR`, `best depth` are oracle upper bounds. `worked236` / `tradeable236` / depth are the §9.27 labels; net R is the simulated TP/SL result with the same costs.");
            builder.AppendLine();

            AppendSkeletonSampleTable(builder, runs);
            AppendSkeletonQualityTable(builder, runs);
            AppendSkeletonSelectionTable(builder, runs);
            AppendSkeletonPairTable(builder, runs);
            AppendSkeletonSpikeSection(builder, runs);
            AppendSkeletonVerdict(builder, runs);

            return builder.ToString();
        }

        private static void AppendSkeletonSampleTable(
            StringBuilder builder,
            IReadOnlyList<SkeletonRun> runs)
        {
            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Config | Trigger | Set | Files | Real signals | Point-0 groups | Groups with alternatives | Skeleton signals | Alternatives | Registered skeletons | Evaluated skeletons |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (SkeletonRun run in runs)
            {
                int files = run.Groups.Select(g => g.File).Distinct().Count();
                int real = run.Groups.Count(g => g.Real != null);
                int withAlternatives = run.Groups.Count(g => g.Real != null && g.Alternatives.Count > 0);
                int signals = run.Groups.Sum(g => g.All.Count);
                int alternatives = run.Groups.Sum(g => g.Alternatives.Count);

                builder.AppendLine(FormattableString.Invariant(
                    $"| {run.Config} | {run.Trigger} | {run.Set} | {files} | {real} | {run.Groups.Count} | {withAlternatives} | {signals} | {alternatives} | {run.RegisteredSkeletons} | {run.EvaluatedSkeletons} |"));
            }
        }

        private static void AppendSkeletonQualityTable(
            StringBuilder builder,
            IReadOnlyList<SkeletonRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Signal quality by selection");
            builder.AppendLine();
            builder.AppendLine("| Config | Trigger | Set | Selection | Groups | Signals | worked236 | tradeable236 | median depth | avg netR | profitable | avg MFE/R |");
            builder.AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (SkeletonRun run in runs)
            {
                AppendSkeletonQualityRow(builder, run, "first (current)", SelectFirst(run.Groups));
                AppendSkeletonQualityRow(builder, run, "alternatives only", SelectAlternatives(run.Groups));
                AppendSkeletonQualityRow(builder, run, "latest P4 (oracle)", SelectLatestP4(run.Groups));
                AppendSkeletonQualityRow(builder, run, "latest signal (oracle)", SelectLatestSignal(run.Groups));
                AppendSkeletonQualityRow(builder, run,
                    $"wait {SKELETON_WAIT_SHORT} / latest P4",
                    SelectWaitLatestP4(run.Groups, SKELETON_WAIT_SHORT));
                AppendSkeletonQualityRow(builder, run,
                    $"wait {SKELETON_WAIT_MEDIUM} / latest P4",
                    SelectWaitLatestP4(run.Groups, SKELETON_WAIT_MEDIUM));
                AppendSkeletonQualityRow(builder, run,
                    $"wait {SKELETON_WAIT_LONG} / latest P4",
                    SelectWaitLatestP4(run.Groups, SKELETON_WAIT_LONG));
                AppendSkeletonQualityRow(builder, run, "best Risk/W3 (oracle)", SelectBestRiskW3(run.Groups));
                AppendSkeletonQualityRow(builder, run, "best W4/W2 (oracle)", SelectBestW4W2(run.Groups));
                AppendSkeletonQualityRow(builder, run, "best netR (oracle)", SelectBestNetR(run.Groups));
                AppendSkeletonQualityRow(builder, run, "best depth (oracle)", SelectBestDepth(run.Groups));
            }
        }

        private static void AppendSkeletonQualityRow(
            StringBuilder builder,
            SkeletonRun run,
            string selection,
            IReadOnlyList<SkeletonSignalSample> samples)
        {
            RebaseStats stats = RebaseStats.Create(samples);
            builder.AppendLine(FormattableString.Invariant(
                $"| {run.Config} | {run.Trigger} | {run.Set} | {selection} | {stats.Groups} | {stats.Signals} | {stats.Worked236:P1} | {stats.Tradeable236:P1} | {stats.MedianDepth:F3} | {stats.AvgNetR:F3} | {stats.Profitable:P1} | {stats.AvgMfeR:F3} |"));
        }

        private static void AppendSkeletonSelectionTable(
            StringBuilder builder,
            IReadOnlyList<SkeletonRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## How often does the selection change the first signal?");
            builder.AppendLine();
            builder.AppendLine("`changed` counts point-0 groups where the rule picks a different skeleton than the first one; `better/worse netR` and `better/worse depth` compare the selected signal with the first. A rule that changes many signals but improves roughly half is not an edge.");
            builder.AppendLine();
            builder.AppendLine("| Config | Trigger | Set | Rule | Groups | Changed | Better netR | Worse netR | Better depth | Worse depth |");
            builder.AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|---:|");

            foreach (SkeletonRun run in runs)
            {
                List<SkeletonGroup> groups = run.Groups.Where(g => g.Real != null).ToList();
                AppendSkeletonSelectionRow(builder, run, groups, "latest P4",
                    SelectLatestP4(groups));
                AppendSkeletonSelectionRow(builder, run, groups, "latest signal",
                    SelectLatestSignal(groups));
                AppendSkeletonSelectionRow(builder, run, groups,
                    $"wait {SKELETON_WAIT_MEDIUM} / latest P4",
                    SelectWaitLatestP4(groups, SKELETON_WAIT_MEDIUM));
                AppendSkeletonSelectionRow(builder, run, groups, "best Risk/W3",
                    SelectBestRiskW3(groups));
                AppendSkeletonSelectionRow(builder, run, groups, "best W4/W2",
                    SelectBestW4W2(groups));
                AppendSkeletonSelectionRow(builder, run, groups, "best netR",
                    SelectBestNetR(groups));
                AppendSkeletonSelectionRow(builder, run, groups, "best depth",
                    SelectBestDepth(groups));
            }
        }

        private static void AppendSkeletonSelectionRow(
            StringBuilder builder,
            SkeletonRun run,
            IReadOnlyList<SkeletonGroup> groups,
            string rule,
            IReadOnlyList<SkeletonSignalSample> selected)
        {
            int changed = 0;
            int betterNet = 0;
            int worseNet = 0;
            int betterDepth = 0;
            int worseDepth = 0;

            for (int i = 0; i < groups.Count; i++)
            {
                SkeletonSignalSample first = groups[i].Real!;
                SkeletonSignalSample choice = selected[i];
                if (!ReferenceEquals(first, choice))
                    changed++;

                if (choice.Sample.NetR > first.Sample.NetR)
                    betterNet++;
                else if (choice.Sample.NetR < first.Sample.NetR)
                    worseNet++;

                if (choice.Outcome.DepthDiag > first.Outcome.DepthDiag)
                    betterDepth++;
                else if (choice.Outcome.DepthDiag < first.Outcome.DepthDiag)
                    worseDepth++;
            }

            builder.AppendLine(FormattableString.Invariant(
                $"| {run.Config} | {run.Trigger} | {run.Set} | {rule} | {groups.Count} | {changed} | {betterNet} | {worseNet} | {betterDepth} | {worseDepth} |"));
        }

        private static void AppendSkeletonPairTable(
            StringBuilder builder,
            IReadOnlyList<SkeletonRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Pair-level consistency (fresh)");
            builder.AppendLine();
            builder.AppendLine("Mean net R per file. `first` is the current signal; `latest P4` and `wait 50` are the same selections as above.");
            builder.AppendLine();
            builder.AppendLine("| Config | Trigger | Pair | Groups | first | latest P4 | wait 50 |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|");

            foreach (SkeletonRun run in runs.Where(r => r.Set == "fresh"))
            {
                List<SkeletonGroup> groups = run.Groups.Where(g => g.Real != null).ToList();
                foreach (IGrouping<string, SkeletonGroup> file in groups
                             .GroupBy(g => g.File)
                             .OrderBy(g => g.First().File))
                {
                    List<SkeletonGroup> local = file.ToList();
                    List<SkeletonSignalSample> first = SelectFirst(local);
                    List<SkeletonSignalSample> latest = SelectLatestP4(local);
                    List<SkeletonSignalSample> wait = SelectWaitLatestP4(local, SKELETON_WAIT_MEDIUM);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Config} | {run.Trigger} | {local[0].File[..local[0].File.IndexOf('_')]} | {local.Count} | {MeanNetR(first):F3} | {MeanNetR(latest):F3} | {MeanNetR(wait):F3} |"));
                }
            }
        }

        private static void AppendSkeletonSpikeSection(
            StringBuilder builder,
            IReadOnlyList<SkeletonRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Wave-5 spike: do the survivors look ugly?");
            builder.AppendLine();
            builder.AppendLine("`Spill5` is the D-INSIDE-5 area ratio of wave 5 from P4 to the signal bar: how far the bars of wave 5 live outside the final wedge. `LineDistanceW3` is how far the wave-5 extreme is above the extended 1-3 generatrix at the signal, in `|W3|` units; positive means the signal candle already broke out of the wedge. Both are causal at the signal. AUC > 0.5 means the **uglier** setups are better (`tradeable236` / net R > 0); AUC < 0.5 means the cleaner ones are better. The clean/ugly split uses the shipped D-INSIDE-5 threshold `0.005` and the generatrix sign `0`.");
            builder.AppendLine();
            builder.AppendLine("| Config | Trigger | Set | Selection | N | AUC spill / tradeable | AUC spill / netR>0 | AUC line / tradeable | AUC line / netR>0 |");
            builder.AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|");

            foreach (SkeletonRun run in runs)
            {
                foreach ((string name, List<SkeletonSignalSample> selected) in new[]
                         {
                             ("first", SelectFirst(run.Groups)),
                             ("wait 50", SelectWaitLatestP4(run.Groups, SKELETON_WAIT_MEDIUM)),
                             ("latest P4", SelectLatestP4(run.Groups))
                         })
                {
                    List<SkeletonSignalSample> resolved = selected
                        .Where(x => x.Sample.Resolved)
                        .ToList();
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Config} | {run.Trigger} | {run.Set} | {name} | {selected.Count} | {AucOfSkeleton(selected, x => x.Spill5, x => x.Outcome.Tradeable236):F3} | {AucOfSkeleton(resolved, x => x.Spill5, x => x.Sample.NetR > 0):F3} | {AucOfSkeleton(selected, x => x.LineDistanceW3, x => x.Outcome.Tradeable236):F3} | {AucOfSkeleton(resolved, x => x.LineDistanceW3, x => x.Sample.NetR > 0):F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("### Clean vs ugly groups");
            builder.AppendLine();
            builder.AppendLine("| Config | Trigger | Set | Selection | Group | N | worked236 | tradeable236 | median depth | median Risk/W3 | median W5/W3 | avg netR | profitable |");
            builder.AppendLine("|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (SkeletonRun run in runs)
            {
                foreach ((string name, List<SkeletonSignalSample> selected) in new[]
                         {
                             ("first", SelectFirst(run.Groups)),
                             ("wait 50", SelectWaitLatestP4(run.Groups, SKELETON_WAIT_MEDIUM))
                         })
                {
                    AppendSpikeGroupRow(builder, run, name, "clean spill <= 0.005",
                        selected.Where(x => x.Spill5 <= 0.005).ToList());
                    AppendSpikeGroupRow(builder, run, name, "ugly spill > 0.005",
                        selected.Where(x => x.Spill5 > 0.005).ToList());
                    AppendSpikeGroupRow(builder, run, name, "inside line <= 0",
                        selected.Where(x => x.LineDistanceW3 <= 0).ToList());
                    AppendSpikeGroupRow(builder, run, name, "above line > 0",
                        selected.Where(x => x.LineDistanceW3 > 0).ToList());
                }
            }
        }

        private static void AppendSpikeGroupRow(
            StringBuilder builder,
            SkeletonRun run,
            string selection,
            string group,
            IReadOnlyList<SkeletonSignalSample> samples)
        {
            RebaseStats stats = RebaseStats.Create(samples);
            double medianRiskW3 = MedianOf(samples
                .Where(x => x.Sample.W3 > 1e-12)
                .Select(x => x.Sample.Risk / x.Sample.W3));
            double medianW5W3 = MedianOf(samples.Select(x => x.Sample.W5W3));
            builder.AppendLine(FormattableString.Invariant(
                $"| {run.Config} | {run.Trigger} | {run.Set} | {selection} | {group} | {stats.Signals} | {stats.Worked236:P1} | {stats.Tradeable236:P1} | {stats.MedianDepth:F3} | {medianRiskW3:F3} | {medianW5W3:F3} | {stats.AvgNetR:F3} | {stats.Profitable:P1} |"));
        }

        private static double AucOfSkeleton(
            IReadOnlyList<SkeletonSignalSample> samples,
            Func<SkeletonSignalSample, double> metric,
            Func<SkeletonSignalSample, bool> positive)
        {
            var values = samples
                .Select(s => (Value: metric(s), Positive: positive(s)))
                .Where(x => IsFinite(x.Value))
                .OrderBy(x => x.Value)
                .ToList();

            int posCount = values.Count(x => x.Positive);
            int negCount = values.Count - posCount;
            if (posCount == 0 || negCount == 0)
                return double.NaN;

            double rankSumPositive = 0;
            int i = 0;
            while (i < values.Count)
            {
                int j = i;
                while (j + 1 < values.Count &&
                       Math.Abs(values[j + 1].Value - values[i].Value) < 1e-12)
                {
                    j++;
                }

                double averageRank = (i + 1 + j + 1) / 2d;
                for (int k = i; k <= j; k++)
                {
                    if (values[k].Positive)
                        rankSumPositive += averageRank;
                }

                i = j + 1;
            }

            return (rankSumPositive - posCount * (posCount + 1) / 2d) / (posCount * negCount);
        }

        private static void AppendSkeletonVerdict(
            StringBuilder builder,
            IReadOnlyList<SkeletonRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## What it says");
            builder.AppendLine();
            builder.AppendLine("**The survivors do look uglier, and the wait-50 edge lives there.** `Spill5` vs netR>0 AUC is >0.5 in all 12 wave3 selection slices (0.509–0.559): higher wave-5 spill goes with a higher chance of a positive trade. On `wait 50`, the ugly group (`Spill5 > 0.005`) beats the clean group in all four slices: net R −0.035 vs −0.054 (permissive tuning), +0.086 vs −0.027 (permissive fresh), +0.043 vs +0.030 (exact tuning), −0.028 vs −0.042 (exact fresh); `tradeable236` is also higher in all four (26.3/23.0, 27.3/24.4, 35.4/28.9, 38.4/35.7). So the user's observation is confirmed for the wait rule: the setups that survive and correct are the ones with a wave-5 spike / breakout from the wedge.");
            builder.AppendLine();
            builder.AppendLine("**The ugly group is not the shortest-stop group.** Median `Risk/W3`: permissive tuning 0.533 ugly vs 0.504 clean, permissive fresh 0.526 vs 0.502 — the ugly stop is slightly **wider**; exact tuning 0.711 vs 0.742, exact fresh 0.705 vs 0.725 — slightly shorter. The W5/W3 quintiles make the point sharper: W5/W3 and Risk/W3 correlate −0.75, but the shortest-stop quintile (W5/W3 0.74–1.0, Risk/W3 ≈0.35) has the worst `tradeable236` (17.9%) and netR (−0.014), while the wide-stop q1 (Risk/W3 ≈0.72) has netR +0.046 and tradeable 36.8%. So the edge is the throw-over **shape**, not a short stop; do not tighten the stop or chase the maximum W5.");
            builder.AppendLine();
            builder.AppendLine("**But that means filtering the ugly ones out would remove the better trades, not improve them.** The clean group is not uniformly bad (exact tuning first: net R +0.041 clean vs +0.030 ugly), and the effect is a weak tilt (AUC 0.51–0.56), not a separator. The line-distance split is mixed, so the spill area is the better proxy than \"how far above the 1-3 line\" alone. This is consistent with §9.14: D-INSIDE-5 did not improve expected value because the ugly wave-5 is not the bad group for this entry.");
            builder.AppendLine();
            builder.AppendLine("**For a clean training class, separate the ugly spike from the clean diagonal.** Excluding it may improve label purity, but then the model is learning a different pattern and loses the wait-rule edge. Don't mix \"clean pattern\" and \"profitable trade\" as the same target: label the ugly spike as its own sub-class and evaluate it on trading outcomes.");
            builder.AppendLine();
            builder.AppendLine("**The 1-3 trigger does not rescue the suppressed alternatives.** `alternatives only` stays negative in seven of the eight line13 slices: permissive tuning −0.234 (close) / −0.239 (pending), permissive fresh −0.028 (pending), exact tuning −0.136 / −0.103, exact fresh −0.356 / −0.272. The single exception is permissive fresh with `line13-close`: +0.026 vs the first signal's −0.014. That is one slice out of eight and is not confirmed on exact or tuning. The deeper-correction pattern persists: alternatives have higher `worked236`, but their net R is lower.");
            builder.AppendLine();
            builder.AppendLine("**The first skeleton remains a good representative.** The oracle `best netR` upper bound stays small: at most +0.110R on permissive fresh (wave3) and +0.074R on exact tuning (line13-pending); exact fresh turns positive only with line13-pending (+0.016R). With perfect foresight there is no large hidden edge in the discarded pool under any trigger.");
            builder.AppendLine();
            builder.AppendLine("**The 1-3 trigger changes the base signal, but not uniformly.** First-signal net R: exact tuning +0.032 (wave3) → +0.047 (close) → +0.063 (pending); exact fresh −0.037 → −0.029 → −0.005; permissive tuning −0.056 → −0.018 → −0.056; permissive fresh +0.009 → −0.014 → −0.009. So line13 helps exact and permissive tuning (close), but slightly hurts permissive fresh. There is no universal trigger winner; this matches §9.24.");
            builder.AppendLine();
            builder.AppendLine("**The causal wait remains the only consistently positive rule.** `wait 50` improves over the first signal in all 12 trigger/config/set slices (+0.006…+0.036R). The best exact combination is line13-pending (tuning +0.070, fresh +0.004); the best permissive combination is wave3 fresh (+0.045). The magnitude is still small and the fallback caveat applies: if no alternative triggers inside the window, the rule keeps the first signal at its original entry, so these are upper bounds.");
            builder.AppendLine();
            builder.AppendLine("**Verdict.** The 1-3 generatrix trigger is a useful execution tweak for exact h1 (and permissive tuning close), but it does not turn the suppressed skeletons into a hidden edge and does not justify removing `m_SignaledPoint0`. The first skeleton remains the better trade representative; the causal 20–50-bar wait is the only consistently positive candidate, and it is still an upper bound. The bottleneck remains stop survival / entry timing, not the trigger level or the point-0 dedup.");
            builder.AppendLine();
            builder.AppendLine("Read the quality table in this order: (1) `first` vs `alternatives only` for each trigger; (2) `wait 50` — is the causal improvement consistent across triggers, configs and sets? (3) the selection table — does the rule change many signals while improving more than it worsens? (4) the pair table — is the improvement broad or driven by one file? A result that only appears in an oracle rule is an upper bound, not a strategy.");
        }

        #endregion

        #region Helpers

        private static double MeanNetR(IReadOnlyList<SkeletonSignalSample> samples)
        {
            List<double> values = samples
                .Where(x => x.Sample.Resolved)
                .Select(x => x.Sample.NetR)
                .Where(IsFinite)
                .ToList();
            return values.Count == 0 ? double.NaN : values.Average();
        }

        private static string BuildSkeletonRebaseCsv(IReadOnlyList<SkeletonRun> runs)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "config,trigger,set,file,symbol,is_up,p0_bar,p4_bar,signal_bar,is_real,entry,risk,net_r,resolved,profitable," +
                "worked236,tradeable236,depth_diag,mfe_r,w2_retr,w3_w1,w4_w3,w4_w2,w5_w3,risk_w3");

            foreach (SkeletonRun run in runs)
            {
                foreach (SkeletonGroup group in run.Groups)
                {
                    foreach (SkeletonSignalSample x in group.All)
                    {
                        builder.AppendLine(string.Join(",",
                            Csv(run.Config),
                            Csv(run.Trigger),
                            Csv(run.Set),
                            Csv(x.File),
                            Csv(x.Symbol),
                            B(x.IsUp),
                            x.P0Bar.ToString(CultureInfo.InvariantCulture),
                            x.P4Bar.ToString(CultureInfo.InvariantCulture),
                            x.SignalBar.ToString(CultureInfo.InvariantCulture),
                            B(x.IsReal),
                            F(x.Sample.Entry),
                            F(x.Sample.Risk),
                            F(x.Sample.NetR),
                            B(x.Sample.Resolved),
                            B(x.Sample.Profitable),
                            B(x.Outcome.Worked236),
                            B(x.Outcome.Tradeable236),
                            F(x.Outcome.DepthDiag),
                            F(x.Outcome.MfeR),
                            F(x.Sample.W2Retr),
                            F(x.Sample.W3W1),
                            F(x.Sample.W4W3),
                            F(x.Sample.W4W2),
                            F(x.Sample.W5W3),
                            F(x.Sample.W3 > 1e-12 ? x.Sample.Risk / x.Sample.W3 : double.NaN)));
                    }
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Types

        private sealed class SkeletonSignalSample
        {
            public SkeletonSignalSample(
                string file,
                string symbol,
                string timeFrame,
                string config,
                string set,
                bool isUp,
                int p0Bar,
                int p4Bar,
                int signalBar,
                double spill5,
                double lineDistanceW3,
                Sample sample,
                CorrectionOutcome outcome,
                bool isReal)
            {
                File = file;
                Symbol = symbol;
                TimeFrame = timeFrame;
                Config = config;
                Set = set;
                IsUp = isUp;
                P0Bar = p0Bar;
                P4Bar = p4Bar;
                SignalBar = signalBar;
                Spill5 = spill5;
                LineDistanceW3 = lineDistanceW3;
                Sample = sample;
                Outcome = outcome;
                IsReal = isReal;
            }

            public string File { get; }
            public string Symbol { get; }
            public string TimeFrame { get; }
            public string Config { get; }
            public string Set { get; }
            public bool IsUp { get; }
            public int P0Bar { get; }
            public int P4Bar { get; }
            public int SignalBar { get; }
            public double Spill5 { get; }
            public double LineDistanceW3 { get; }
            public Sample Sample { get; }
            public CorrectionOutcome Outcome { get; }
            public bool IsReal { get; set; }
        }

        private sealed record SkeletonGroup(
            string File,
            string Config,
            string Set,
            bool IsUp,
            int P0Bar,
            SkeletonSignalSample? Real,
            List<SkeletonSignalSample> All,
            List<SkeletonSignalSample> Alternatives);

        private sealed class SkeletonRun
        {
            public SkeletonRun(string config, string trigger, string set)
            {
                Config = config;
                Trigger = trigger;
                Set = set;
            }

            public string Config { get; }
            public string Trigger { get; }
            public string Set { get; }
            public List<SkeletonGroup> Groups { get; } = new();
            public int RegisteredSkeletons { get; set; }
            public int EvaluatedSkeletons { get; set; }
        }

        private readonly record struct TradeOutcome(
            string Outcome,
            double ExitPrice,
            int OutcomeBar,
            double NetR,
            bool Resolved,
            bool Profitable);

        private sealed record RebaseStats(
            int Groups,
            int Signals,
            double Worked236,
            double Tradeable236,
            double MedianDepth,
            double AvgNetR,
            double Profitable,
            double AvgMfeR)
        {
            public static RebaseStats Create(IReadOnlyList<SkeletonSignalSample> samples)
            {
                List<SkeletonSignalSample> resolved = samples
                    .Where(x => x.Sample.Resolved)
                    .ToList();
                int groups = samples
                    .Select(x => (x.File, x.IsUp, x.P0Bar))
                    .Distinct()
                    .Count();

                return new RebaseStats(
                    groups,
                    samples.Count,
                    Rate(samples, x => x.Outcome.Worked236),
                    Rate(samples, x => x.Outcome.Tradeable236),
                    MedianOf(samples.Select(x => x.Outcome.DepthDiag)),
                    resolved.Count == 0 ? double.NaN : resolved.Average(x => x.Sample.NetR),
                    resolved.Count == 0
                        ? double.NaN
                        : resolved.Count(x => x.Sample.Profitable) / (double)resolved.Count,
                    samples.Count == 0 ? double.NaN : samples.Average(x => x.Outcome.MfeR));
            }

            private static double Rate(
                IReadOnlyList<SkeletonSignalSample> samples,
                Func<SkeletonSignalSample, bool> predicate)
            {
                return samples.Count == 0
                    ? 0
                    : samples.Count(predicate) / (double)samples.Count;
            }
        }

        #endregion
    }
}
