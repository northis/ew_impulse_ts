using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace TradeKit.Tests
{
    /// <summary>
    /// Fresh-pair validation of the h1 <c>MinRiskRewardRatio = 0.5</c> candidate
    /// (DIAGONAL.md §9.23). Tuning pairs are the eight h1 files used in Phase 0/1;
    /// the fresh set is eight h1 pairs that never took part in the threshold search.
    /// Both sets are replayed with minRR 0 and 0.5, with the exact W2/W4 corridor and
    /// the 23.6% target.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private static readonly string[] FRESH_H1_FILES =
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

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_minrr05_validation.md")]
        [Category("Research")]
        public void DiagonalMinRr05_Validation_Report()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = H1_FILES
                .Where(f => File.Exists(Path.Combine(dataDir, f)))
                .ToList();
            List<string> freshFiles = FRESH_H1_FILES
                .Where(f => File.Exists(Path.Combine(dataDir, f)))
                .ToList();

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning h1 file was found.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh h1 file was found.");

            var configs = new[]
            {
                new ExactConfig("minRR 0", 0.5d, 0.382d, 0d),
                new ExactConfig("minRR 0.5", 0.5d, 0.382d, 0.5d)
            };

            var tuning = new List<(ExactConfig Config, List<Sample> Samples)>();
            var fresh = new List<(ExactConfig Config, List<Sample> Samples)>();
            foreach (ExactConfig config in configs)
            {
                List<Sample> tuningSamples = RunExactAll(dataDir, tuningFiles, config);
                List<Sample> freshSamples = RunExactAll(dataDir, freshFiles, config);
                tuning.Add((config, tuningSamples));
                fresh.Add((config, freshSamples));

                TestContext.Out.WriteLine(
                    $"{config.Name}: tuning={tuningSamples.Count}, fresh={freshSamples.Count}");
            }

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_minrr05_validation.md");
            File.WriteAllText(reportPath, BuildMinRr05Report(tuning, fresh));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static string BuildMinRr05Report(
            IReadOnlyList<(ExactConfig Config, List<Sample> Samples)> tuning,
            IReadOnlyList<(ExactConfig Config, List<Sample> Samples)> fresh)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal minRR 0.5 — tuning vs fresh h1");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("Exact h1 replays with `W2/W1 >= 0.5`, `W4/W3 <= 0.382` and the " +
                               "23.6% diagonal target. Tuning pairs are the eight h1 files used " +
                               "in Phase 0/1; the fresh set is eight different h1 pairs. " +
                               "`MinRiskRewardRatio` is the finder's real wait.");
            builder.AppendLine();

            builder.AppendLine("## Overall");
            builder.AppendLine();
            builder.AppendLine("| Set | Config | Signals | worked236 | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");
            foreach ((ExactConfig config, List<Sample> samples) in tuning)
            {
                Phase1Stats stats = Stats(samples);
                builder.AppendLine(FormattableString.Invariant(
                    $"| tuning | {config.Name} | {stats.N} | {stats.Worked236:P1} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            foreach ((ExactConfig config, List<Sample> samples) in fresh)
            {
                Phase1Stats stats = Stats(samples);
                builder.AppendLine(FormattableString.Invariant(
                    $"| fresh | {config.Name} | {stats.N} | {stats.Worked236:P1} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            Phase1Stats freshBase = Stats(fresh.First(r => r.Config.Name == "minRR 0").Samples);
            List<Sample> fresh05Samples = fresh.First(r => r.Config.Name == "minRR 0.5").Samples;
            Phase1Stats fresh05 = Stats(fresh05Samples);
            builder.AppendLine();
            builder.AppendLine("Fresh-set delta (minRR 0.5 vs 0):");
            builder.AppendLine();
            builder.AppendLine("| Δ signals | Δ worked236 | Δ profitable | Δ netR |");
            builder.AppendLine("|---:|---:|---:|---:|");
            builder.AppendLine(FormattableString.Invariant(
                $"| {fresh05.N - freshBase.N:+0;-0} | {fresh05.Worked236 - freshBase.Worked236:+0.0%;-0.0%} | {fresh05.Profitable - freshBase.Profitable:+0.0%;-0.0%} | {fresh05.AvgNetR - freshBase.AvgNetR:+0.000;-0.000} |"));

            builder.AppendLine();
            builder.AppendLine("## Fresh walk-forward — minRR 0.5");
            builder.AppendLine();
            builder.AppendLine("| Period | Signals | worked236 | worked382 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (string period in new[] { "2017–2020", "2021–2023", "2024–2026" })
            {
                List<Sample> subset = fresh05Samples
                    .Where(s => PeriodOf(s.EntryTime) == period)
                    .ToList();
                Phase1Stats stats = Stats(subset);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {period} | {stats.N} | {stats.Worked236:P1} | {stats.Worked382:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by year — minRR 0.5");
            builder.AppendLine();
            builder.AppendLine("| Year | Signals | worked236 | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach (IGrouping<int, Sample> group in fresh05Samples
                         .GroupBy(s => s.EntryTime.Year)
                         .OrderBy(g => g.Key))
            {
                Phase1Stats stats = Stats(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {group.Key} | {stats.N} | {stats.Worked236:P1} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by pair — minRR 0.5");
            builder.AppendLine();
            builder.AppendLine("| File | Signals | worked236 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in fresh05Samples
                         .GroupBy(s => s.File)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Phase1Stats stats = Stats(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| `{group.Key}` | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## What this validation says");
            builder.AppendLine();
            int positivePeriods = new[] { "2017–2020", "2021–2023", "2024–2026" }
                .Count(p => Stats(fresh05Samples
                    .Where(s => PeriodOf(s.EntryTime) == p).ToList()).AvgNetR > 0);
            int positivePairs = fresh05Samples
                .GroupBy(s => s.File)
                .Where(g => g.Count() >= 10)
                .Count(g => Stats(g.ToList()).AvgNetR > 0);
            int eligiblePairs = fresh05Samples
                .GroupBy(s => s.File)
                .Count(g => g.Count() >= 10);

            builder.AppendLine(FormattableString.Invariant(
                $"- Fresh minRR 0.5: {fresh05.N} signals, {fresh05.Profitable:P1} profitable, {fresh05.AvgNetR:F3}R. Baseline minRR 0: {freshBase.N} signals, {freshBase.Profitable:P1} profitable, {freshBase.AvgNetR:F3}R."));
            builder.AppendLine(FormattableString.Invariant(
                $"- Positive fresh periods: {positivePeriods}/3. Positive pairs with at least 10 signals: {positivePairs}/{eligiblePairs}."));

            bool improves = fresh05.AvgNetR > freshBase.AvgNetR + 0.02d;
            bool positive = fresh05.AvgNetR > 0d;
            bool robust = positivePeriods >= 2 && positivePairs >= Math.Max(2, eligiblePairs / 2);

            if (positive && improves && robust)
            {
                builder.AppendLine("- The h1 `minRR 0.5` candidate survives fresh-pair validation: " +
                                   "positive overall, better than minRR 0, and at least half of the " +
                                   "pairs/periods agree. It is a candidate for an **h1 retrace " +
                                   "preset**, not for the global default; `TakeProfitMode = RISK_RATIO` " +
                                   "is unaffected by `MinRiskRewardRatio`.");
            }
            else if (positive && improves)
            {
                builder.AppendLine("- The fresh set is positive and better than minRR 0, but the " +
                                   "period/pair stability is not there yet. Keep it as a research " +
                                   "preset; do not change the production default.");
            }
            else
            {
                builder.AppendLine("- The fresh set does not confirm the tuning result. Do not make " +
                                   "`MinRiskRewardRatio = 0.5` a default; keep the W2/W4 corridor and " +
                                   "minRR as research settings.");
            }

            builder.AppendLine("- `MaxWave4RetraceW3` remains research-only; it is not exposed in " +
                               "the cTrader panel. A production change would need a separate " +
                               "`DiagonalParams`/robot parameter and a fresh walk-forward.");
            return builder.ToString();
        }
    }
}
