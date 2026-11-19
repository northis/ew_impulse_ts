using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Phase 0 of the "diagonal → impulse" research (DIAGONAL.md §9.40): an exact h1 replay of
    /// the widened-stop cells that the post-hoc TP/SL grid liked. The post-hoc h1 grid was
    /// positive on `R50–61.8` with `k1.50–2.00`, but the exact m15 run collapsed to +0.027R,
    /// and the h1 exact run was never made. This test closes that gap: the real finder is run
    /// with `TakeProfitRetraceRatio` and `StopWave3Ratio`, so its own `tpBehindEntry`,
    /// `riskTooSmall`, `tpSlHit` and TP/SL events decide the sample.
    /// <para>
    /// Both initial modes are replayed: `ALL` is the baseline, `ENDING` adds D-INIT-END and
    /// D-INIT-SHIFT. 8 tuning + 8 fresh h1 pairs, exact corridor (`W2/W1 >= 0.5`,
    /// `W4/W3 <= 0.382`), 2 bp costs. Writes
    /// <c>reports/diagonal_stop_widen_h1.md</c> and
    /// <c>reports/diagonal_stop_widen_h1_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalStopWidenH1"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string STOP_WIDEN_REPORT = "diagonal_stop_widen_h1.md";
        private const string STOP_WIDEN_CSV = "diagonal_stop_widen_h1_samples.csv";

        private static readonly StopWidenCell[] STOP_WIDEN_CELLS =
        {
            new("R23.6 / k1.00", 0.236d, 1.00d),
            new("R23.6 / k1.25", 0.236d, 1.25d),
            new("R38.2 / k1.25", 0.382d, 1.25d),
            new("R50.0 / k1.25", 0.500d, 1.25d),
            new("R50.0 / k1.50", 0.500d, 1.50d),
            new("R50.0 / k2.00", 0.500d, 2.00d),
            new("R61.8 / k1.25", 0.618d, 1.25d),
            new("R61.8 / k1.50", 0.618d, 1.50d),
            new("R61.8 / k2.00", 0.618d, 2.00d)
        };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_stop_widen_h1.md")]
        [Category("Research")]
        public void DiagonalStopWidenH1_Report()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            // Smoke-test knob: one archive file instead of the full 16-pair replay.
            string? onlyFile = Environment.GetEnvironmentVariable("STOP_WIDEN_FILE");
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

            var modes = new[] { DiagonalInitialMode.ALL, DiagonalInitialMode.ENDING };
            StopWidenCell[] cells = STOP_WIDEN_CELLS;
            var runs = new List<StopWidenRun>();

            // Smoke knob: one mode and two cells, to validate the report before the full grid.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STOP_WIDEN_SMOKE")))
            {
                modes = new[] { DiagonalInitialMode.ALL };
                cells = new[]
                {
                    new StopWidenCell("R23.6 / k1.00", 0.236d, 1.00d),
                    new StopWidenCell("R50.0 / k2.00", 0.500d, 2.00d)
                };
            }

            foreach (DiagonalInitialMode mode in modes)
            {
                foreach (StopWidenCell cell in cells)
                {
                    DiagonalRunConfig config = EXACT_CORRIDOR with
                    {
                        Name = $"{mode} / {cell.Name}",
                        InitialMode = mode,
                        TakeProfitRetraceRatio = cell.Retrace,
                        StopWave3Ratio = cell.Stop
                    };

                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var run = new StopWidenRun(mode, cell);
                    RunStopWidenSet(dataDir, config, tuningFiles, run.Tuning);
                    RunStopWidenSet(dataDir, config, freshFiles, run.Fresh);
                    runs.Add(run);

                    TestContext.Out.WriteLine(
                        $"{run.Name}: tuning={run.Tuning.Samples.Count}, " +
                        $"fresh={run.Fresh.Samples.Count}, {watch.Elapsed.TotalSeconds:F0}s");
                }
            }

            Assert.That(runs.Sum(r => r.Tuning.Samples.Count + r.Fresh.Samples.Count),
                Is.GreaterThan(0), "The exact replay produced no signals.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, STOP_WIDEN_REPORT);
            File.WriteAllText(reportPath, BuildStopWidenReport(
                runs, tuningFiles.Count, freshFiles.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, STOP_WIDEN_CSV);
            File.WriteAllText(csvPath, BuildStopWidenCsv(runs));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            foreach (StopWidenRun run in runs)
            {
                foreach (Sample sample in run.Tuning.Samples.Concat(run.Fresh.Samples))
                    sample.Provider = null!;
            }
        }

        #region Replay

        private static void RunStopWidenSet(
            string dataDir,
            DiagonalRunConfig config,
            IReadOnlyList<string> files,
            StopWidenSet set)
        {
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 12 },
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

        #endregion

        #region Report

        private static string BuildStopWidenReport(
            IReadOnlyList<StopWidenRun> runs,
            int tuningFileCount,
            int freshFileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — exact h1 replay of the widened stop");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(
                $"Files: {tuningFileCount} tuning + {freshFileCount} fresh h1 pairs. Exact corridor " +
                "(`W2/W1 >= 0.5`, `W4/W3 <= 0.382`), 2 bp costs, stop-first on a tie. The target is a " +
                "retrace of the diagonal (`R`), the stop is `V4 ± k·|W3|`. Both modes are exact " +
                "finder replays, not a post-hoc level grid.");
            builder.AppendLine();
            builder.AppendLine("Reference points: the post-hoc h1 grid gave fresh test " +
                               "**+0.262…+0.338R** for `R50–61.8 / k1.50–2.00` (88 trades), while the " +
                               "exact m15 run of `R50 / k2.00` gave only **+0.027R** on the fresh " +
                               "test. This table is the missing h1 exact check.");
            builder.AppendLine();

            AppendStopWidenMainTable(builder, runs);
            AppendStopWidenSplitTable(builder, runs);
            AppendStopWidenPairTable(builder, runs);
            AppendStopWidenYearTable(builder, runs);
            AppendStopWidenVerdict(builder, runs);

            return builder.ToString();
        }

        private static void AppendStopWidenMainTable(
            StringBuilder builder,
            IReadOnlyList<StopWidenRun> runs)
        {
            builder.AppendLine("## Main results");
            builder.AppendLine();
            builder.AppendLine("| Mode / cell | Set | Signals | Resolved | profitable | avg netR | avg R:R | worked236 | worked382 |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (StopWidenRun run in runs)
            {
                foreach ((string set, StopWidenSet data) in new[]
                         {
                             ("tuning", run.Tuning), ("fresh", run.Fresh)
                         })
                {
                    Phase1Stats stats = Stats(data.Samples);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Name} | {set} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {AverageRr(data.Samples):F2} | {stats.Worked236:P1} | {stats.Worked382:P1} |"));
                }
            }
        }

        private static void AppendStopWidenSplitTable(
            StringBuilder builder,
            IReadOnlyList<StopWidenRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Train / test split");
            builder.AppendLine();
            builder.AppendLine("Train = entries in 2017–2022, test = 2023–2026. The same split as " +
                               "the execution report; a cell must be positive on both halves to count.");
            builder.AppendLine();
            builder.AppendLine("| Mode / cell | Set | Train N | Train netR | Test N | Test netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");

            foreach (StopWidenRun run in runs)
            {
                foreach ((string set, StopWidenSet data) in new[]
                         {
                             ("tuning", run.Tuning), ("fresh", run.Fresh)
                         })
                {
                    Phase1Stats train = Stats(data.Samples
                        .Where(s => s.EntryTime.Year <= 2022).ToList());
                    Phase1Stats test = Stats(data.Samples
                        .Where(s => s.EntryTime.Year >= 2023).ToList());
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Name} | {set} | {train.N} | {train.AvgNetR:F3} | {test.N} | {test.AvgNetR:F3} |"));
                }
            }
        }

        /// <summary>
        /// The three region cells with the best fresh-test netR per mode, plus the base cell.
        /// A compact pair table is more useful than 16 pairs × 18 configs.
        /// </summary>
        private static void AppendStopWidenPairTable(
            StringBuilder builder,
            IReadOnlyList<StopWidenRun> runs)
        {
            builder.AppendLine();
            builder.AppendLine("## Fresh test by pair");
            builder.AppendLine();
            builder.AppendLine("Only the base cell and the three best region cells per mode " +
                               "(by fresh-test netR). `—` means no resolved signal.");
            builder.AppendLine();

            foreach (DiagonalInitialMode mode in new[]
                     {
                         DiagonalInitialMode.ALL, DiagonalInitialMode.ENDING
                     })
            {
                List<StopWidenRun> allModeRuns = runs
                    .Where(r => r.Mode == mode)
                    .ToList();
                if (allModeRuns.Count == 0)
                    continue;

                List<StopWidenRun> modeRuns = allModeRuns
                    .Where(r => r.Cell.Name != "R23.6 / k1.00")
                    .OrderByDescending(r => FreshTestStats(r).AvgNetR)
                    .Take(3)
                    .ToList();
                StopWidenRun baseRun = allModeRuns.First(r =>
                    r.Cell.Name == "R23.6 / k1.00");

                List<StopWidenRun> shown = new[] { baseRun }.Concat(modeRuns).ToList();
                builder.AppendLine(
                    "| Pair | " + string.Join(" | ", shown.Select(r => r.Cell.Name)) + " |");
                builder.AppendLine("|---|" + string.Concat(shown.Select(_ => "---:|")));

                IEnumerable<string> symbols = shown
                    .SelectMany(r => r.Fresh.Samples)
                    .Select(s => s.Symbol)
                    .Distinct()
                    .OrderBy(s => s, StringComparer.Ordinal);

                foreach (string symbol in symbols)
                {
                    var cells = shown.Select(r =>
                    {
                        List<Sample> pair = r.Fresh.Samples
                            .Where(s => s.Symbol == symbol && s.EntryTime.Year >= 2023)
                            .ToList();
                        List<Sample> resolved = pair.Where(s => s.Resolved).ToList();
                        return resolved.Count == 0
                            ? "—"
                            : resolved.Average(s => s.NetR)
                                .ToString("F3", CultureInfo.InvariantCulture);
                    });
                    builder.AppendLine($"| {symbol} | " + string.Join(" | ", cells) + " |");
                }

                builder.AppendLine();
            }
        }

        private static void AppendStopWidenYearTable(
            StringBuilder builder,
            IReadOnlyList<StopWidenRun> runs)
        {
            builder.AppendLine("## Fresh test by year");
            builder.AppendLine();
            builder.AppendLine("The same base + three best region cells per mode, fresh pairs only.");
            builder.AppendLine();

            foreach (DiagonalInitialMode mode in new[]
                     {
                         DiagonalInitialMode.ALL, DiagonalInitialMode.ENDING
                     })
            {
                List<StopWidenRun> allModeRuns = runs
                    .Where(r => r.Mode == mode)
                    .ToList();
                if (allModeRuns.Count == 0)
                    continue;

                List<StopWidenRun> modeRuns = allModeRuns
                    .Where(r => r.Cell.Name != "R23.6 / k1.00")
                    .OrderByDescending(r => FreshTestStats(r).AvgNetR)
                    .Take(3)
                    .ToList();
                StopWidenRun baseRun = allModeRuns.First(r =>
                    r.Cell.Name == "R23.6 / k1.00");
                List<StopWidenRun> shown = new[] { baseRun }.Concat(modeRuns).ToList();

                builder.AppendLine(
                    "| Year | " + string.Join(" | ", shown.Select(r => r.Cell.Name)) + " |");
                builder.AppendLine("|---|" + string.Concat(shown.Select(_ => "---:|")));

                IEnumerable<int> years = shown
                    .SelectMany(r => r.Fresh.Samples)
                    .Where(s => s.EntryTime.Year >= 2023)
                    .Select(s => s.EntryTime.Year)
                    .Distinct()
                    .OrderBy(y => y);

                foreach (int year in years)
                {
                    var cells = shown.Select(r =>
                    {
                        List<Sample> slice = r.Fresh.Samples
                            .Where(s => s.EntryTime.Year == year && s.Resolved)
                            .ToList();
                        return slice.Count == 0
                            ? "—"
                            : slice.Average(s => s.NetR)
                                .ToString("F3", CultureInfo.InvariantCulture);
                    });
                    builder.AppendLine($"| {year} | " + string.Join(" | ", cells) + " |");
                }

                builder.AppendLine();
            }
        }

        private static void AppendStopWidenVerdict(
            StringBuilder builder,
            IReadOnlyList<StopWidenRun> runs)
        {
            builder.AppendLine("## Verdict");
            builder.AppendLine();

            foreach (DiagonalInitialMode mode in new[]
                     {
                         DiagonalInitialMode.ALL, DiagonalInitialMode.ENDING
                     })
            {
                List<StopWidenRun> modeRuns = runs.Where(r => r.Mode == mode).ToList();
                if (modeRuns.Count == 0)
                    continue;

                StopWidenRun baseRun = modeRuns.First(r => r.Cell.Name == "R23.6 / k1.00");
                Phase1Stats baseTuningTest = TestStats(baseRun.Tuning);
                Phase1Stats baseFreshTest = TestStats(baseRun.Fresh);

                List<StopWidenRun> bothPositive = modeRuns
                    .Where(r =>
                        TestStats(r.Tuning).AvgNetR > 0 &&
                        TestStats(r.Fresh).AvgNetR > 0)
                    .ToList();

                StopWidenRun bestFresh = modeRuns
                    .OrderByDescending(r => TestStats(r.Fresh).AvgNetR)
                    .First();
                StopWidenRun bestTuning = modeRuns
                    .OrderByDescending(r => TestStats(r.Tuning).AvgNetR)
                    .First();

                builder.AppendLine(FormattableString.Invariant(
                    $"**{mode}.** Base `R23.6 / k1.00`: tuning test {baseTuningTest.AvgNetR:F3}R ({baseTuningTest.N}), fresh test {baseFreshTest.AvgNetR:F3}R ({baseFreshTest.N}). Best tuning test: `{bestTuning.Cell.Name}` {TestStats(bestTuning.Tuning).AvgNetR:F3}R; best fresh test: `{bestFresh.Cell.Name}` {TestStats(bestFresh.Fresh).AvgNetR:F3}R. Cells positive on **both** tuning test and fresh test: {bothPositive.Count} of {modeRuns.Count}{(bothPositive.Count == 0 ? "." : ": " + string.Join(", ", bothPositive.Select(r => $"`{r.Cell.Name}` ({TestStats(r.Tuning).AvgNetR:F3} / {TestStats(r.Fresh).AvgNetR:F3})")) + ".")}"));
                builder.AppendLine();
            }

            builder.AppendLine("**How to read this.** The post-hoc grid is not a backtest: it " +
                               "reuses the finder's entry bar and only replaces SL/TP, so its " +
                               "`tpBehindEntry`, `riskTooSmall` and `tpSlHit` gates never see the " +
                               "new levels. A cell that is positive here has survived the real " +
                               "finder. If the region is not positive on both halves of the h1 " +
                               "sample, the widened-stop direction is closed and the next step is " +
                               "the diagonal → impulse harness (Phase 1).");
        }

        private static Phase1Stats TestStats(StopWidenSet set)
        {
            return Stats(set.Samples.Where(s => s.EntryTime.Year >= 2023).ToList());
        }

        private static Phase1Stats FreshTestStats(StopWidenRun run)
        {
            return TestStats(run.Fresh);
        }

        #endregion

        #region Csv

        private static string BuildStopWidenCsv(IReadOnlyList<StopWidenRun> runs)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "config,mode,set,file,symbol,entry_time,entry,risk,sl,tp,outcome,net_r," +
                "resolved,profitable,worked236,worked382,worked50,mfe_diag,mfe_r,mae_r,rr," +
                "w2_retr,w3_w1,w4_w3,w4_w2,w5_w3,a5_early,bars_w5");

            foreach (StopWidenRun run in runs)
            {
                foreach ((string set, StopWidenSet data) in new[]
                         {
                             ("tuning", run.Tuning), ("fresh", run.Fresh)
                         })
                {
                    foreach (Sample s in data.Samples
                                 .OrderBy(s => s.Symbol, StringComparer.Ordinal)
                                 .ThenBy(s => s.EntryTime))
                    {
                        builder.AppendLine(string.Join(",",
                            Csv(run.Cell.Name),
                            Csv(run.Mode.ToString()),
                            Csv(set),
                            Csv(s.File),
                            Csv(s.Symbol),
                            Csv(s.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
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
                            s.BarsW5.ToString(CultureInfo.InvariantCulture)));
                    }
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Types

        private sealed record StopWidenCell(string Name, double Retrace, double Stop);

        private sealed class StopWidenSet
        {
            public List<Sample> Samples { get; } = new();
            public Dictionary<string, int> Diag { get; } = new();
        }

        private sealed class StopWidenRun
        {
            public StopWidenRun(DiagonalInitialMode mode, StopWidenCell cell)
            {
                Mode = mode;
                Cell = cell;
            }

            public DiagonalInitialMode Mode { get; }
            public StopWidenCell Cell { get; }
            public StopWidenSet Tuning { get; } = new();
            public StopWidenSet Fresh { get; } = new();
            public string Name => $"{Mode} / {Cell.Name}";
        }

        #endregion
    }
}
