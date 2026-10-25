using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace TradeKit.Tests
{
    /// <summary>
    /// Exact h1 pass with the real <c>MinRiskRewardRatio</c> wait and a robot-level
    /// London/New York session filter (DIAGONAL.md §9.22). The finder is replayed with
    /// the W2/W4 corridor and the 23.6% target; the session filter only removes emitted
    /// signals, so the signal set inside a session is exact.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_h1_session_minrr.md")]
        [Category("Research")]
        public void DiagonalH1SessionMinRr_Report()
        {
            string dataDir = FindDataDir();
            var h1Files = H1_FILES
                .Where(f => File.Exists(Path.Combine(dataDir, f)))
                .ToList();

            Assert.That(h1Files, Is.Not.Empty, "No h1 archive file was found under data/.");

            var configs = new[]
            {
                new ExactConfig("minRR 0", 0.5d, 0.382d, 0d),
                new ExactConfig("minRR 0.5", 0.5d, 0.382d, 0.5d),
                new ExactConfig("minRR 0.7", 0.5d, 0.382d, 0.7d),
                new ExactConfig("minRR 0.8", 0.5d, 0.382d, 0.8d)
            };

            var results = new List<(ExactConfig Config, List<Sample> Samples)>();
            foreach (ExactConfig config in configs)
            {
                List<Sample> samples = RunExactAll(dataDir, h1Files, config);
                results.Add((config, samples));
                TestContext.Out.WriteLine(
                    $"{config.Name}: signals={samples.Count}, " +
                    $"London/NY={samples.Count(s => IsLondonNewYork(s.EntryTime))}");
            }

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_h1_session_minrr.md");
            File.WriteAllText(reportPath, BuildH1SessionMinRrReport(results));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static string BuildH1SessionMinRrReport(
            IReadOnlyList<(ExactConfig Config, List<Sample> Samples)> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal h1 — session filter × MinRiskRewardRatio wait");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("Exact h1 replays with `W2/W1 >= 0.5`, `W4/W3 <= 0.382` and the " +
                               "23.6% diagonal target. `MinRiskRewardRatio` is the finder's real " +
                               "wait, not a drop filter. The London/New York filter is applied " +
                               "to emitted signals at the robot level, so the in-session signal " +
                               "set is exact.");
            builder.AppendLine();

            builder.AppendLine("## Configs");
            builder.AppendLine();
            builder.AppendLine("| Config | Signals | worked236 | profitable | avg netR | avg R:R | " +
                               "London/NY N | London/NY profit | London/NY netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach ((ExactConfig config, List<Sample> samples) in results)
            {
                Phase1Stats all = Stats(samples);
                List<Sample> london = samples.Where(s => IsLondonNewYork(s.EntryTime)).ToList();
                Phase1Stats londonStats = Stats(london);
                List<Sample> resolved = samples.Where(s => s.Resolved).ToList();
                double avgRr = resolved.Count == 0
                    ? 0
                    : resolved.Average(s => Math.Abs(s.Tp - s.Entry) / Math.Max(1e-12, s.Risk));
                builder.AppendLine(FormattableString.Invariant(
                    $"| {config.Name} | {all.N} | {all.Worked236:P1} | {all.Profitable:P1} | {all.AvgNetR:F3} | {avgRr:F2} | {londonStats.N} | {londonStats.Profitable:P1} | {londonStats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Session split per config");
            builder.AppendLine();
            builder.AppendLine("| Config | Session | Signals | worked236 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach ((ExactConfig config, List<Sample> samples) in results)
            {
                WriteSessionRow(builder, config.Name, "London+NY",
                    samples.Where(s => IsLondonNewYork(s.EntryTime)).ToList());
                WriteSessionRow(builder, config.Name, "Asia+Late",
                    samples.Where(s => !IsLondonNewYork(s.EntryTime)).ToList());
            }

            builder.AppendLine();
            builder.AppendLine("## Best h1 session config by pair");
            builder.AppendLine();
            (ExactConfig BestConfig, List<Sample> BestSamples)? best = null;
            double bestNetR = double.NegativeInfinity;
            foreach ((ExactConfig config, List<Sample> samples) in results)
            {
                List<Sample> london = samples.Where(s => IsLondonNewYork(s.EntryTime)).ToList();
                Phase1Stats stats = Stats(london);
                if (stats.Resolved >= 30 && stats.AvgNetR > bestNetR)
                {
                    bestNetR = stats.AvgNetR;
                    best = (config, samples);
                }
            }

            if (best == null)
            {
                builder.AppendLine("No config reached 30 resolved London/NY signals.");
            }
            else
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"Best: **{best.Value.BestConfig.Name}**, London/NY only."));
                builder.AppendLine();
                builder.AppendLine("| File | Signals | worked236 | resolved | profitable | avg netR |");
                builder.AppendLine("|---|---:|---:|---:|---:|---:|");
                foreach (IGrouping<string, Sample> group in best.Value.BestSamples
                             .Where(s => IsLondonNewYork(s.EntryTime))
                             .GroupBy(s => s.File)
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    Phase1Stats stats = Stats(group.ToList());
                    builder.AppendLine(FormattableString.Invariant(
                        $"| `{group.Key}` | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## What this run says");
            builder.AppendLine();
            if (best == null)
            {
                builder.AppendLine("- No MinRiskRewardRatio/session combination has enough " +
                                   "London/NY signals to judge. Do not add a session filter.");
            }
            else
            {
                Phase1Stats bestStats = Stats(
                    best.Value.BestSamples.Where(s => IsLondonNewYork(s.EntryTime)).ToList());
                int positivePairs = best.Value.BestSamples
                    .Where(s => IsLondonNewYork(s.EntryTime))
                    .GroupBy(s => s.File)
                    .Where(g => g.Count() >= 10)
                    .Count(g => Stats(g.ToList()).AvgNetR > 0);

                builder.AppendLine(FormattableString.Invariant(
                    $"- Best London/NY config: **{best.Value.BestConfig.Name}** — {bestStats.N} signals, {bestStats.Profitable:P1} profitable, {bestStats.AvgNetR:F3}R, {positivePairs} positive pairs with at least 10 signals."));
                if (bestStats.AvgNetR > 0.05d && positivePairs >= 2)
                {
                    builder.AppendLine("- The session + wait combination is positive on h1. " +
                                       "This is the first context candidate worth adding to the " +
                                       "robot: a London/NY entry window plus the real MinRiskReward " +
                                       "wait. Keep `MaxWave4RetraceW3` research-only until a fresh " +
                                       "pair/timeframe validation.");
                }
                else if (bestStats.AvgNetR > 0)
                {
                    builder.AppendLine("- The combination is marginally positive, but the edge is " +
                                       "thin and pair-dependent. Do not add the session filter yet; " +
                                       "extend the archive or validate on fresh pairs first.");
                }
                else
                {
                    builder.AppendLine("- Even the best London/NY + MinRiskReward combination is " +
                                       "not positive. The h1 edge seen in the walk-forward does not " +
                                       "survive the real wait; do not add a session filter.");
                }
            }

            builder.AppendLine("- `MinRiskRewardRatio` is the finder's wait, so its signal count and " +
                               "entry prices differ from the earlier drop-filter screen. The " +
                               "session filter is robot-level and does not change the candidate pool.");
            return builder.ToString();
        }

        private static void WriteSessionRow(
            StringBuilder builder, string config, string session, IReadOnlyList<Sample> samples)
        {
            Phase1Stats stats = Stats(samples);
            builder.AppendLine(FormattableString.Invariant(
                $"| {config} | {session} | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
        }

        private static bool IsLondonNewYork(DateTime utc) =>
            SessionOf(utc) is "London 07–12" or "New York 12–21";
    }
}
