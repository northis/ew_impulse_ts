using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace TradeKit.Tests
{
    /// <summary>
    /// Phase 1 of the diagonal geometry research (DIAGONAL.md §9.19): holdout validation
    /// of the Phase 0 candidates plus a post-hoc execution/context grid.
    /// <para>
    /// The test reuses the Phase 0 permissive sample. Geometry/context rules are evaluated
    /// on the train/test split; execution variants are simulated on the same signals from
    /// the stored entry/SL/wave-5 values. This is deliberately cheaper than re-running the
    /// finder for every TP mode; the report marks the execution grid as post-hoc.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_phase1.md")]
        [Category("Research")]
        public void DiagonalPhase1_HoldoutAndExecution_Report()
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

            var samples = new List<Sample>();
            var fileStats = new List<FileStat>();
            long totalBars = 0;
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                file =>
                {
                    FileRun run = RunFile(Path.Combine(dataDir, file), file);
                    lock (sync)
                    {
                        samples.AddRange(run.Samples);
                        fileStats.Add(run.Stat);
                        totalBars += run.Stat.Bars;
                    }

                    TestContext.Out.WriteLine(
                        $"{file}: bars={run.Stat.Bars}, signals={run.Stat.Signals}");
                });

            Assert.That(samples, Is.Not.Empty,
                "The permissive baseline produced no diagonal setups.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_phase1.md");
            File.WriteAllText(reportPath, BuildPhase1Report(samples, fileStats, totalBars));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static string BuildPhase1Report(
            IReadOnlyList<Sample> samples, IReadOnlyList<FileStat> fileStats, long totalBars)
        {
            var builder = new StringBuilder();
            List<Sample> train = samples.Where(s => s.EntryTime.Year <= 2022).ToList();
            List<Sample> test = samples.Where(s => s.EntryTime.Year >= 2023).ToList();
            Phase1Stats trainBase = Stats(train);
            Phase1Stats testBase = Stats(test);

            builder.AppendLine("# Diagonal geometry — Phase 1");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Files: {fileStats.Count}, bars: {totalBars}, signals: {samples.Count}. Train 2017–2022: {train.Count}; test 2023–2026: {test.Count}."));
            builder.AppendLine();
            builder.AppendLine("The sample is the Phase 0 permissive baseline: every beauty gate " +
                               "is off, only structural Elliott rules and duration sanity remain. " +
                               "Geometry/context rules below are post-hoc filters on that sample. " +
                               "The execution grid is also post-hoc: it simulates TP/SL on the same " +
                               "signals from the stored entry, stop and wave-5 values, so it is a " +
                               "screening tool, not an exact finder re-run.");
            builder.AppendLine();

            builder.AppendLine("## Baseline by split");
            builder.AppendLine();
            builder.AppendLine("| Split | Signals | worked236 | worked382 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            WriteStatsRow(builder, "train", trainBase);
            WriteStatsRow(builder, "test", testBase);

            var rules = new List<Phase1Rule>
            {
                new("W2/W1 >= 0.5", s => s.W2Retr >= 0.5d),
                new("W2/W1 >= 0.786", s => s.W2Retr >= 0.786d),
                new("W3/W1 >= 0.5", s => s.W3W1 >= 0.5d),
                new("W3/W1 >= 0.7", s => s.W3W1 >= 0.7d),
                new("W4/W3 <= 0.382", s => s.W4W3 <= 0.382d),
                new("W4/W3 <= 0.30", s => s.W4W3 <= 0.30d),
                new("c >= 0.8", s => s.C >= 0.8d),
                new("c >= 1.2", s => s.C >= 1.2d),
                new("c >= 2.0", s => s.C >= 2.0d),
                new("A(2–4) <= 0.005", s => s.A234 <= 0.005d),
                new("A(max) <= 0.02", s => s.AMax <= 0.02d),
                new("A5 early <= 0.20", s => s.A5Early <= 0.20d),
                new("max outside run <= 2", s => s.MaxOutsideRun <= 2),
                new("Activity W3/W1 <= 0.8", s => s.ActivityW3W1 <= 0.8d),
                new("Activity W5/W3 <= 0.8", s => s.ActivityW5W3 <= 0.8d),
                new("W2/W1 >= 0.5 & W4/W3 <= 0.382", s =>
                    s.W2Retr >= 0.5d && s.W4W3 <= 0.382d),
                new("W2/W1 >= 0.5 & W4/W3 <= 0.382 & c >= 1.0", s =>
                    s.W2Retr >= 0.5d && s.W4W3 <= 0.382d && s.C >= 1.0d),
                new("W2/W1 >= 0.5 & W4/W3 <= 0.382 & A5 early <= 0.20", s =>
                    s.W2Retr >= 0.5d && s.W4W3 <= 0.382d && s.A5Early <= 0.20d),
                new("W2/W1 >= 0.5 & W4/W3 <= 0.382 & Activity W5/W3 <= 0.8", s =>
                    s.W2Retr >= 0.5d && s.W4W3 <= 0.382d && s.ActivityW5W3 <= 0.8d)
            };

            builder.AppendLine();
            builder.AppendLine("## Rule holdout");
            builder.AppendLine();
            builder.AppendLine("Coverage is relative to the split's baseline. `Pairs+` counts files " +
                               "where the rule improves the profitable share by at least 2 pp among " +
                               "resolved setups, with at least 10 setups in both the baseline and the " +
                               "rule subset. A rule that only improves `worked236` but not " +
                               "`profitable`/`netR` is a structural description, not a trading filter.");
            builder.AppendLine();
            builder.AppendLine("| Rule | Train N | Train cov | Train worked | Train profit | Train netR | " +
                               "Test N | Test cov | Test worked | Test profit | Test netR | Pairs+ |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (Phase1Rule rule in rules)
            {
                List<Sample> trainSubset = train.Where(rule.Predicate).ToList();
                List<Sample> testSubset = test.Where(rule.Predicate).ToList();
                Phase1Stats tr = Stats(trainSubset);
                Phase1Stats te = Stats(testSubset);
                int pairs = PairsImproved(samples, rule.Predicate);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {rule.Name} | {tr.N} | {Cov(tr.N, train.Count):P0} | {tr.Worked236:P1} | {tr.Profitable:P1} | {tr.AvgNetR:F3} | {te.N} | {Cov(te.N, test.Count):P0} | {te.Worked236:P1} | {te.Profitable:P1} | {te.AvgNetR:F3} | {pairs} |"));
            }

            List<Phase1Rule> eligible = rules
                .Where(r =>
                {
                    Phase1Stats st = Stats(train.Where(r.Predicate).ToList());
                    return st.N >= 100 && st.N >= 0.25 * train.Count;
                })
                .ToList();

            Phase1Rule? bestNet = eligible
                .OrderByDescending(r => Stats(train.Where(r.Predicate).ToList()).AvgNetR)
                .FirstOrDefault();
            Phase1Rule? bestWorked = eligible
                .OrderByDescending(r => Stats(train.Where(r.Predicate).ToList()).Worked236)
                .FirstOrDefault();
            Phase1Rule? bestProfit = eligible
                .OrderByDescending(r => Stats(train.Where(r.Predicate).ToList()).Profitable)
                .FirstOrDefault();

            builder.AppendLine();
            builder.AppendLine("## Selected on train");
            builder.AppendLine();
            builder.AppendLine("Eligibility: at least 100 train signals and at least 25% train " +
                               "coverage. The test column is the only one that matters for shipping.");
            builder.AppendLine();
            builder.AppendLine("| Selected by | Rule | Train N | Train worked | Train profit | Train netR | " +
                               "Test N | Test worked | Test profit | Test netR | Δ worked | Δ profit | Δ netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            WriteSelectedRule(builder, "max netR", bestNet, train, test, trainBase, testBase);
            WriteSelectedRule(builder, "max worked236", bestWorked, train, test, trainBase, testBase);
            WriteSelectedRule(builder, "max profitable", bestProfit, train, test, trainBase, testBase);

            builder.AppendLine();
            builder.AppendLine("## Per-pair stability");
            builder.AppendLine();
            if (bestNet == null)
            {
                builder.AppendLine("No eligible rule.");
            }
            else
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"Rule selected by train netR: **{bestNet.Name}**."));
                builder.AppendLine();
                builder.AppendLine("| File | Base N | Rule N | Base worked | Rule worked | Base profit | Rule profit | Base netR | Rule netR |");
                builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
                foreach (IGrouping<string, Sample> group in samples
                             .GroupBy(s => s.File)
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    List<Sample> baseList = group.ToList();
                    List<Sample> ruleList = baseList.Where(bestNet.Predicate).ToList();
                    Phase1Stats b = Stats(baseList);
                    Phase1Stats r = Stats(ruleList);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| `{group.Key}` | {b.N} | {r.N} | {b.Worked236:P1} | {r.Worked236:P1} | {b.Profitable:P1} | {r.Profitable:P1} | {b.AvgNetR:F3} | {r.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Execution grid (post-hoc)");
            builder.AppendLine();
            builder.AppendLine("Simulated on the Phase 0 signals from entry/SL/wave-5 values. " +
                               "`Resolved` means TP or SL inside the Phase 0 horizon; `Skipped` means " +
                               "the retrace target was already behind the entry. Costs are the same " +
                               "round-trip charge as in Phase 0. This is a screen: an exact finder " +
                               "re-run is required before shipping.");
            builder.AppendLine();
            builder.AppendLine("| Variant | Signals | Eligible | Resolved | Profitable | Avg netR | Avg R:R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");

            var execution = new List<Phase1Execution>
            {
                new("fixed R:R 1.0", _ => true, Phase1ExecKind.FixedRr, 1.0d),
                new("fixed R:R 1.5", _ => true, Phase1ExecKind.FixedRr, 1.5d),
                new("fixed R:R 2.0", _ => true, Phase1ExecKind.FixedRr, 2.0d),
                new("fixed R:R 3.0", _ => true, Phase1ExecKind.FixedRr, 3.0d),
                new("diagonal retrace 23.6%", _ => true, Phase1ExecKind.DiagonalRetrace, 0.236d),
                new("W3 retrace 38.2%", _ => true, Phase1ExecKind.Wave3Retrace, 0.382d),
                new("W5 ≥ 78.6% + fixed 1.5", s => s.W5W3 >= 0.786d, Phase1ExecKind.FixedRr, 1.5d),
                new("W5 ≥ 78.6% + diagonal 23.6%", s => s.W5W3 >= 0.786d,
                    Phase1ExecKind.DiagonalRetrace, 0.236d),
                new("W2/W1 ≥ 0.5 + fixed 1.5", s => s.W2Retr >= 0.5d,
                    Phase1ExecKind.FixedRr, 1.5d),
                new("W2/W1 ≥ 0.5 + diagonal 23.6%", s => s.W2Retr >= 0.5d,
                    Phase1ExecKind.DiagonalRetrace, 0.236d),
                new("W2/W1 ≥ 0.5 & W4/W3 ≤ 0.382 + fixed 1.5", s =>
                        s.W2Retr >= 0.5d && s.W4W3 <= 0.382d,
                    Phase1ExecKind.FixedRr, 1.5d),
                new("W2/W1 ≥ 0.5 & W4/W3 ≤ 0.382 + diagonal 23.6%", s =>
                        s.W2Retr >= 0.5d && s.W4W3 <= 0.382d,
                    Phase1ExecKind.DiagonalRetrace, 0.236d),
                new("Activity W5/W3 ≤ 0.8 + fixed 1.5", s => s.ActivityW5W3 <= 0.8d,
                    Phase1ExecKind.FixedRr, 1.5d)
            };

            Phase1Execution? bestExecution = null;
            double bestExecutionNetR = double.NegativeInfinity;
            double bestExecutionProfitable = 0;
            double bestExecutionRr = 0;
            int bestExecutionResolved = 0;
            foreach (Phase1Execution executionVariant in execution)
            {
                List<Sample> subset = samples.Where(executionVariant.Filter).ToList();
                List<Phase1ExecResult> results = subset
                    .Select(s => SimulatePhase1(s, executionVariant.Kind, executionVariant.Param))
                    .ToList();
                List<Phase1ExecResult> eligibleResults = results.Where(r => !r.Skipped).ToList();
                List<Phase1ExecResult> resolved = eligibleResults.Where(r => r.Resolved).ToList();
                double profitable = resolved.Count == 0
                    ? 0
                    : (double)resolved.Count(r => r.Profitable) / resolved.Count;
                double avgNetR = resolved.Count == 0 ? 0 : resolved.Average(r => r.NetR);
                double avgRr = resolved.Count == 0 ? 0 : resolved.Average(r => r.Rr);

                builder.AppendLine(FormattableString.Invariant(
                    $"| {executionVariant.Name} | {subset.Count} | {eligibleResults.Count} | {resolved.Count} | {profitable:P1} | {avgNetR:F3} | {avgRr:F2} |"));

                if (resolved.Count >= 100 && avgNetR > bestExecutionNetR)
                {
                    bestExecutionNetR = avgNetR;
                    bestExecution = executionVariant;
                    bestExecutionProfitable = profitable;
                    bestExecutionRr = avgRr;
                    bestExecutionResolved = resolved.Count;
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Activity / fuel proxy");
            builder.AppendLine();
            builder.AppendLine("Median bar range per wave, used as a volume proxy because the old " +
                               "archive has no tick volume. A contracting wedge should show " +
                               "`W5/W3 < 1` and `W3/W1 < 1` if the \"fuel exhausted\" hypothesis holds.");
            builder.AppendLine();
            BinTable(builder, "Activity W3/W1",
                "Median range of wave 3 over wave 1.",
                samples, s => s.ActivityW3W1,
                new[]
                {
                    (0d, 0.5d, "0–0.5"),
                    (0.5d, 0.8d, "0.5–0.8"),
                    (0.8d, 1.0d, "0.8–1.0"),
                    (1.0d, 1.5d, "1.0–1.5"),
                    (1.5d, double.PositiveInfinity, "1.5+")
                });
            BinTable(builder, "Activity W5/W3",
                "Median range of wave 5 over wave 3.",
                samples, s => s.ActivityW5W3,
                new[]
                {
                    (0d, 0.5d, "0–0.5"),
                    (0.5d, 0.8d, "0.5–0.8"),
                    (0.8d, 1.0d, "0.8–1.0"),
                    (1.0d, 1.5d, "1.0–1.5"),
                    (1.5d, double.PositiveInfinity, "1.5+")
                });

            builder.AppendLine("## What Phase 1 says");
            builder.AppendLine();
            Phase1Stats bestTest = bestNet == null
                ? testBase
                : Stats(test.Where(bestNet.Predicate).ToList());
            bool survives = bestNet != null &&
                            bestTest.AvgNetR > testBase.AvgNetR + 0.05 &&
                            bestTest.Profitable > testBase.Profitable + 0.05 &&
                            PairsImproved(samples, bestNet.Predicate) >= 2;

            if (survives)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- The train-selected rule **{bestNet!.Name}** survives the test split: netR {bestTest.AvgNetR:F3} vs {testBase.AvgNetR:F3}, profitable {bestTest.Profitable:P1} vs {testBase.Profitable:P1}, pairs+ {PairsImproved(samples, bestNet.Predicate)}. Re-run the exact finder with this rule before shipping; post-hoc filters can differ from the live candidate pool."));
            }
            else
            {
                builder.AppendLine("- No geometry/context rule selected on train clears the test " +
                                   "bar (netR +0.05R, profitable +5 pp, at least two pairs). " +
                                   "Do not ship a new geometry gate yet.");
            }

            List<Phase1Rule> positiveTest = rules
                .Where(r => Stats(test.Where(r.Predicate).ToList()).AvgNetR > 0)
                .OrderByDescending(r => Stats(test.Where(r.Predicate).ToList()).AvgNetR)
                .ToList();
            if (positiveTest.Count > 0)
            {
                builder.AppendLine("- Rules positive on test but **negative on train** are holdout " +
                                   "failures, not discoveries:");
                foreach (Phase1Rule rule in positiveTest)
                {
                    Phase1Stats tr = Stats(train.Where(rule.Predicate).ToList());
                    Phase1Stats te = Stats(test.Where(rule.Predicate).ToList());
                    builder.AppendLine(FormattableString.Invariant(
                        $"  - `{rule.Name}`: train {tr.AvgNetR:F3}R / {tr.Profitable:P1} profitable ({tr.N} signals), test {te.AvgNetR:F3}R / {te.Profitable:P1} profitable ({te.N} signals)."));
                }
            }

            if (bestExecution != null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- The best post-hoc execution row with at least 100 resolved setups is **{bestExecution.Name}**: {bestExecutionResolved} resolved, profitable {bestExecutionProfitable:P1}, avg netR {bestExecutionNetR:F3}, avg R:R {bestExecutionRr:F2}. This is the closest to break-even and the best candidate for an exact finder re-run."));
            }
            else
            {
                builder.AppendLine("- No post-hoc execution row has at least 100 resolved setups.");
            }

            builder.AppendLine("- Geometry/context filters in this report are applied after the " +
                               "signal. They can rank emitted setups, but they do not reproduce the " +
                               "finder's candidate-pool interactions (D-INSIDE/D-W4 gates, tpSlHit, " +
                               "deduplication). Any survivor needs an exact re-run.");

            return builder.ToString();
        }

        private static void WriteStatsRow(StringBuilder builder, string name, Phase1Stats stats)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {stats.N} | {stats.Worked236:P1} | {stats.Worked382:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
        }

        private static void WriteSelectedRule(
            StringBuilder builder,
            string selectedBy,
            Phase1Rule? rule,
            IReadOnlyList<Sample> train,
            IReadOnlyList<Sample> test,
            Phase1Stats trainBase,
            Phase1Stats testBase)
        {
            if (rule == null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {selectedBy} | — | — | — | — | — | — | — | — | — | — | — | — |"));
                return;
            }

            Phase1Stats tr = Stats(train.Where(rule.Predicate).ToList());
            Phase1Stats te = Stats(test.Where(rule.Predicate).ToList());
            builder.AppendLine(FormattableString.Invariant(
                $"| {selectedBy} | {rule.Name} | {tr.N} | {tr.Worked236:P1} | {tr.Profitable:P1} | {tr.AvgNetR:F3} | {te.N} | {te.Worked236:P1} | {te.Profitable:P1} | {te.AvgNetR:F3} | {te.Worked236 - testBase.Worked236:+0.0%;-0.0%} | {te.Profitable - testBase.Profitable:+0.0%;-0.0%} | {te.AvgNetR - testBase.AvgNetR:+0.000;-0.000} |"));
        }

        private static Phase1Stats Stats(IReadOnlyList<Sample> subset)
        {
            List<Sample> resolved = subset.Where(s => s.Resolved).ToList();
            return new Phase1Stats(
                subset.Count,
                resolved.Count,
                subset.Count == 0 ? 0 : Share(subset, s => s.Worked236),
                subset.Count == 0 ? 0 : Share(subset, s => s.Worked382),
                resolved.Count == 0 ? 0 : Share(resolved, s => s.Profitable),
                resolved.Count == 0 ? 0 : resolved.Average(s => s.NetR));
        }

        private static double Cov(int subset, int total) =>
            total == 0 ? 0 : (double)subset / total;

        /// <summary>
        /// Files where the rule improves the profitable share by at least 2 pp, requiring at
        /// least 10 setups on both sides.
        /// </summary>
        private static int PairsImproved(IReadOnlyList<Sample> all, Func<Sample, bool> rule)
        {
            int improved = 0;
            foreach (IGrouping<string, Sample> group in all.GroupBy(s => s.File))
            {
                List<Sample> baseList = group.ToList();
                List<Sample> ruleList = baseList.Where(rule).ToList();
                List<Sample> baseResolved = baseList.Where(s => s.Resolved).ToList();
                List<Sample> ruleResolved = ruleList.Where(s => s.Resolved).ToList();
                if (baseResolved.Count < 10 || ruleResolved.Count < 10)
                    continue;

                double baseProfit = Share(baseResolved, s => s.Profitable);
                double ruleProfit = Share(ruleResolved, s => s.Profitable);
                if (ruleProfit > baseProfit + 0.02d)
                    improved++;
            }

            return improved;
        }

        private static Phase1ExecResult SimulatePhase1(
            Sample sample, Phase1ExecKind kind, double param)
        {
            double risk = Math.Abs(sample.Entry - sample.Sl);
            if (risk <= 0)
                return Phase1ExecResult.SkippedResult();

            double tp;
            if (kind == Phase1ExecKind.FixedRr)
            {
                tp = sample.IsUpDiagonal
                    ? sample.Entry - param * risk
                    : sample.Entry + param * risk;
            }
            else
            {
                double depth = kind == Phase1ExecKind.DiagonalRetrace
                    ? param * Math.Abs(sample.V5 - sample.V0)
                    : param * sample.W3;
                tp = sample.IsUpDiagonal
                    ? sample.V5 - depth
                    : sample.V5 + depth;

                bool behindEntry = sample.IsUpDiagonal
                    ? tp >= sample.Entry
                    : tp <= sample.Entry;
                if (behindEntry)
                    return Phase1ExecResult.SkippedResult();
            }

            double rr = Math.Abs(tp - sample.Entry) / risk;
            int end = Math.Min(sample.Provider.Count - 1, sample.SignalBar + sample.Horizon);
            for (int i = sample.SignalBar + 1; i <= end; i++)
            {
                double high = sample.Provider.GetHighPrice(i);
                double low = sample.Provider.GetLowPrice(i);

                bool stopHit = sample.IsUpDiagonal
                    ? high >= sample.Sl
                    : low <= sample.Sl;
                if (stopHit)
                {
                    return Resolved(sample, sample.Sl, risk, rr, "SL");
                }

                bool targetHit = sample.IsUpDiagonal
                    ? low <= tp
                    : high >= tp;
                if (targetHit)
                {
                    return Resolved(sample, tp, risk, rr, "TP");
                }
            }

            return new Phase1ExecResult(false, false, false, 0, 0, rr, 0, "NONE");
        }

        private static Phase1ExecResult Resolved(
            Sample sample, double exit, double risk, double rr, string outcome)
        {
            double grossR = sample.IsUpDiagonal
                ? (sample.Entry - exit) / risk
                : (exit - sample.Entry) / risk;
            double costR = sample.Entry * COST_RATE / risk;
            double netR = grossR - costR;
            return new Phase1ExecResult(
                true, netR > 0, false, grossR, netR, rr, exit, outcome);
        }

        private sealed record Phase1Rule(string Name, Func<Sample, bool> Predicate);

        private sealed record Phase1Stats(
            int N,
            int Resolved,
            double Worked236,
            double Worked382,
            double Profitable,
            double AvgNetR);

        private enum Phase1ExecKind
        {
            FixedRr,
            DiagonalRetrace,
            Wave3Retrace
        }

        private sealed record Phase1Execution(
            string Name,
            Func<Sample, bool> Filter,
            Phase1ExecKind Kind,
            double Param);

        private sealed record Phase1ExecResult(
            bool Resolved,
            bool Profitable,
            bool Skipped,
            double GrossR,
            double NetR,
            double Rr,
            double Exit,
            string Outcome)
        {
            public static Phase1ExecResult SkippedResult() =>
                new(false, false, true, 0, 0, 0, 0, "SKIP");
        }
    }
}
