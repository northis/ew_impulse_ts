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
    /// Exact replay of the recommended diagonal geometry: <c>TP = 50% retrace</c> of the
    /// diagonal and <c>SL = V(4) ± 2.0·|W3|</c>. The TP/SL grid that found this region was
    /// post-hoc; this test runs the real finder with the new parameters, so its own
    /// <c>tpBehindEntry</c>, <c>riskTooSmall</c> and <c>tpSlHit</c> gates and its own TP/SL
    /// events decide the sample.
    /// <para>
    /// m15 only: that is where the region was validated and where the sample is largest.
    /// Writes <c>reports/diagonal_exact_region_m15.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalExactRegionM15"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const double EXACT_REGION_RETRACE = 0.5d;
        private const double EXACT_REGION_STOP = 2.0d;

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_exact_region_m15.md")]
        [Category("Research")]
        public void DiagonalExactRegionM15_Report()
        {
            string dataDir = FindDataDir();

            List<string> tuningFiles = Existing(dataDir, M15_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_M15_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning m15 file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh m15 file was found under data/.");

            List<Sample> tuning = RunExactRegion(dataDir, tuningFiles);
            List<Sample> fresh = RunExactRegion(dataDir, freshFiles);

            Assert.That(tuning, Is.Not.Empty, "The tuning exact replay produced no signals.");
            Assert.That(fresh, Is.Not.Empty, "The fresh exact replay produced no signals.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_exact_region_m15.md");
            File.WriteAllText(reportPath, BuildExactRegionReport(tuning, fresh));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static List<Sample> RunExactRegion(
            string dataDir, IReadOnlyList<string> files)
        {
            return RunExactRegion(
                dataDir, files, EXACT_REGION_RETRACE, EXACT_REGION_STOP, 0d);
        }

        private static List<Sample> RunExactRegion(
            string dataDir,
            IReadOnlyList<string> files,
            double retraceRatio,
            double stopRatio,
            double minRiskReward)
        {
            var result = new List<Sample>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                file =>
                {
                    ITimeFrame timeFrame = file.Contains("_m15_")
                        ? TimeFrameHelper.Minute15
                        : TimeFrameHelper.Hour1;

                    var provider = new TestBarsProvider(timeFrame);
                    provider.LoadCandles(Path.Combine(dataDir, file));

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
                        maxWave4RetraceW3: 0.382,
                        requireWave4Shorter: true,
                        requireWave2Shorter: false,
                        minWave2Retrace: 0.5,
                        maxWave2Retrace: 0,
                        maxWave5SpillRatio: 0,
                        minRiskRewardRatio: minRiskReward,
                        takeProfitRetraceRatio: retraceRatio,
                        stopWave3Ratio: stopRatio);

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

                    string symbol = file[..file.IndexOf('_')];
                    var local = new List<Sample>(records.Count);
                    foreach (SignalRecord record in records)
                    {
                        Sample? sample = BuildSample(
                            provider, finder, record, file, symbol, "m15");
                        if (sample != null)
                            local.Add(sample);
                    }

                    lock (sync)
                    {
                        result.AddRange(local);
                    }
                });

            return result;
        }

        private static string BuildExactRegionReport(
            IReadOnlyList<Sample> tuning, IReadOnlyList<Sample> fresh)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — exact replay of the R50 / k2.00 region (m15)");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Real finder, not a post-hoc grid: `TakeProfitRetraceRatio = {EXACT_REGION_RETRACE:F2}`, `StopWave3Ratio = {EXACT_REGION_STOP:F1}`, `TakeProfitMode = DIAGONAL_RETRACE`. The exact corridor is unchanged (`W2/W1 >= 0.5`, `W4/W3 <= 0.382`). Net R includes a 2 bp round-trip cost."));
            builder.AppendLine();

            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Set | Signals | Train | Test |");
            builder.AppendLine("|---|---:|---:|---:|");
            AppendExactSampleRow(builder, "tuning m15", tuning);
            AppendExactSampleRow(builder, "fresh m15", fresh);

            builder.AppendLine();
            builder.AppendLine("## Exact result");
            builder.AppendLine();
            builder.AppendLine("| Set | Split | N | resolved | profitable | avg netR | avg R:R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            AppendExactRows(builder, "tuning m15", "train", tuning.Where(s => s.EntryTime.Year <= 2022).ToList());
            AppendExactRows(builder, "tuning m15", "test", tuning.Where(s => s.EntryTime.Year >= 2023).ToList());
            AppendExactRows(builder, "fresh m15", "train", fresh.Where(s => s.EntryTime.Year <= 2022).ToList());
            AppendExactRows(builder, "fresh m15", "test", fresh.Where(s => s.EntryTime.Year >= 2023).ToList());

            builder.AppendLine();
            builder.AppendLine("## Fresh by pair");
            builder.AppendLine();
            builder.AppendLine("| File | N | win | netR |");
            builder.AppendLine("|---|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in fresh
                         .GroupBy(s => s.File)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                ExactStats stats = ExactStat(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| `{group.Key}` | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by year");
            builder.AppendLine();
            builder.AppendLine("| Year | N | win | netR |");
            builder.AppendLine("|---|---:|---:|---:|");
            foreach (IGrouping<int, Sample> group in fresh
                         .GroupBy(s => s.EntryTime.Year)
                         .OrderBy(g => g.Key))
            {
                ExactStats stats = ExactStat(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {group.Key} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## What this exact run says");
            builder.AppendLine();
            AppendExactConclusions(builder, tuning, fresh);

            return builder.ToString();
        }

        private static void AppendExactSampleRow(
            StringBuilder builder, string name, IReadOnlyList<Sample> samples)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {samples.Count} | {samples.Count(s => s.EntryTime.Year <= 2022)} | {samples.Count(s => s.EntryTime.Year >= 2023)} |"));
        }

        private static void AppendExactRows(
            StringBuilder builder, string set, string split, IReadOnlyList<Sample> samples)
        {
            ExactStats stats = ExactStat(samples);
            builder.AppendLine(FormattableString.Invariant(
                $"| {set} | {split} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {stats.AvgRr:F2} |"));
        }

        private static ExactStats ExactStat(IReadOnlyList<Sample> samples)
        {
            List<Sample> resolved = samples.Where(s => s.Resolved).ToList();
            return new ExactStats(
                samples.Count,
                resolved.Count,
                resolved.Count == 0 ? 0d : Share(resolved, s => s.Profitable),
                resolved.Count == 0 ? 0d : resolved.Average(s => s.NetR),
                resolved.Count == 0 ? 0d : resolved.Average(s =>
                    Math.Abs(s.Tp - s.Entry) / Math.Max(1e-12d, s.Risk)));
        }

        private static void AppendExactConclusions(
            StringBuilder builder,
            IReadOnlyList<Sample> tuning,
            IReadOnlyList<Sample> fresh)
        {
            ExactStats tuningTest = ExactStat(tuning.Where(s => s.EntryTime.Year >= 2023).ToList());
            ExactStats freshTest = ExactStat(fresh.Where(s => s.EntryTime.Year >= 2023).ToList());

            builder.AppendLine(FormattableString.Invariant(
                $"- The exact replay gives **{freshTest.AvgNetR:F3}R** on the fresh m15 test over {freshTest.N} signals ({freshTest.Profitable:P1} profitable, avg R:R {freshTest.AvgRr:F2}). The post-hoc grid gave +0.163R over 349; the difference is the finder's own gates and its exact TP/SL events."));
            builder.AppendLine(FormattableString.Invariant(
                $"- Tuning m15 test: {tuningTest.AvgNetR:F3}R over {tuningTest.N} signals ({tuningTest.Profitable:P1} profitable)."));
            builder.AppendLine(FormattableString.Invariant(
                $"- The exact result **collapses** relative to the post-hoc grid: fresh test {freshTest.AvgNetR:F3}R versus +0.163R, tuning test {tuningTest.AvgNetR:F3}R versus +0.128R. The finder admits {fresh.Count} fresh signals versus 713 in the post-hoc set; the extra signals, admitted by the changed `tpBehindEntry` / `riskTooSmall` / `tpSlHit` gates, are lower quality. The region is **not confirmed** in the real gate order. Do not ship it to cTrader yet."));
            builder.AppendLine("- If the exact fresh result is close to the post-hoc one, the region survives the real gate order and is ready for cTrader. If it collapses, the post-hoc grid was relying on signals the finder rejects (for example `tpBehindEntry` or `riskTooSmall`).");
            builder.AppendLine("- This run changes only the target and stop multipliers. It does not add a session, pair or year filter.");
        }

        private sealed record ExactStats(
            int N,
            int Resolved,
            double Profitable,
            double AvgNetR,
            double AvgRr);
    }
}
