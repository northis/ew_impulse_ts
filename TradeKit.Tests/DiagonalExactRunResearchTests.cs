using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Exact-run validation of the Phase 1 candidate (DIAGONAL.md §9.20): the finder is
    /// replayed with the real gates <c>W2/W1 >= 0.5</c> and an optional
    /// <c>W4/W3 &lt;= 0.382</c> corridor, with the 23.6% diagonal target. This is not a
    /// post-hoc filter on the Phase 0 sample: the candidate pool, wave-5 tracking,
    /// <c>tpBehindEntry</c> and deduplication all run with the selected settings.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_exact_run.md")]
        [Category("Research")]
        public void DiagonalExactRun_W2W4_Report()
        {
            string dataDir = FindDataDir();
            var files = new List<string>();

            foreach (string file in H1_FILES.Concat(M15_FILES))
            {
                if (File.Exists(Path.Combine(dataDir, file)))
                    files.Add(file);
                else
                    TestContext.Out.WriteLine($"SKIP (missing): {file}");
            }

            Assert.That(files, Is.Not.Empty, "No archive file was found under data/.");

            var configs = new[]
            {
                new ExactConfig("W2/W1 >= 0.5 + 23.6%", 0.5d, 0d),
                new ExactConfig("W2/W1 >= 0.5 & W4/W3 <= 0.382 + 23.6%", 0.5d, 0.382d)
            };

            var results = new List<(ExactConfig Config, List<Sample> Samples)>();
            foreach (ExactConfig config in configs)
            {
                List<Sample> samples = RunExactAll(dataDir, files, config);
                results.Add((config, samples));
                TestContext.Out.WriteLine(
                    $"{config.Name}: signals={samples.Count}, " +
                    $"worked236={samples.Count(s => s.Worked236)}");
            }

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_exact_run.md");
            File.WriteAllText(reportPath, BuildExactReport(results, files.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static List<Sample> RunExactAll(
            string dataDir, IReadOnlyList<string> files, ExactConfig config,
            Dictionary<string, int>? diagSink = null, int maxDegreeOfParallelism = 4,
            Dictionary<DateTime, int>? repriceSink = null)
        {
            var samples = new List<Sample>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                file =>
                {
                    List<Sample> local = RunExactFile(
                        Path.Combine(dataDir, file), file, config, diagSink, repriceSink);
                    lock (sync)
                    {
                        samples.AddRange(local);
                    }
                });

            return samples;
        }

        private static List<Sample> RunExactFile(string path, string fileName, ExactConfig config,
            Dictionary<string, int>? diagSink = null,
            Dictionary<DateTime, int>? repriceSink = null)
        {
            ITimeFrame timeFrame = fileName.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;

            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(path);

            var finder = new DiagonalSetupFinder(
                provider,
                provider.BarSymbol,
                new EWParams(0, 0.1, 10),
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
                maxWave4RetraceW3: config.MaxWave4RetraceW3,
                requireWave4Shorter: true,
                requireWave2Shorter: false,
                minWave2Retrace: config.MinWave2Retrace,
                maxWave2Retrace: 0,
                maxWave5SpillRatio: 0,
                minRiskRewardRatio: config.MinRiskRewardRatio,
                triggerMode: config.TriggerMode);

            var records = new List<SignalRecord>();
            SignalRecord? pending = null;

            finder.OnEnter += (_, args) =>
            {
                pending = new SignalRecord(args);
                records.Add(pending);
            };
            finder.OnTakeProfit += (_, args) =>
            {
                if (pending != null)
                {
                    pending.Outcome = "TP";
                    pending.ExitPrice = args.Level.Value;
                    pending.OutcomeBar = args.Level.BarIndex;
                    pending = null;
                }
            };
            finder.OnStopLoss += (_, args) =>
            {
                if (pending != null)
                {
                    pending.Outcome = "SL";
                    pending.ExitPrice = args.Level.Value;
                    pending.OutcomeBar = args.Level.BarIndex;
                    pending = null;
                }
            };

            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            if (diagSink != null)
            {
                lock (diagSink)
                {
                    foreach (KeyValuePair<string, int> pair in finder.Diag)
                        diagSink[pair.Key] = diagSink.GetValueOrDefault(pair.Key) + pair.Value;
                }
            }

            if (repriceSink != null)
            {
                lock (repriceSink)
                {
                    foreach (KeyValuePair<DateTime, int> pair in finder.PendingRepriceCounts)
                        repriceSink[pair.Key] = pair.Value;
                }
            }

            string symbol = fileName[..fileName.IndexOf('_')];
            string tf = fileName.Contains("_m15_") ? "m15" : "h1";
            var samples = new List<Sample>(records.Count);
            foreach (SignalRecord record in records)
            {
                Sample? sample = BuildSample(provider, finder, record, fileName, symbol, tf);
                if (sample == null)
                    continue;

                if (repriceSink != null)
                {
                    sample.RepriceCount = repriceSink.GetValueOrDefault(
                        record.Args.Level.OpenTime, -1);
                }

                samples.Add(sample);
            }

            return samples;
        }

        private static string BuildExactReport(
            IReadOnlyList<(ExactConfig Config, List<Sample> Samples)> results, int fileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal exact run — W2/W4 corridor + 23.6% TP");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Files: {fileCount}. Every row is an exact finder replay: the gates are applied before the signal, wave 5 is tracked on raw bars, and TP/SL are the finder's own events. The 23.6% target is the Phase 1 candidate; the W4 upper bound is the new research gate `maxWave4RetraceW3` (0 disables it)."));
            builder.AppendLine();

            builder.AppendLine("## Configs");
            builder.AppendLine();
            builder.AppendLine("| Config | Signals | worked236 | worked382 | resolved | profitable | avg netR | avg R:R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach ((ExactConfig config, List<Sample> samples) in results)
            {
                Phase1Stats stats = Stats(samples);
                List<Sample> resolved = samples.Where(s => s.Resolved).ToList();
                double avgRr = resolved.Count == 0
                    ? 0
                    : resolved.Average(s => Math.Abs(s.Tp - s.Entry) / Math.Max(1e-12, s.Risk));
                builder.AppendLine(FormattableString.Invariant(
                    $"| {config.Name} | {stats.N} | {stats.Worked236:P1} | {stats.Worked382:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {avgRr:F2} |"));
            }

            if (results.Count == 0)
                return builder.ToString();

            (ExactConfig comboConfig, List<Sample> combo) = results[^1];
            builder.AppendLine();
            builder.AppendLine("## Exact combo by timeframe");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant($"Config: **{comboConfig.Name}**."));
            builder.AppendLine();
            builder.AppendLine("| Timeframe | Signals | worked236 | worked382 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in combo
                         .GroupBy(s => s.TimeFrame)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Phase1Stats stats = Stats(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {group.Key} | {stats.N} | {stats.Worked236:P1} | {stats.Worked382:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Exact combo by pair");
            builder.AppendLine();
            builder.AppendLine("| File | Signals | worked236 | worked382 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in combo
                         .GroupBy(s => s.File)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Phase1Stats stats = Stats(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| `{group.Key}` | {stats.N} | {stats.Worked236:P1} | {stats.Worked382:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Exact combo by session (UTC)");
            builder.AppendLine();
            builder.AppendLine("| Session | Signals | worked236 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in combo
                         .GroupBy(s => SessionOf(s.EntryTime))
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Phase1Stats stats = Stats(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {group.Key} | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Exact combo by activity");
            builder.AppendLine();
            builder.AppendLine("Median bar range proxy; the old archive has no tick volume.");
            builder.AppendLine();
            BinTable(builder, "Activity W5/W3 within the exact combo",
                "Does the 'fuel exhausted' proxy add anything inside the already-selected set?",
                combo, s => s.ActivityW5W3,
                new[]
                {
                    (0d, 0.5d, "0–0.5"),
                    (0.5d, 0.8d, "0.5–0.8"),
                    (0.8d, 1.0d, "0.8–1.0"),
                    (1.0d, 1.5d, "1.0–1.5"),
                    (1.5d, double.PositiveInfinity, "1.5+")
                });

            builder.AppendLine();
            builder.AppendLine("## What the exact run says");
            builder.AppendLine();
            Phase1Stats comboStats = Stats(combo);
            Phase1Stats? w2OnlyStats = results.Count > 1 ? Stats(results[0].Samples) : null;
            if (w2OnlyStats != null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- Adding the W4 upper bound changes the exact sample from {w2OnlyStats.N} to {comboStats.N} signals, `worked236` from {w2OnlyStats.Worked236:P1} to {comboStats.Worked236:P1}, profitable from {w2OnlyStats.Profitable:P1} to {comboStats.Profitable:P1}, avg netR from {w2OnlyStats.AvgNetR:F3} to {comboStats.AvgNetR:F3}."));
            }

            if (comboStats.N == 0)
            {
                builder.AppendLine("- The exact combo produced no signals. The post-hoc result " +
                                   "does not survive the real gate order; do not ship it.");
            }
            else if (comboStats.AvgNetR > 0.05d && comboStats.Profitable > 0.45d)
            {
                builder.AppendLine("- The exact combo is positive and materially above the Phase 1 " +
                                   "post-hoc baseline. This is the first candidate worth a full " +
                                   "walk-forward and cTrader parameterisation.");
            }
            else if (comboStats.AvgNetR > -0.05d)
            {
                builder.AppendLine("- The exact combo is close to break-even but not positive. " +
                                   "Before shipping, test the same corridor with fixed R:R, " +
                                   "`MinRiskRewardRatio`, and a higher-timeframe/session filter.");
            }
            else
            {
                builder.AppendLine("- The exact combo is still clearly negative. The Phase 1 " +
                                   "post-hoc edge did not survive the exact gate order; reject it " +
                                   "as a trading filter.");
            }

            builder.AppendLine("- The exact run uses the same 12-file archive as Phase 0/1. " +
                               "A walk-forward by year and a fresh pair set are still required " +
                               "before any production change.");

            return builder.ToString();
        }

        private static string SessionOf(DateTime utc)
        {
            int hour = utc.Hour;
            if (hour < 7)
                return "Asia 00–07";
            if (hour < 12)
                return "London 07–12";
            if (hour < 21)
                return "New York 12–21";
            return "Late 21–24";
        }

        private sealed record ExactConfig(
            string Name,
            double MinWave2Retrace,
            double MaxWave4RetraceW3,
            double MinRiskRewardRatio = 0,
            DiagonalTriggerMode TriggerMode = DiagonalTriggerMode.WAVE3_BREAK);
    }
}
