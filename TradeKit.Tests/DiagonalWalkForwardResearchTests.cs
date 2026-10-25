using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Walk-forward and context pass over the exact W2/W4 corridor (DIAGONAL.md §9.21):
    /// period stability, fixed R:R / MinRiskReward screening, session and higher-timeframe
    /// context. The sample is produced by the exact finder, not by a post-hoc filter on the
    /// Phase 0 baseline.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_walk_forward.md")]
        [Category("Research")]
        public void DiagonalWalkForward_Report()
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

            var config = new ExactConfig(
                "W2/W1 >= 0.5 & W4/W3 <= 0.382 + 23.6%", 0.5d, 0.382d);
            List<Sample> samples = RunExactAll(dataDir, files, config);
            Assert.That(samples, Is.Not.Empty, "The exact corridor produced no signals.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_walk_forward.md");
            File.WriteAllText(reportPath, BuildWalkForwardReport(samples, dataDir, files.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static string BuildWalkForwardReport(
            IReadOnlyList<Sample> samples, string dataDir, int fileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal walk-forward — W2/W4 corridor + 23.6% TP");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Exact finder sample: {samples.Count} signals across {fileCount} files. Gates: `W2/W1 >= 0.5`, `W4/W3 <= 0.382`, target 23.6% of the diagonal. The execution/context sections below are post-hoc screens on this exact sample; the period tables are exact."));
            builder.AppendLine();

            Phase1Stats overall = Stats(samples);
            builder.AppendLine("## Overall");
            builder.AppendLine();
            builder.AppendLine("| Signals | worked236 | worked382 | resolved | profitable | avg netR |");
            builder.AppendLine("|---:|---:|---:|---:|---:|---:|");
            builder.AppendLine(FormattableString.Invariant(
                $"| {overall.N} | {overall.Worked236:P1} | {overall.Worked382:P1} | {overall.Resolved} | {overall.Profitable:P1} | {overall.AvgNetR:F3} |"));

            builder.AppendLine();
            builder.AppendLine("## Period stability");
            builder.AppendLine();
            builder.AppendLine("| Period | Signals | worked236 | worked382 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            var periods = new[] { "2017–2020", "2021–2023", "2024–2026" };
            foreach (string period in periods)
            {
                List<Sample> subset = samples.Where(s => PeriodOf(s.EntryTime) == period).ToList();
                Phase1Stats stats = Stats(subset);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {period} | {stats.N} | {stats.Worked236:P1} | {stats.Worked382:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("### By year");
            builder.AppendLine();
            builder.AppendLine("| Year | Signals | worked236 | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach (IGrouping<int, Sample> group in samples
                         .GroupBy(s => s.EntryTime.Year)
                         .OrderBy(g => g.Key))
            {
                Phase1Stats stats = Stats(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {group.Key} | {stats.N} | {stats.Worked236:P1} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("### Period × timeframe");
            builder.AppendLine();
            builder.AppendLine("| Period | Timeframe | Signals | worked236 | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");
            foreach (string period in periods)
            {
                foreach (string tf in new[] { "h1", "m15" })
                {
                    List<Sample> subset = samples
                        .Where(s => PeriodOf(s.EntryTime) == period && s.TimeFrame == tf)
                        .ToList();
                    if (subset.Count == 0)
                        continue;

                    Phase1Stats stats = Stats(subset);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {period} | {tf} | {stats.N} | {stats.Worked236:P1} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Session");
            builder.AppendLine();
            builder.AppendLine("| Session (UTC) | Signals | worked236 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in samples
                         .GroupBy(s => SessionOf(s.EntryTime))
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Phase1Stats stats = Stats(group.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {group.Key} | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("London + New York only (07–21 UTC) vs the rest:");
            builder.AppendLine();
            builder.AppendLine("| Filter | Signals | worked236 | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            WriteFilterRow(builder, "London + New York",
                samples.Where(s => SessionOf(s.EntryTime) is "London 07–12" or "New York 12–21").ToList());
            WriteFilterRow(builder, "Asia + Late",
                samples.Where(s => SessionOf(s.EntryTime) is "Asia 00–07" or "Late 21–24").ToList());

            builder.AppendLine();
            builder.AppendLine("## Higher-timeframe context (m15 signals)");
            builder.AppendLine();
            builder.AppendLine("For every m15 signal the h1 series of the same symbol is read at " +
                               "the entry time; the feature is `(close - close[-20]) / ATR20`. " +
                               "It is a context screen, not a filter: h1 signals are excluded " +
                               "because there is no h4/d1 series in the archive.");
            builder.AppendLine();
            Dictionary<string, TestBarsProvider> h1Providers = LoadH1Providers(dataDir);
            BinTable(builder, "h1 trend at m15 entry",
                "Negative = h1 down, positive = h1 up; ±0.25 ATR is treated as flat.",
                samples.Where(s => s.TimeFrame == "m15").ToList(),
                s => HtfTrend(s, h1Providers),
                new[]
                {
                    (double.NegativeInfinity, -0.25d, "h1 down (< -0.25)"),
                    (-0.25d, 0.25d, "flat"),
                    (0.25d, double.PositiveInfinity, "h1 up (> 0.25)")
                });

            builder.AppendLine();
            builder.AppendLine("## Execution screen inside the exact corridor");
            builder.AppendLine();
            builder.AppendLine("Post-hoc on the exact signals. `Min R:R` is applied as a **drop** " +
                               "filter here; the live finder with `MinRiskRewardRatio` would wait " +
                               "for a better candle instead of dropping. Any winner needs an exact " +
                               "finder re-run with the real wait.");
            builder.AppendLine();
            builder.AppendLine("| Variant | Signals | Eligible | Resolved | Profitable | Avg netR | Avg R:R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");

            var executions = new List<WalkForwardExecution>
            {
                new("fixed R:R 1.0", _ => true, Phase1ExecKind.FixedRr, 1.0d, 0d),
                new("fixed R:R 1.5", _ => true, Phase1ExecKind.FixedRr, 1.5d, 0d),
                new("fixed R:R 2.0", _ => true, Phase1ExecKind.FixedRr, 2.0d, 0d),
                new("fixed R:R 3.0", _ => true, Phase1ExecKind.FixedRr, 3.0d, 0d),
                new("diagonal retrace 23.6%", _ => true, Phase1ExecKind.DiagonalRetrace, 0.236d, 0d),
                new("diagonal 23.6% + Min R:R 0.5", _ => true,
                    Phase1ExecKind.DiagonalRetrace, 0.236d, 0.5d),
                new("diagonal 23.6% + Min R:R 1.0", _ => true,
                    Phase1ExecKind.DiagonalRetrace, 0.236d, 1.0d),
                new("W3 retrace 38.2%", _ => true, Phase1ExecKind.Wave3Retrace, 0.382d, 0d),
                new("h1 only + diagonal 23.6%", s => s.TimeFrame == "h1",
                    Phase1ExecKind.DiagonalRetrace, 0.236d, 0d),
                new("London+NY + diagonal 23.6%", s =>
                        SessionOf(s.EntryTime) is "London 07–12" or "New York 12–21",
                    Phase1ExecKind.DiagonalRetrace, 0.236d, 0d),
                new("h1 + London+NY + diagonal 23.6%", s =>
                        s.TimeFrame == "h1" &&
                        SessionOf(s.EntryTime) is "London 07–12" or "New York 12–21",
                    Phase1ExecKind.DiagonalRetrace, 0.236d, 0d)
            };

            WalkForwardExecution? best = null;
            double bestNetR = double.NegativeInfinity;
            Phase1Stats bestStats = default!;
            int bestResolved = 0;
            double bestRr = 0;
            foreach (WalkForwardExecution execution in executions)
            {
                List<Sample> subset = samples.Where(execution.Filter).ToList();
                List<Phase1ExecResult> results = subset
                    .Select(s => SimulatePhase1(s, execution.Kind, execution.Param))
                    .Where(r => !r.Skipped)
                    .Where(r => execution.MinRr <= 0 || r.Rr >= execution.MinRr)
                    .ToList();
                List<Phase1ExecResult> resolved = results.Where(r => r.Resolved).ToList();
                double profitable = resolved.Count == 0
                    ? 0
                    : (double)resolved.Count(r => r.Profitable) / resolved.Count;
                double avgNetR = resolved.Count == 0 ? 0 : resolved.Average(r => r.NetR);
                double avgRr = resolved.Count == 0 ? 0 : resolved.Average(r => r.Rr);

                builder.AppendLine(FormattableString.Invariant(
                    $"| {execution.Name} | {subset.Count} | {results.Count} | {resolved.Count} | {profitable:P1} | {avgNetR:F3} | {avgRr:F2} |"));

                if (resolved.Count >= 50 && avgNetR > bestNetR)
                {
                    bestNetR = avgNetR;
                    best = execution;
                    bestStats = Stats(subset);
                    bestResolved = resolved.Count;
                    bestRr = avgRr;
                }
            }

            builder.AppendLine();
            builder.AppendLine("## What the walk-forward says");
            builder.AppendLine();
            List<(string Period, Phase1Stats Stats)> periodStats = periods
                .Select(p => (p, Stats(samples.Where(s => PeriodOf(s.EntryTime) == p).ToList())))
                .ToList();
            int positivePeriods = periodStats.Count(p => p.Stats.AvgNetR > 0);
            builder.AppendLine(FormattableString.Invariant(
                $"- Overall {overall.N} signals: worked236 {overall.Worked236:P1}, profitable {overall.Profitable:P1}, avg netR {overall.AvgNetR:F3}."));
            builder.AppendLine(FormattableString.Invariant(
                $"- Positive periods: {positivePeriods}/{periodStats.Count} ({string.Join(", ", periodStats.Select(p => $"{p.Period}: {p.Stats.AvgNetR:F3}R"))})."));

            Phase1Stats h1 = Stats(samples.Where(s => s.TimeFrame == "h1").ToList());
            Phase1Stats m15 = Stats(samples.Where(s => s.TimeFrame == "m15").ToList());
            builder.AppendLine(FormattableString.Invariant(
                $"- h1: {h1.N} signals, {h1.Profitable:P1} profitable, {h1.AvgNetR:F3}R. m15: {m15.N} signals, {m15.Profitable:P1} profitable, {m15.AvgNetR:F3}R."));

            if (best != null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- Best post-hoc execution row with at least 50 resolved: **{best.Name}** — {bestResolved} resolved, {bestStats.Profitable:P1} profitable, {bestNetR:F3}R, avg R:R {bestRr:F2}."));
            }

            builder.AppendLine("- The exact corridor is not yet a trading default: the overall " +
                               "expectancy is still below zero and the pair/timeframe dispersion is " +
                               "large. The next exact run should test the most promising context " +
                               "combination (h1 + London/NY + MinRiskRewardRatio wait), not add " +
                               "another geometry gate.");

            return builder.ToString();
        }

        private static void WriteFilterRow(
            StringBuilder builder, string name, IReadOnlyList<Sample> samples)
        {
            Phase1Stats stats = Stats(samples);
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {stats.N} | {stats.Worked236:P1} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
        }

        private static string PeriodOf(DateTime utc)
        {
            if (utc.Year <= 2020)
                return "2017–2020";
            if (utc.Year <= 2023)
                return "2021–2023";
            return "2024–2026";
        }

        private static Dictionary<string, TestBarsProvider> LoadH1Providers(string dataDir)
        {
            var result = new Dictionary<string, TestBarsProvider>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in H1_FILES)
            {
                string path = Path.Combine(dataDir, file);
                if (!File.Exists(path))
                    continue;

                string symbol = file[..file.IndexOf('_')];
                var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
                provider.LoadCandles(path);
                result[symbol] = provider;
            }

            return result;
        }

        private static double HtfTrend(
            Sample sample, IReadOnlyDictionary<string, TestBarsProvider> h1Providers)
        {
            if (sample.TimeFrame != "m15" ||
                !h1Providers.TryGetValue(sample.Symbol, out TestBarsProvider? h1))
            {
                return double.NaN;
            }

            int index = IndexAtOrBefore(h1, sample.EntryTime);
            if (index < 20)
                return double.NaN;

            double atr = 0;
            for (int i = index - 19; i <= index; i++)
                atr += h1.GetHighPrice(i) - h1.GetLowPrice(i);
            atr /= 20d;
            if (atr <= 1e-12)
                return double.NaN;

            return (h1.GetClosePrice(index) - h1.GetClosePrice(index - 20)) / atr;
        }

        private static int IndexAtOrBefore(TestBarsProvider provider, DateTime time)
        {
            int low = 0;
            int high = provider.Count - 1;
            int answer = -1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                if (provider.GetOpenTime(mid) <= time)
                {
                    answer = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return answer;
        }

        private sealed record WalkForwardExecution(
            string Name,
            Func<Sample, bool> Filter,
            Phase1ExecKind Kind,
            double Param,
            double MinRr);
    }
}
