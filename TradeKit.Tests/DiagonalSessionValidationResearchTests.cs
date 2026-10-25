using System.Text;
using NUnit.Framework;

namespace TradeKit.Tests
{
    /// <summary>
    /// Runs 1 and 2 of the diagonal follow-up: the session filter on fresh h1 pairs and a
    /// bootstrap / permutation significance check of the only positive cell
    /// (<c>h1 + London/NY + 23.6%</c>).
    /// <para>
    /// The finder is the exact Phase 0/1 corridor. The session labels are the same coarse UTC
    /// buckets as in the existing diagonal reports. The bootstrap resamples trades, not bars,
    /// and the permutation test shuffles the session labels; both use a fixed seed, so the
    /// report is reproducible.
    /// </para>
    /// <para>
    /// Writes <c>reports/diagonal_session_validation.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalSessionValidation"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const int BOOTSTRAP_ITERATIONS = 10_000;
        private const int BOOTSTRAP_SEED = 12345;

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_session_validation.md")]
        [Category("Research")]
        public void DiagonalSessionValidation_Report()
        {
            string dataDir = FindDataDir();

            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning h1 file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh h1 file was found under data/.");

            List<Sample> tuning = RunDiagonalAll(dataDir, tuningFiles);
            List<Sample> fresh = RunDiagonalAll(dataDir, freshFiles);

            Assert.That(tuning, Is.Not.Empty, "The tuning replay produced no signals.");
            Assert.That(fresh, Is.Not.Empty, "The fresh replay produced no signals.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_session_validation.md");
            File.WriteAllText(reportPath, BuildSessionReport(tuning, fresh, tuningFiles.Count, freshFiles.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static string BuildSessionReport(
            IReadOnlyList<Sample> tuning,
            IReadOnlyList<Sample> fresh,
            int tuningFileCount,
            int freshFileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — session filter and significance");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Tuning: {tuningFileCount} h1 Phase 0 pairs. Fresh: {freshFileCount} different h1 pairs. Exact corridor (`W2/W1 >= 0.5`, `W4/W3 <= 0.382`, 23.6% diagonal target). Net R includes a 2 bp round-trip cost."));
            builder.AppendLine();
            builder.AppendLine("Train = 2017–2022, test = 2023–2026. London/NY = the `London 07–12` and `New York 12–21` UTC buckets. The bootstrap resamples trades; the permutation test shuffles the session labels.");
            builder.AppendLine();

            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Set | Signals | Resolved | London/NY | Other |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            AppendSampleRow(builder, "tuning h1", tuning);
            AppendSampleRow(builder, "fresh h1", fresh);

            builder.AppendLine();
            builder.AppendLine("## By session");
            builder.AppendLine();
            builder.AppendLine("| Set | Session | N | resolved | profitable | avg netR | worked236 |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            AppendSessionRows(builder, "tuning h1", tuning);
            AppendSessionRows(builder, "fresh h1", fresh);

            builder.AppendLine();
            builder.AppendLine("## London/NY vs the rest");
            builder.AppendLine();
            builder.AppendLine("| Set | Group | N | resolved | profitable | avg netR | worked236 |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            AppendGroupRow(builder, "tuning h1", "London/NY", tuning.Where(s => IsLondonNewYork(s.EntryTime)).ToList());
            AppendGroupRow(builder, "tuning h1", "other", tuning.Where(s => !IsLondonNewYork(s.EntryTime)).ToList());
            AppendGroupRow(builder, "fresh h1", "London/NY", fresh.Where(s => IsLondonNewYork(s.EntryTime)).ToList());
            AppendGroupRow(builder, "fresh h1", "other", fresh.Where(s => !IsLondonNewYork(s.EntryTime)).ToList());

            builder.AppendLine();
            builder.AppendLine("## Train / test");
            builder.AppendLine();
            builder.AppendLine("| Set | Split | Group | N | profitable | avg netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|");
            foreach ((string name, IReadOnlyList<Sample> samples) in new[]
                     {
                         ("tuning h1", tuning),
                         ("fresh h1", fresh)
                     })
            {
                AppendSplitRows(builder, name, "train", samples.Where(s => s.EntryTime.Year <= 2022).ToList());
                AppendSplitRows(builder, name, "test", samples.Where(s => s.EntryTime.Year >= 2023).ToList());
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by pair");
            builder.AppendLine();
            builder.AppendLine("| File | London/NY N | London/NY netR | Other N | Other netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in fresh
                         .GroupBy(s => s.File)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<Sample> lny = group.Where(s => IsLondonNewYork(s.EntryTime)).ToList();
                List<Sample> other = group.Where(s => !IsLondonNewYork(s.EntryTime)).ToList();
                SessionStats lnyStats = SStat(lny);
                SessionStats otherStats = SStat(other);
                builder.AppendLine(FormattableString.Invariant(
                    $"| `{group.Key}` | {lnyStats.N} | {lnyStats.AvgNetR:F3} | {otherStats.N} | {otherStats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by period");
            builder.AppendLine();
            builder.AppendLine("| Period | London/NY N | London/NY netR | Other N | Other netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in fresh
                         .GroupBy(s => PeriodOf(s.EntryTime))
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<Sample> lny = group.Where(s => IsLondonNewYork(s.EntryTime)).ToList();
                List<Sample> other = group.Where(s => !IsLondonNewYork(s.EntryTime)).ToList();
                SessionStats lnyStats = SStat(lny);
                SessionStats otherStats = SStat(other);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {group.Key} | {lnyStats.N} | {lnyStats.AvgNetR:F3} | {otherStats.N} | {otherStats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Bootstrap — mean net R per trade");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"{BOOTSTRAP_ITERATIONS:N0} resamples, seed {BOOTSTRAP_SEED}. `95% CI` is the percentile interval."));
            builder.AppendLine();
            builder.AppendLine("| Set | Group | N | mean netR | 95% CI low | 95% CI high |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");
            AppendBootstrapRow(builder, "tuning h1", "all", tuning);
            AppendBootstrapRow(builder, "tuning h1", "London/NY", tuning.Where(s => IsLondonNewYork(s.EntryTime)).ToList());
            AppendBootstrapRow(builder, "tuning h1", "other", tuning.Where(s => !IsLondonNewYork(s.EntryTime)).ToList());
            AppendBootstrapRow(builder, "fresh h1", "all", fresh);
            AppendBootstrapRow(builder, "fresh h1", "London/NY", fresh.Where(s => IsLondonNewYork(s.EntryTime)).ToList());
            AppendBootstrapRow(builder, "fresh h1", "other", fresh.Where(s => !IsLondonNewYork(s.EntryTime)).ToList());

            builder.AppendLine();
            builder.AppendLine("## Permutation — London/NY minus the rest");
            builder.AppendLine();
            builder.AppendLine("| Set | Observed difference | 95% CI low | 95% CI high | p (two-sided) |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            AppendPermutationRow(builder, "tuning h1",
                tuning.Where(s => IsLondonNewYork(s.EntryTime)).ToList(),
                tuning.Where(s => !IsLondonNewYork(s.EntryTime)).ToList());
            AppendPermutationRow(builder, "fresh h1",
                fresh.Where(s => IsLondonNewYork(s.EntryTime)).ToList(),
                fresh.Where(s => !IsLondonNewYork(s.EntryTime)).ToList());

            builder.AppendLine();
            builder.AppendLine("## What this run says");
            builder.AppendLine();
            AppendSessionConclusions(builder, tuning, fresh);

            return builder.ToString();
        }

        private static void AppendSampleRow(
            StringBuilder builder, string name, IReadOnlyList<Sample> samples)
        {
            int lny = samples.Count(s => IsLondonNewYork(s.EntryTime));
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {samples.Count} | {samples.Count(s => s.Resolved)} | {lny} | {samples.Count - lny} |"));
        }

        private static void AppendSessionRows(
            StringBuilder builder, string name, IReadOnlyList<Sample> samples)
        {
            foreach (IGrouping<string, Sample> group in samples
                         .GroupBy(s => SessionOf(s.EntryTime))
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                SessionStats stats = SStat(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {name} | {group.Key} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {stats.Worked236:P1} |"));
            }
        }

        private static void AppendGroupRow(
            StringBuilder builder, string set, string group, IReadOnlyList<Sample> samples)
        {
            SessionStats stats = SStat(samples);
            builder.AppendLine(FormattableString.Invariant(
                $"| {set} | {group} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {stats.Worked236:P1} |"));
        }

        private static void AppendSplitRows(
            StringBuilder builder, string set, string split, IReadOnlyList<Sample> samples)
        {
            foreach ((string group, IReadOnlyList<Sample> subset) in new[]
                     {
                         ("London/NY", samples.Where(s => IsLondonNewYork(s.EntryTime)).ToList()),
                         ("other", samples.Where(s => !IsLondonNewYork(s.EntryTime)).ToList())
                     })
            {
                SessionStats stats = SStat(subset);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {set} | {split} | {group} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }
        }

        private static void AppendBootstrapRow(
            StringBuilder builder, string set, string group, IReadOnlyList<Sample> samples)
        {
            List<double> values = samples.Where(s => s.Resolved).Select(s => s.NetR).ToList();
            if (values.Count == 0)
            {
                builder.AppendLine($"| {set} | {group} | 0 | — | — | — |");
                return;
            }

            (double low, double high, double mean) =
                BootstrapMean(values, BOOTSTRAP_ITERATIONS, BOOTSTRAP_SEED);
            builder.AppendLine(FormattableString.Invariant(
                $"| {set} | {group} | {values.Count} | {mean:F3} | {low:F3} | {high:F3} |"));
        }

        private static void AppendPermutationRow(
            StringBuilder builder,
            string set,
            IReadOnlyList<Sample> groupA,
            IReadOnlyList<Sample> groupB)
        {
            List<double> a = groupA.Where(s => s.Resolved).Select(s => s.NetR).ToList();
            List<double> b = groupB.Where(s => s.Resolved).Select(s => s.NetR).ToList();

            if (a.Count < 2 || b.Count < 2)
            {
                builder.AppendLine($"| {set} | — | — | — | — |");
                return;
            }

            double observed = a.Average() - b.Average();
            (double low, double high, double _) =
                BootstrapDifference(a, b, BOOTSTRAP_ITERATIONS, BOOTSTRAP_SEED);
            double p = PermutationPValue(a, b, BOOTSTRAP_ITERATIONS, BOOTSTRAP_SEED + 1);

            builder.AppendLine(FormattableString.Invariant(
                $"| {set} | {observed:F3} | {low:F3} | {high:F3} | {p:F4} |"));
        }

        private static void AppendSessionConclusions(
            StringBuilder builder,
            IReadOnlyList<Sample> tuning,
            IReadOnlyList<Sample> fresh)
        {
            List<Sample> tuningLny = tuning.Where(s => IsLondonNewYork(s.EntryTime)).ToList();
            List<Sample> tuningOther = tuning.Where(s => !IsLondonNewYork(s.EntryTime)).ToList();
            List<Sample> freshLny = fresh.Where(s => IsLondonNewYork(s.EntryTime)).ToList();
            List<Sample> freshOther = fresh.Where(s => !IsLondonNewYork(s.EntryTime)).ToList();

            SessionStats tuningLnyStats = SStat(tuningLny);
            SessionStats freshLnyStats = SStat(freshLny);
            SessionStats freshOtherStats = SStat(freshOther);

            double freshP = PermutationPValue(
                freshLny.Where(s => s.Resolved).Select(s => s.NetR).ToList(),
                freshOther.Where(s => s.Resolved).Select(s => s.NetR).ToList(),
                BOOTSTRAP_ITERATIONS, BOOTSTRAP_SEED + 1);

            List<double> tuningLnyValues = tuningLny.Where(s => s.Resolved).Select(s => s.NetR).ToList();
            List<double> tuningOtherValues = tuningOther.Where(s => s.Resolved).Select(s => s.NetR).ToList();
            double tuningP = PermutationPValue(
                tuningLnyValues, tuningOtherValues, BOOTSTRAP_ITERATIONS, BOOTSTRAP_SEED + 1);
            (double tuningLow, double tuningHigh, double tuningMean) =
                BootstrapMean(tuningLnyValues, BOOTSTRAP_ITERATIONS, BOOTSTRAP_SEED);

            builder.AppendLine(FormattableString.Invariant(
                $"- Tuning h1 London/NY: {tuningLnyStats.N} signals, {tuningLnyStats.Profitable:P1} profitable, {tuningLnyStats.AvgNetR:F3}R. Fresh h1 London/NY: {freshLnyStats.N} signals, {freshLnyStats.Profitable:P1} profitable, {freshLnyStats.AvgNetR:F3}R; the rest of fresh is {freshOtherStats.N} signals at {freshOtherStats.AvgNetR:F3}R."));
            builder.AppendLine(FormattableString.Invariant(
                $"- Even on tuning the London/NY mean is {tuningMean:F3}R with a 95% CI of [{tuningLow:F3}, {tuningHigh:F3}] and permutation p = {tuningP:F4}. The +0.044R cell was never statistically distinguishable from zero; the fresh set then flips it negative."));
            builder.AppendLine(FormattableString.Invariant(
                $"- The fresh permutation p-value for London/NY minus the rest is {freshP:F4}. With 125-ish trades and a 0.43 R:R, only a p-value clearly below 0.05 and a 95% CI that excludes zero would justify the session filter."));

            bool freshPositive = freshLnyStats.AvgNetR > 0d;
            bool freshSignificant = freshP < 0.05d;
            if (!freshPositive)
            {
                builder.AppendLine("- The fresh London/NY mean is not positive. The session filter is **rejected** on fresh pairs.");
            }
            else if (!freshSignificant)
            {
                builder.AppendLine("- The fresh London/NY mean is positive but not statistically distinguishable from the rest. Treat it as noise, not as an edge.");
            }
            else
            {
                builder.AppendLine("- The fresh London/NY mean is positive and the permutation test separates it from the rest. This is a lead worth the execution-realism check.");
            }

            builder.AppendLine("- The bootstrap resamples trades, so it measures the uncertainty of the mean net R, not the uncertainty of the whole strategy. It does not correct for the fact that the session split was chosen after looking at the same archive.");
            builder.AppendLine("- The exact corridor is unchanged; this run only slices it by session. It does not test a different stop, target or entry.");
        }

        private static SessionStats SStat(IReadOnlyList<Sample> subset)
        {
            List<Sample> resolved = subset.Where(s => s.Resolved).ToList();
            return new SessionStats(
                subset.Count,
                resolved.Count,
                resolved.Count == 0 ? 0d : Share(resolved, s => s.Profitable),
                resolved.Count == 0 ? 0d : resolved.Average(s => s.NetR),
                subset.Count == 0 ? 0d : Share(subset, s => s.Worked236));
        }

        private static (double Low, double High, double Mean) BootstrapMean(
            IReadOnlyList<double> values, int iterations, int seed)
        {
            var rng = new Random(seed);
            var means = new double[iterations];

            for (int i = 0; i < iterations; i++)
            {
                double sum = 0d;
                for (int j = 0; j < values.Count; j++)
                    sum += values[rng.Next(values.Count)];

                means[i] = sum / values.Count;
            }

            Array.Sort(means);
            return (means[(int)(0.025d * iterations)],
                means[(int)(0.975d * iterations)],
                values.Average());
        }

        private static (double Low, double High, double Mean) BootstrapDifference(
            IReadOnlyList<double> a, IReadOnlyList<double> b, int iterations, int seed)
        {
            var rng = new Random(seed);
            var differences = new double[iterations];

            for (int i = 0; i < iterations; i++)
            {
                double sumA = 0d;
                for (int j = 0; j < a.Count; j++)
                    sumA += a[rng.Next(a.Count)];

                double sumB = 0d;
                for (int j = 0; j < b.Count; j++)
                    sumB += b[rng.Next(b.Count)];

                differences[i] = sumA / a.Count - sumB / b.Count;
            }

            Array.Sort(differences);
            return (differences[(int)(0.025d * iterations)],
                differences[(int)(0.975d * iterations)],
                a.Average() - b.Average());
        }

        private static double PermutationPValue(
            IReadOnlyList<double> a, IReadOnlyList<double> b, int iterations, int seed)
        {
            if (a.Count < 2 || b.Count < 2)
                return double.NaN;

            double observed = Math.Abs(a.Average() - b.Average());
            var all = a.Concat(b).ToArray();
            var rng = new Random(seed);
            int atLeast = 0;

            for (int i = 0; i < iterations; i++)
            {
                for (int j = all.Length - 1; j > 0; j--)
                {
                    int k = rng.Next(j + 1);
                    (all[j], all[k]) = (all[k], all[j]);
                }

                double sumA = 0d;
                for (int j = 0; j < a.Count; j++)
                    sumA += all[j];

                double sumB = 0d;
                for (int j = a.Count; j < all.Length; j++)
                    sumB += all[j];

                double difference = Math.Abs(sumA / a.Count - sumB / b.Count);
                if (difference >= observed)
                    atLeast++;
            }

            return (double)atLeast / iterations;
        }

        private sealed record SessionStats(
            int N,
            int Resolved,
            double Profitable,
            double AvgNetR,
            double Worked236);
    }
}
