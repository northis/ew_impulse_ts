using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for the "wave 4 is often ~78 % of wave 2 in length" hypothesis
    /// (DIAGONAL.md §9.49). The replay is done with the <b>loosest</b> geometry the finder
    /// allows — every optional gate is off, including the 3 % wave-3 penetration floor and
    /// the wave-4-shorter-than-wave-2 time rule — so that the W4/W2 distribution is not
    /// pre-filtered by the other beauty rules.
    /// <para>
    /// Writes <c>reports/diagonal_w4w2.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalW4W2"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string W4W2_REPORT = "diagonal_w4w2.md";

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_w4w2.md")]
        [Category("Research")]
        public void DiagonalW4W2Research_Report()
        {
            string dataDir = FindDataDir();
            var files = H1_FILES.Concat(M15_FILES)
                .Where(f => File.Exists(Path.Combine(dataDir, f)))
                .ToList();
            Assert.That(files, Is.Not.Empty, "No archive file was found under data/.");

            var samples = new List<Sample>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 6 },
                file =>
                {
                    FileRun run = RunFile(Path.Combine(dataDir, file), file, loosest: true);
                    lock (sync)
                    {
                        samples.AddRange(run.Samples);
                    }

                    TestContext.Out.WriteLine($"{file}: signals={run.Samples.Count}");
                });

            Assert.That(samples, Is.Not.Empty,
                "The loosest replay produced no diagonal setups.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);
            string reportPath = Path.Combine(reportDir, W4W2_REPORT);
            File.WriteAllText(reportPath, BuildW4W2Report(samples));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static string BuildW4W2Report(IReadOnlyList<Sample> samples)
        {
            List<Sample> resolved = samples.Where(s => s.Resolved).ToList();
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal: is wave 4 ~78 % of wave 2?");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC."));
            builder.AppendLine(
                "Loosest geometry: all optional gates off (penetration 0, W4/W3 corridor off, " +
                "W4 shorter than W2 off, convergence/spill off, no PA/RR gate), structural " +
                "Elliott rules and the duration sanity remain. Fixed R:R 1.5 target.");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Signals: {samples.Count}, resolved {resolved.Count}, worked236 {Share(samples, s => s.Worked236):P1}, profitable among resolved {Share(resolved, s => s.Profitable):P1}, avg netR {resolved.DefaultIfEmpty().Average(s => s?.NetR ?? 0):F3}."));
            builder.AppendLine();

            var bins = new List<(double Low, double High, string Label)>();
            for (double low = 0.30; low < 1.0 - 1e-9; low += 0.05)
                bins.Add((low, low + 0.05, FormattableString.Invariant($"{low:F2}-{low + 0.05:F2}")));

            BinTable(builder, "W4/W2 distribution",
                "Does the ratio cluster at a fibo level (0.786) or spread out? A spike in one " +
                "bin is the claim under test; the outcome columns say whether the level matters.",
                samples, s => s.W4W2, bins);

            builder.AppendLine("## Rules (train <= 2022 / test >= 2023)");
            builder.AppendLine();
            builder.AppendLine("| Rule | Signals | Coverage | worked236 | resolved | profitable | avg netR | train N | train worked236 | test N | test worked236 |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            RuleRow(builder, "W4/W2 >= 0.78", samples, s => s.W4W2 >= 0.78);
            RuleRow(builder, "W4/W2 >= 0.75 & <= 0.82", samples,
                s => s.W4W2 >= 0.75 && s.W4W2 <= 0.82);
            RuleRow(builder, "W4/W2 >= 0.786 (±0.05)", samples,
                s => Math.Abs(s.W4W2 - 0.786) <= 0.05);
            RuleRow(builder, "W4/W2 >= 0.70", samples, s => s.W4W2 >= 0.70);
            RuleRow(builder, "W4/W2 <= 0.30 (control)", samples, s => s.W4W2 <= 0.30);
            RuleRow(builder, "W4/W2 >= 0.78 & W3/W1 >= 0.78", samples,
                s => s.W4W2 >= 0.78 && s.W3W1 >= 0.78);
            builder.AppendLine();

            builder.AppendLine("## AUC");
            builder.AppendLine();
            builder.AppendLine("| Metric | AUC worked236 | AUC worked382 | AUC profitable |");
            builder.AppendLine("|---|---:|---:|---:|");
            AucRow(builder, "W4/W2", samples, s => s.W4W2);
            AucRow(builder, "W4/W3", samples, s => s.W4W3);
            builder.AppendLine();

            return builder.ToString();
        }
    }
}
