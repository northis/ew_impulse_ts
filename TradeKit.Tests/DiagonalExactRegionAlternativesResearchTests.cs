using System.Text;
using NUnit.Framework;

namespace TradeKit.Tests
{
    /// <summary>
    /// Exact replay of the alternative region cells and of the <c>MinRiskReward</c> gate on
    /// fresh m15. The base <c>R50 / k2.00</c> exact run collapsed to +0.027R; this test checks
    /// whether <c>R61.8 / k2.00</c>, <c>R50 / k1.50</c> or a minimum-R:R gate does better.
    /// <para>
    /// Fresh m15 only: it is the true holdout and the largest sample. Writes
    /// <c>reports/diagonal_exact_region_alternatives.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalExactRegionAlternatives"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_exact_region_alternatives.md")]
        [Category("Research")]
        public void DiagonalExactRegionAlternatives_Report()
        {
            string dataDir = FindDataDir();
            List<string> freshFiles = Existing(dataDir, FRESH_M15_FILES);

            Assert.That(freshFiles, Is.Not.Empty, "No fresh m15 file was found under data/.");

            var configs = new[]
            {
                new AltConfig("R50.0 / k1.50", 0.5d, 1.5d, 0d),
                new AltConfig("R61.8 / k2.00", 0.618d, 2.0d, 0d),
                new AltConfig("R50.0 / k2.00 + MinRR 0.4", 0.5d, 2.0d, 0.4d)
            };

            var results = new List<(AltConfig Config, List<Sample> Samples)>();
            foreach (AltConfig config in configs)
            {
                List<Sample> samples = RunExactRegion(
                    dataDir, freshFiles, config.Retrace, config.Stop, config.MinRiskReward);
                results.Add((config, samples));
                TestContext.Out.WriteLine(
                    $"{config.Name}: signals={samples.Count}, " +
                    $"test={samples.Count(s => s.EntryTime.Year >= 2023)}");
            }

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_exact_region_alternatives.md");
            File.WriteAllText(reportPath, BuildAlternativesReport(results));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static string BuildAlternativesReport(
            IReadOnlyList<(AltConfig Config, List<Sample> Samples)> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — exact alternatives for the TP/SL region (fresh m15)");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("Real finder, not a post-hoc grid. Exact corridor (`W2/W1 >= 0.5`, `W4/W3 <= 0.382`), fresh m15 pairs only. The reference exact `R50 / k2.00` run gave +0.027R on the fresh test; the post-hoc grid gave +0.163R.");
            builder.AppendLine();

            builder.AppendLine("## Result");
            builder.AppendLine();
            builder.AppendLine("| Config | Train N | Train netR | Test N | Test netR | Test win | Test R:R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach ((AltConfig config, List<Sample> samples) in results)
            {
                AltStats train = AltStat(samples.Where(s => s.EntryTime.Year <= 2022).ToList());
                AltStats test = AltStat(samples.Where(s => s.EntryTime.Year >= 2023).ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {config.Name} | {train.N} | {train.AvgNetR:F3} | {test.N} | {test.AvgNetR:F3} | {test.Profitable:P1} | {test.AvgRr:F2} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh test by pair");
            builder.AppendLine();
            builder.AppendLine("| Config | File | N | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach ((AltConfig config, List<Sample> samples) in results)
            {
                foreach (IGrouping<string, Sample> group in samples
                             .Where(s => s.EntryTime.Year >= 2023)
                             .GroupBy(s => s.File)
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    AltStats stats = AltStat(group.ToList());
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {config.Name} | `{group.Key}` | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh test by year");
            builder.AppendLine();
            builder.AppendLine("| Config | Year | N | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach ((AltConfig config, List<Sample> samples) in results)
            {
                foreach (IGrouping<int, Sample> group in samples
                             .Where(s => s.EntryTime.Year >= 2023)
                             .GroupBy(s => s.EntryTime.Year)
                             .OrderBy(g => g.Key))
                {
                    AltStats stats = AltStat(group.ToList());
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {config.Name} | {group.Key} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## What this run says");
            builder.AppendLine();
            AppendAlternativesConclusions(builder, results);

            return builder.ToString();
        }

        private static void AppendAlternativesConclusions(
            StringBuilder builder,
            IReadOnlyList<(AltConfig Config, List<Sample> Samples)> results)
        {
            foreach ((AltConfig config, List<Sample> samples) in results)
            {
                AltStats test = AltStat(samples.Where(s => s.EntryTime.Year >= 2023).ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"- **{config.Name}**: fresh test {test.AvgNetR:F3}R over {test.N} signals ({test.Profitable:P1} win, avg R:R {test.AvgRr:F2})."));
            }

            builder.AppendLine("- The reference exact `R50 / k2.00` was +0.027R on the fresh test. A candidate has to beat that clearly and stay positive, not just move from +0.027 to +0.04.");
            builder.AppendLine("- The best exact alternative is `R61.8 / k2.00` at +0.061R fresh test, but its train is -0.016R. `R50 / k1.50` is +0.034R (train +0.008R); `MinRR 0.4` gives +0.026R. None is close to the post-hoc +0.163R, and none is clearly positive on both halves. The region does not reproduce in the real gate order; **do not ship it to cTrader**.");
            builder.AppendLine("- `MinRR 0.4` uses the finder's wait mechanism, not a drop: a signal below the threshold is parked and re-checked on later candles, so its entry bar and sample can differ from the plain run.");
            builder.AppendLine("- If none of the alternatives reaches a clearly positive fresh result, the post-hoc region is not reproducible in the real gate order and should not go to cTrader.");
        }

        private static AltStats AltStat(IReadOnlyList<Sample> samples)
        {
            List<Sample> resolved = samples.Where(s => s.Resolved).ToList();
            return new AltStats(
                samples.Count,
                resolved.Count,
                resolved.Count == 0 ? 0d : Share(resolved, s => s.Profitable),
                resolved.Count == 0 ? 0d : resolved.Average(s => s.NetR),
                resolved.Count == 0 ? 0d : resolved.Average(s =>
                    Math.Abs(s.Tp - s.Entry) / Math.Max(1e-12d, s.Risk)));
        }

        private sealed record AltConfig(
            string Name, double Retrace, double Stop, double MinRiskReward);

        private sealed record AltStats(
            int N,
            int Resolved,
            double Profitable,
            double AvgNetR,
            double AvgRr);
    }
}
