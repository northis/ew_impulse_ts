using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research: only ENDING diagonals, with and without the wave-5 throw-over (DIAGONAL.md
    /// §9.39). §9.35 found that the "ugly" throw-over group is where the wait-rule edge lives,
    /// and §9.36 only had a post-hoc screen — a filter applied to an existing sample, which
    /// does not rebuild candidates or re-run the dedup. Now that ENDING also applies
    /// D-INIT-SHIFT (§4.8), the question is asked again with the real gates:
    /// <list type="bullet">
    /// <item><c>ENDING / spill off</c> — the new ending set without a wave-5 shape filter;</item>
    /// <item><c>ENDING / ONLY_SPILL</c> — throw-over only (the §9.35 hypothesis);</item>
    /// <item><c>ENDING / EXCLUDE_SPILL</c> — clean wedge only (the "beautiful model" hypothesis);</item>
    /// <item><c>ALL / spill off</c> — the exact-corridor baseline, unchanged by §4.8.</item>
    /// </list>
    /// Exact corridor, 23.6% target, 16 h1 pairs (8 tuning + 8 fresh). Writes
    /// <c>reports/diagonal_ending_spill.md</c> and
    /// <c>reports/diagonal_ending_spill_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalEndingSpill"</c>.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string ENDING_SPILL_REPORT = "diagonal_ending_spill.md";
        private const string ENDING_SPILL_CSV = "diagonal_ending_spill_samples.csv";

        /// <summary>Gates shown in the funnel table, in report order.</summary>
        private static readonly string[] ENDING_SPILL_GATES =
        {
            "entered",
            "endingHasLeadingPoint0",
            "initialDiagonal",
            "endingCorrectionTooShallow",
            "notInitialDiagonal",
            "spillTooSmall",
            "w5SpillsOutOfWedge",
            "duplicateSkeleton",
            "duplicatePoint0",
            "ratioTooLow",
            "tpBehindEntry",
            "riskTooSmall",
            "tpSlHit",
            "thinMarketWindow"
        };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_ending_spill.md")]
        [Category("Research")]
        public void DiagonalEndingSpill_Report()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            // Smoke-test knob: set ENDING_SPILL_FILE to one archive name to run a single file
            // without touching the full 16-file replay.
            string? onlyFile = Environment.GetEnvironmentVariable("ENDING_SPILL_FILE");
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

            var configs = new[]
            {
                EXACT_CORRIDOR with
                {
                    Name = "ALL / spill off"
                },
                EXACT_CORRIDOR with
                {
                    Name = "ENDING / spill off",
                    InitialMode = DiagonalInitialMode.ENDING
                },
                EXACT_CORRIDOR with
                {
                    Name = "ENDING / throw-over",
                    InitialMode = DiagonalInitialMode.ENDING,
                    Wave5SpillMode = DiagonalWave5SpillMode.ONLY_SPILL
                },
                EXACT_CORRIDOR with
                {
                    Name = "ENDING / clean wedge",
                    InitialMode = DiagonalInitialMode.ENDING,
                    Wave5SpillMode = DiagonalWave5SpillMode.EXCLUDE_SPILL
                }
            };

            var runs = new List<EndingSpillRun>();
            foreach (DiagonalRunConfig config in configs)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                EndingSpillRun run = RunEndingSpillConfig(dataDir, config, tuningFiles, freshFiles);
                runs.Add(run);
                TestContext.Out.WriteLine(
                    $"{config.Name}: tuning={run.Tuning.Samples.Count}, " +
                    $"fresh={run.Fresh.Samples.Count}, {watch.Elapsed.TotalSeconds:F0}s");
            }

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, ENDING_SPILL_REPORT);
            File.WriteAllText(reportPath, BuildEndingSpillReport(
                runs, tuningFiles.Count, freshFiles.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, ENDING_SPILL_CSV);
            File.WriteAllText(csvPath, BuildEndingSpillCsv(runs));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            // Providers are heavy; do not keep them alive through the NUnit fixture.
            foreach (EndingSpillRun run in runs)
            {
                foreach (Sample sample in run.Tuning.Samples.Concat(run.Fresh.Samples))
                    sample.Provider = null!;
            }
        }

        #region Replay

        private static EndingSpillRun RunEndingSpillConfig(
            string dataDir,
            DiagonalRunConfig config,
            IReadOnlyList<string> tuningFiles,
            IReadOnlyList<string> freshFiles)
        {
            var run = new EndingSpillRun(config);
            RunEndingSpillSet(dataDir, config, tuningFiles, run.Tuning);
            RunEndingSpillSet(dataDir, config, freshFiles, run.Fresh);
            return run;
        }

        private static void RunEndingSpillSet(
            string dataDir,
            DiagonalRunConfig config,
            IReadOnlyList<string> files,
            EndingSpillSet set)
        {
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                file =>
                {
                    var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
                    provider.LoadCandles(Path.Combine(dataDir, file));

                    Dictionary<string, int> diag = null!;
                    List<Sample> samples = RunDiagonalSamples(
                        provider, file, config,
                        onFinished: finder =>
                            diag = new Dictionary<string, int>(finder.Diag));

                    lock (sync)
                    {
                        set.Samples.AddRange(samples);
                        MergeDiag(set.Diag, diag);
                    }
                });
        }

        private static void MergeDiag(
            Dictionary<string, int> target,
            IReadOnlyDictionary<string, int> source)
        {
            if (source == null)
                return;

            foreach (KeyValuePair<string, int> pair in source)
                target[pair.Key] = target.GetValueOrDefault(pair.Key) + pair.Value;
        }

        #endregion

        #region Report

        private static string BuildEndingSpillReport(
            IReadOnlyList<EndingSpillRun> runs,
            int tuningFileCount,
            int freshFileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — ending only: with and without the wave-5 throw-over");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(
                $"Files: {tuningFileCount} tuning + {freshFileCount} fresh h1 pairs. Exact corridor " +
                "(`W2/W1 >= 0.5`, `W4/W3 <= 0.382`, `W4 >= 0`, `W4/W2 level >= 0`), 23.6% diagonal " +
                "target, 2 bp costs. Every row is a real finder replay: the gates are applied " +
                "before the signal and wave 5 is tracked on raw bars. `ENDING` now also applies " +
                "D-INIT-SHIFT (§4.8), so a wedge whose point 0 can be moved left into a leading " +
                "reading is not emitted.");
            builder.AppendLine();
            builder.AppendLine("`throw-over` = `ONLY_SPILL` (D-INSIDE-5 area ratio at the signal " +
                               "bar >= 0.005); `clean wedge` = `EXCLUDE_SPILL` (the same measure " +
                               "<= 0.005). `worked236/382` is the correction from the running " +
                               "wave-5 extreme before the theoretical invalidation; `netR` is the " +
                               "actual finder TP/SL minus costs.");
            builder.AppendLine();

            AppendEndingSpillMainTable(builder, runs);
            AppendEndingSpillGateEffect(builder, runs);
            AppendEndingSpillFunnelTable(builder, runs);
            AppendEndingSpillPairTables(builder, runs);
            AppendEndingSpillPeriodTable(builder, runs);
            AppendEndingSpillVerdict(builder, runs);

            return builder.ToString();
        }

        private static void AppendEndingSpillMainTable(
            StringBuilder builder,
            IReadOnlyList<EndingSpillRun> runs)
        {
            builder.AppendLine("## Main results");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Signals | Resolved | profitable | avg netR | avg R:R | worked236 | worked382 | median MFE/diag | avg MFE/R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (EndingSpillRun run in runs)
            {
                foreach ((string set, EndingSpillSet data) in new[]
                         {
                             ("tuning", run.Tuning), ("fresh", run.Fresh)
                         })
                {
                    Phase1Stats stats = Stats(data.Samples);
                    double avgRr = AverageRr(data.Samples);
                    double medianMfe = MedianOf(data.Samples.Select(s => s.MfeDiag));
                    double avgMfeR = data.Samples.Count == 0
                        ? 0
                        : data.Samples.Average(s => s.MfeR);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Config.Name} | {set} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {avgRr:F2} | {stats.Worked236:P1} | {stats.Worked382:P1} | {medianMfe:F3} | {avgMfeR:F2} |"));
                }
            }
        }

        /// <summary>
        /// What the ending-only gate itself costs: the ALL baseline vs ENDING with no wave-5
        /// shape filter, plus the D-INIT-SHIFT / D-INIT / D-INIT-END counters.
        /// </summary>
        private static void AppendEndingSpillGateEffect(
            StringBuilder builder,
            IReadOnlyList<EndingSpillRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## What the ending-only gate removes");
            builder.AppendLine();

            EndingSpillRun baseline = runs.First(r => r.Config.Name == "ALL / spill off");
            EndingSpillRun ending = runs.First(r => r.Config.Name == "ENDING / spill off");

            builder.AppendLine("| Set | ALL signals | ENDING signals | kept | `endingHasLeadingPoint0` | `initialDiagonal` | `endingCorrectionTooShallow` |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");

            foreach ((string set, EndingSpillSet all, EndingSpillSet end) in new[]
                     {
                         ("tuning", baseline.Tuning, ending.Tuning),
                         ("fresh", baseline.Fresh, ending.Fresh)
                     })
            {
                double kept = all.Samples.Count == 0
                    ? 0
                    : (double)end.Samples.Count / all.Samples.Count;
                builder.AppendLine(FormattableString.Invariant(
                    $"| {set} | {all.Samples.Count} | {end.Samples.Count} | {kept:P1} | {end.Diag.GetValueOrDefault("endingHasLeadingPoint0")} | {end.Diag.GetValueOrDefault("initialDiagonal")} | {end.Diag.GetValueOrDefault("endingCorrectionTooShallow")} |"));
            }
        }

        private static void AppendEndingSpillFunnelTable(
            StringBuilder builder,
            IReadOnlyList<EndingSpillRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Gate funnel");
            builder.AppendLine();
            builder.AppendLine("Counters are summed over the file set; a candidate can hit the " +
                               "same gate on several candles (the R:R / thin-market waits re-try " +
                               "it), so the funnel is a diagnostic, not a partition.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | " + string.Join(" | ", ENDING_SPILL_GATES) + " |");
            builder.AppendLine("|---|---|" + string.Concat(ENDING_SPILL_GATES.Select(_ => "---:|")));

            foreach (EndingSpillRun run in runs)
            {
                foreach ((string set, EndingSpillSet data) in new[]
                         {
                             ("tuning", run.Tuning), ("fresh", run.Fresh)
                         })
                {
                    builder.AppendLine(
                        $"| {run.Config.Name} | {set} | " +
                        string.Join(" | ", ENDING_SPILL_GATES.Select(g =>
                            data.Diag.GetValueOrDefault(g).ToString(CultureInfo.InvariantCulture))) +
                        " |");
                }
            }
        }

        private static void AppendEndingSpillPairTables(
            StringBuilder builder,
            IReadOnlyList<EndingSpillRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## By pair");
            builder.AppendLine();
            builder.AppendLine("Mean net R among resolved signals; `N` is the signal count. " +
                               "Only the three ending variants are shown.");
            builder.AppendLine();
            builder.AppendLine("| Set | Pair | spill off N | spill off netR | throw-over N | throw-over netR | clean N | clean netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");

            EndingSpillRun off = runs.First(r => r.Config.Name == "ENDING / spill off");
            EndingSpillRun spill = runs.First(r => r.Config.Name == "ENDING / throw-over");
            EndingSpillRun clean = runs.First(r => r.Config.Name == "ENDING / clean wedge");

            foreach ((string set, EndingSpillSet offSet, EndingSpillSet spillSet, EndingSpillSet cleanSet) in new[]
                     {
                         ("tuning", off.Tuning, spill.Tuning, clean.Tuning),
                         ("fresh", off.Fresh, spill.Fresh, clean.Fresh)
                     })
            {
                IEnumerable<string> symbols = offSet.Samples.Select(s => s.Symbol)
                    .Concat(spillSet.Samples.Select(s => s.Symbol))
                    .Concat(cleanSet.Samples.Select(s => s.Symbol))
                    .Distinct()
                    .OrderBy(s => s, StringComparer.Ordinal);

                foreach (string symbol in symbols)
                {
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {set} | {symbol} | {Cell(offSet.Samples, symbol)} | {Cell(spillSet.Samples, symbol)} | {Cell(cleanSet.Samples, symbol)} |"));
                }
            }
        }

        private static string Cell(IReadOnlyList<Sample> samples, string symbol)
        {
            List<Sample> pair = samples.Where(s => s.Symbol == symbol).ToList();
            List<Sample> resolved = pair.Where(s => s.Resolved).ToList();
            if (pair.Count == 0)
                return "0 | —";

            double avg = resolved.Count == 0 ? double.NaN : resolved.Average(s => s.NetR);
            return FormattableString.Invariant(
                $"{pair.Count} | {(double.IsNaN(avg) ? "—" : avg.ToString("F3", CultureInfo.InvariantCulture))}");
        }

        private static void AppendEndingSpillPeriodTable(
            StringBuilder builder,
            IReadOnlyList<EndingSpillRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## By period");
            builder.AppendLine();
            builder.AppendLine("Tuning and fresh pairs combined; `profitable` is the share of " +
                               "resolved signals with net R > 0.");
            builder.AppendLine();
            builder.AppendLine("| Config | Period | N | Resolved | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");

            foreach (EndingSpillRun run in runs)
            {
                List<Sample> all = run.Tuning.Samples.Concat(run.Fresh.Samples).ToList();
                foreach (string period in new[] { "2017–2020", "2021–2023", "2024–2026" })
                {
                    List<Sample> slice = all.Where(s => PeriodOf(s.EntryTime) == period).ToList();
                    if (slice.Count == 0)
                        continue;

                    Phase1Stats stats = Stats(slice);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Config.Name} | {period} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                }
            }
        }

        private static void AppendEndingSpillVerdict(
            StringBuilder builder,
            IReadOnlyList<EndingSpillRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Verdict");
            builder.AppendLine();

            EndingSpillRun baseline = runs.First(r => r.Config.Name == "ALL / spill off");
            EndingSpillRun off = runs.First(r => r.Config.Name == "ENDING / spill off");
            EndingSpillRun spill = runs.First(r => r.Config.Name == "ENDING / throw-over");
            EndingSpillRun clean = runs.First(r => r.Config.Name == "ENDING / clean wedge");

            Phase1Stats baselineTuning = Stats(baseline.Tuning.Samples);
            Phase1Stats baselineFresh = Stats(baseline.Fresh.Samples);
            Phase1Stats offTuning = Stats(off.Tuning.Samples);
            Phase1Stats offFresh = Stats(off.Fresh.Samples);
            Phase1Stats spillTuning = Stats(spill.Tuning.Samples);
            Phase1Stats spillFresh = Stats(spill.Fresh.Samples);
            Phase1Stats cleanTuning = Stats(clean.Tuning.Samples);
            Phase1Stats cleanFresh = Stats(clean.Fresh.Samples);

            builder.AppendLine(FormattableString.Invariant(
                $"1. **The ending-only gate keeps {Ratio(offTuning.N, baselineTuning.N):P0} of the baseline on tuning ({offTuning.N} of {baselineTuning.N}) and {Ratio(offFresh.N, baselineFresh.N):P0} on fresh ({offFresh.N} of {baselineFresh.N}).** `endingHasLeadingPoint0` fires {off.Tuning.Diag.GetValueOrDefault("endingHasLeadingPoint0")} times on tuning and {off.Fresh.Diag.GetValueOrDefault("endingHasLeadingPoint0")} on fresh, so D-INIT-SHIFT is a real part of the ending set, not a corner case."));

            builder.AppendLine(FormattableString.Invariant(
                $"2. **Throw-over vs spill off.** Tuning: {spillTuning.N} vs {offTuning.N} signals, profitable {spillTuning.Profitable:P1} vs {offTuning.Profitable:P1}, avg netR {spillTuning.AvgNetR:F3} vs {offTuning.AvgNetR:F3}. Fresh: {spillFresh.N} vs {offFresh.N}, profitable {spillFresh.Profitable:P1} vs {offFresh.Profitable:P1}, avg netR {spillFresh.AvgNetR:F3} vs {offFresh.AvgNetR:F3}."));

            builder.AppendLine(FormattableString.Invariant(
                $"3. **Clean wedge vs spill off.** Tuning: {cleanTuning.N} vs {offTuning.N} signals, profitable {cleanTuning.Profitable:P1} vs {offTuning.Profitable:P1}, avg netR {cleanTuning.AvgNetR:F3} vs {offTuning.AvgNetR:F3}. Fresh: {cleanFresh.N} vs {offFresh.N}, profitable {cleanFresh.Profitable:P1} vs {offFresh.Profitable:P1}, avg netR {cleanFresh.AvgNetR:F3} vs {offFresh.AvgNetR:F3}."));

            int tuningWins = 0;
            int freshWins = 0;
            if (spillTuning.AvgNetR > offTuning.AvgNetR) tuningWins++;
            else tuningWins--;
            if (spillFresh.AvgNetR > offFresh.AvgNetR) freshWins++;
            else freshWins--;

            builder.AppendLine(FormattableString.Invariant(
                $"4. **Sign consistency.** Throw-over beats spill off on tuning ({(tuningWins > 0 ? "yes" : "no")}) and on fresh ({(freshWins > 0 ? "yes" : "no")}). Both sets must agree before the filter can be called an edge; a split sign is a sample-specific artefact."));

            builder.AppendLine();
            builder.AppendLine("**How to read this.** A wave-5 shape filter changes *which* wedges " +
                               "are traded, so the comparison is only meaningful when the signal " +
                               "counts stay in the same ballpark. If a variant keeps fewer than " +
                               "half of the ending signals and the netR improvement is inside " +
                               "±0.05R, it is a sample-size effect, not a filter worth shipping. " +
                               "The `ALL / spill off` row is the unchanged exact-corridor baseline " +
                               "and is not expected to move: §4.8 lives only in `ENDING`.");

            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Baseline check: `ALL` tuning {baselineTuning.N} signals / {baselineTuning.AvgNetR:F3}R, fresh {baselineFresh.N} / {baselineFresh.AvgNetR:F3}R. These numbers must match the exact-run report (§9.20) because the spill filter and the initial mode are both off."));
        }

        private static double AverageRr(IReadOnlyList<Sample> samples)
        {
            List<Sample> resolved = samples.Where(s => s.Resolved).ToList();
            return resolved.Count == 0
                ? 0
                : resolved.Average(s => Math.Abs(s.Tp - s.Entry) / Math.Max(1e-12, s.Risk));
        }

        private static double Ratio(int part, int total)
        {
            return total == 0 ? 0 : (double)part / total;
        }

        #endregion

        #region Csv

        private static string BuildEndingSpillCsv(IReadOnlyList<EndingSpillRun> runs)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "config,set,file,symbol,timeframe,entry_time,signal_bar,is_up,entry,risk,sl,tp," +
                "outcome,net_r,resolved,profitable,worked236,worked382,worked50,final236," +
                "mfe_diag,mfe_r,mae_r,rr,w2_retr,w3_w1,w4_w3,w4_w2,w5_w3," +
                "spill5_early,line13_dist_w3,bars_w5,horizon");

            foreach (EndingSpillRun run in runs)
            {
                foreach ((string set, EndingSpillSet data) in new[]
                         {
                             ("tuning", run.Tuning), ("fresh", run.Fresh)
                         })
                {
                    foreach (Sample s in data.Samples
                                 .OrderBy(s => s.Symbol, StringComparer.Ordinal)
                                 .ThenBy(s => s.EntryTime))
                    {
                        builder.AppendLine(string.Join(",",
                            Csv(run.Config.Name),
                            Csv(set),
                            Csv(s.File),
                            Csv(s.Symbol),
                            Csv(s.TimeFrame),
                            Csv(s.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
                            s.SignalBar.ToString(CultureInfo.InvariantCulture),
                            B(s.IsUpDiagonal),
                            F(s.Entry),
                            F(s.Risk),
                            F(s.Sl),
                            F(s.Tp),
                            Csv(s.Outcome),
                            F(s.NetR),
                            B(s.Resolved),
                            B(s.Profitable),
                            B(s.Worked236),
                            B(s.Worked382),
                            B(s.Worked50),
                            B(s.Final236),
                            F(s.MfeDiag),
                            F(s.MfeR),
                            F(s.MaeR),
                            F(Math.Abs(s.Tp - s.Entry) / Math.Max(1e-12, s.Risk)),
                            F(s.W2Retr),
                            F(s.W3W1),
                            F(s.W4W3),
                            F(s.W4W2),
                            F(s.W5W3),
                            F(s.A5Early),
                            F(s.LineDistanceW3),
                            s.BarsW5.ToString(CultureInfo.InvariantCulture),
                            s.Horizon.ToString(CultureInfo.InvariantCulture)));
                    }
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Types

        private sealed class EndingSpillSet
        {
            public List<Sample> Samples { get; } = new();
            public Dictionary<string, int> Diag { get; } = new();
        }

        private sealed class EndingSpillRun
        {
            public EndingSpillRun(DiagonalRunConfig config)
            {
                Config = config;
            }

            public DiagonalRunConfig Config { get; }
            public EndingSpillSet Tuning { get; } = new();
            public EndingSpillSet Fresh { get; } = new();
        }

        #endregion
    }
}
