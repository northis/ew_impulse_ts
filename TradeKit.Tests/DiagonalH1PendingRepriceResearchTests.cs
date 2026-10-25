using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Tests
{
    /// <summary>
    /// h1 validation of the pending limit on the 1-3 generatrix (DIAGONAL.md §9.25).
    /// The order is placed only after the wedge is identified, then re-priced on every
    /// candle because the line moves with time; it fills at the line (or at the bar open
    /// when the bar gaps beyond it). The sample is split into the Phase-0/1 tuning pairs
    /// and the fresh pairs of §9.23, so the pending result is checked out of sample.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_h1_pending_reprice.md")]
        [Category("Research")]
        public void DiagonalH1PendingReprice_Report()
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
                new ExactConfig("V(3) break", 0.5d, 0.382d, 0d,
                    DiagonalTriggerMode.WAVE3_BREAK),
                new ExactConfig("1-3 pending", 0.5d, 0.382d, 0d,
                    DiagonalTriggerMode.LINE13_PENDING),
                new ExactConfig("1-3 pending + minRR 0.5", 0.5d, 0.382d, 0.5d,
                    DiagonalTriggerMode.LINE13_PENDING)
            };

            var tuning = new List<TriggerRun>();
            var fresh = new List<TriggerRun>();
            foreach (ExactConfig config in configs)
            {
                var tuningDiag = new Dictionary<string, int>();
                var tuningReprice = new Dictionary<DateTime, int>();
                List<Sample> tuningSamples = RunExactAll(
                    dataDir, tuningFiles, config, tuningDiag, 10, tuningReprice);
                tuning.Add(new TriggerRun(config, tuningSamples, tuningDiag));

                var freshDiag = new Dictionary<string, int>();
                var freshReprice = new Dictionary<DateTime, int>();
                List<Sample> freshSamples = RunExactAll(
                    dataDir, freshFiles, config, freshDiag, 10, freshReprice);
                fresh.Add(new TriggerRun(config, freshSamples, freshDiag));

                TestContext.Out.WriteLine(
                    $"{config.Name}: tuning={tuningSamples.Count}, fresh={freshSamples.Count}");
            }

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_h1_pending_reprice.md");
            File.WriteAllText(reportPath, BuildH1PendingRepriceReport(
                tuning, fresh, tuningFiles.Count, freshFiles.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_h1_pending_reprice_samples.csv");
            File.WriteAllText(csvPath, BuildH1PendingRepriceCsv(tuning, fresh));
            TestContext.Out.WriteLine($"Wrote {csvPath}");
        }

        private static string BuildH1PendingRepriceReport(
            IReadOnlyList<TriggerRun> tuning,
            IReadOnlyList<TriggerRun> fresh,
            int tuningFileCount,
            int freshFileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal h1 — pending limit on the 1-3 generatrix");
            builder.AppendLine();
            AppendInv(builder, $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            builder.AppendLine();
            AppendInv(builder, $"Tuning: {tuningFileCount} h1 pairs from Phase 0/1. Fresh: {freshFileCount} h1 pairs from §9.23 that never took part in the threshold search. Exact W2/W4 corridor (`W2/W1 >= 0.5`, `W4/W3 <= 0.382`), 23.6% target. The pending limit is placed only after the wedge is identified, re-priced on every candle and filled at the line (or at the bar open when it gaps beyond the line). `reprice` is the number of candles between the first tradable bar and the fill; `first-bar` is the share of fills on that first bar.");
            builder.AppendLine();

            builder.AppendLine("## Overall");
            builder.AppendLine();
            builder.AppendLine("| Set | Config | Signals | worked236 | resolved | profitable | avg netR | avg R:R | median reprice | mean reprice | first-bar fills |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach ((string setName, IReadOnlyList<TriggerRun> runs) in new[]
                     {
                         ("tuning", tuning),
                         ("fresh", fresh)
                     })
            {
                foreach (TriggerRun run in runs)
                {
                    Phase1Stats stats = Stats(run.Samples);
                    bool isPending = run.Config.TriggerMode ==
                                     DiagonalTriggerMode.LINE13_PENDING;
                    List<Sample> repriced = run.Samples
                        .Where(s => s.RepriceCount >= 0)
                        .ToList();
                    string medianReprice = isPending
                        ? $"{Median(repriced.Select(s => s.RepriceCount)):F0}"
                        : "—";
                    string meanReprice = isPending && repriced.Count > 0
                        ? $"{repriced.Average(s => s.RepriceCount):F1}"
                        : "—";
                    string firstBar = isPending
                        ? $"{Share(repriced, s => s.RepriceCount == 0):P1}"
                        : "—";
                    AppendInv(builder, $"| {setName} | {run.Config.Name} | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {AvgRr(run.Samples):F2} | {medianReprice} | {meanReprice} | {firstBar} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Delta vs the V(3) baseline");
            builder.AppendLine();
            builder.AppendLine("| Set | Config | Δ signals | Δ profitable | Δ netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach ((string setName, IReadOnlyList<TriggerRun> runs) in new[]
                     {
                         ("tuning", tuning),
                         ("fresh", fresh)
                     })
            {
                Phase1Stats baseline = Stats(H1RunByName(runs, "V(3) break").Samples);
                foreach (TriggerRun run in runs.Where(r => r.Config.Name != "V(3) break"))
                {
                    Phase1Stats stats = Stats(run.Samples);
                    AppendInv(builder, $"| {setName} | {run.Config.Name} | {stats.N - baseline.N:+0;-0} | {stats.Profitable - baseline.Profitable:+0.0%;-0.0%} | {stats.AvgNetR - baseline.AvgNetR:+0.000;-0.000} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh walk-forward by period");
            builder.AppendLine();
            builder.AppendLine("| Config | Period | Signals | worked236 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (TriggerRun run in fresh)
            {
                foreach (string period in new[] { "2017–2020", "2021–2023", "2024–2026" })
                {
                    List<Sample> subset = run.Samples
                        .Where(s => PeriodOf(s.EntryTime) == period)
                        .ToList();
                    Phase1Stats stats = Stats(subset);
                    AppendInv(builder, $"| {run.Config.Name} | {period} | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by year");
            builder.AppendLine();
            builder.AppendLine("| Config | Year | Signals | worked236 | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");
            foreach (TriggerRun run in fresh)
            {
                foreach (IGrouping<int, Sample> group in run.Samples
                             .GroupBy(s => s.EntryTime.Year)
                             .OrderBy(g => g.Key))
                {
                    Phase1Stats stats = Stats(group.ToList());
                    AppendInv(builder, $"| {run.Config.Name} | {group.Key} | {stats.N} | {stats.Worked236:P1} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by pair");
            builder.AppendLine();
            builder.AppendLine("| File | V(3) N | V(3) profit | V(3) netR | pending N | pending profit | pending netR | pending+0.5 N | pending+0.5 profit | pending+0.5 netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            string[] pairFiles = fresh
                .SelectMany(r => r.Samples)
                .Select(s => s.File)
                .Distinct()
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();
            foreach (string file in pairFiles)
            {
                Phase1Stats baseline = Stats(H1SamplesOf(fresh, "V(3) break", file));
                Phase1Stats pending = Stats(H1SamplesOf(fresh, "1-3 pending", file));
                Phase1Stats pendingRr = Stats(H1SamplesOf(
                    fresh, "1-3 pending + minRR 0.5", file));
                AppendInv(builder, $"| `{file}` | {baseline.N} | {baseline.Profitable:P1} | {baseline.AvgNetR:F3} | {pending.N} | {pending.Profitable:P1} | {pending.AvgNetR:F3} | {pendingRr.N} | {pendingRr.Profitable:P1} | {pendingRr.AvgNetR:F3} |");
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by session");
            builder.AppendLine();
            builder.AppendLine("| Config | Session | Signals | worked236 | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");
            foreach (TriggerRun run in fresh)
            {
                foreach (string session in new[] { "London/NY", "Asia/Late" })
                {
                    List<Sample> subset = run.Samples
                        .Where(s => SessionGroup(s.EntryTime) == session)
                        .ToList();
                    Phase1Stats stats = Stats(subset);
                    AppendInv(builder, $"| {run.Config.Name} | {session} | {stats.N} | {stats.Worked236:P1} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh — outcome by re-pricing count");
            builder.AppendLine();
            builder.AppendLine("Does waiting for a re-priced limit help or hurt? Buckets are the number of candles between the first tradable bar and the fill. For `pending + minRR 0.5` the count also includes the candles spent waiting for the R:R gate, not only the line re-pricing.");
            builder.AppendLine();
            builder.AppendLine("| Config | Reprice | Signals | worked236 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (TriggerRun run in fresh.Where(r => r.Config.TriggerMode ==
                                                       DiagonalTriggerMode.LINE13_PENDING))
            {
                foreach (string bucket in new[] { "0", "1–2", "3–5", "6–10", ">10" })
                {
                    List<Sample> subset = run.Samples
                        .Where(s => H1RepriceBucket(s.RepriceCount) == bucket)
                        .ToList();
                    Phase1Stats stats = Stats(subset);
                    AppendInv(builder, $"| {run.Config.Name} | {bucket} | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## What this validation says");
            builder.AppendLine();
            TriggerRun tuningBaseRun = H1RunByName(tuning, "V(3) break");
            TriggerRun tuningPendingRun = H1RunByName(tuning, "1-3 pending");
            TriggerRun tuningPendingRrRun = H1RunByName(tuning, "1-3 pending + minRR 0.5");
            TriggerRun freshBaseRun = H1RunByName(fresh, "V(3) break");
            TriggerRun freshPendingRun = H1RunByName(fresh, "1-3 pending");
            TriggerRun freshPendingRrRun = H1RunByName(fresh, "1-3 pending + minRR 0.5");

            Phase1Stats tuningBase = Stats(tuningBaseRun.Samples);
            Phase1Stats tuningPending = Stats(tuningPendingRun.Samples);
            Phase1Stats tuningPendingRr = Stats(tuningPendingRrRun.Samples);
            Phase1Stats freshBase = Stats(freshBaseRun.Samples);
            Phase1Stats freshPending = Stats(freshPendingRun.Samples);
            Phase1Stats freshPendingRr = Stats(freshPendingRrRun.Samples);
            AppendInv(builder, $"- **Tuning:** V(3) {tuningBase.N} / {tuningBase.Profitable:P1} / {tuningBase.AvgNetR:F3}R; pending {tuningPending.N} / {tuningPending.Profitable:P1} / {tuningPending.AvgNetR:F3}R; pending+minRR 0.5 {tuningPendingRr.N} / {tuningPendingRr.Profitable:P1} / {tuningPendingRr.AvgNetR:F3}R.");
            AppendInv(builder, $"- **Fresh:** V(3) {freshBase.N} / {freshBase.Profitable:P1} / {freshBase.AvgNetR:F3}R; pending {freshPending.N} / {freshPending.Profitable:P1} / {freshPending.AvgNetR:F3}R; pending+minRR 0.5 {freshPendingRr.N} / {freshPendingRr.Profitable:P1} / {freshPendingRr.AvgNetR:F3}R.");

            List<Sample> freshRepriced = freshPendingRun.Samples
                .Where(s => s.RepriceCount >= 0)
                .ToList();
            double firstBarPending = Stats(freshPendingRun.Samples
                .Where(s => s.RepriceCount == 0).ToList()).AvgNetR;
            double delayedPending = Stats(freshPendingRun.Samples
                .Where(s => s.RepriceCount is >= 3 and <= 10).ToList()).AvgNetR;
            double firstBarPendingRr = Stats(freshPendingRrRun.Samples
                .Where(s => s.RepriceCount == 0).ToList()).AvgNetR;
            double delayedPendingRr = Stats(freshPendingRrRun.Samples
                .Where(s => s.RepriceCount is >= 3 and <= 10).ToList()).AvgNetR;

            AppendInv(builder, $"- **Fresh re-pricing (pure pending):** median {Median(freshRepriced.Select(s => s.RepriceCount)):F0} candles, mean {freshRepriced.Average(s => s.RepriceCount):F1}; {Share(freshRepriced, s => s.RepriceCount == 0):P1} fill on the first tradable bar.");
            AppendInv(builder, $"- **Re-pricing vs outcome:** the first-bar bucket gives {firstBarPending:F3}R (pure pending) and {firstBarPendingRr:F3}R (pending+0.5); the 3–10 re-pricing bucket gives {delayedPending:F3}R and {delayedPendingRr:F3}R. The edge is in letting the limit wait for the line to move, not in entering immediately.");

            TriggerRun bestFreshRun = freshPendingRr.AvgNetR >= freshPending.AvgNetR
                ? freshPendingRrRun
                : freshPendingRun;
            Phase1Stats bestFresh = Stats(bestFreshRun.Samples);
            int bestPositivePeriods = new[] { "2017–2020", "2021–2023", "2024–2026" }
                .Count(period => Stats(bestFreshRun.Samples
                    .Where(s => PeriodOf(s.EntryTime) == period).ToList()).AvgNetR > 0);
            int bestPositivePairs = bestFreshRun.Samples
                .GroupBy(s => s.File)
                .Where(g => g.Count() >= 10)
                .Count(g => Stats(g.ToList()).AvgNetR > 0);
            int bestEligiblePairs = bestFreshRun.Samples
                .GroupBy(s => s.File)
                .Count(g => g.Count() >= 10);
            int bestBetterYears = bestFreshRun.Samples
                .Select(s => s.EntryTime.Year)
                .Distinct()
                .Count(year => Stats(bestFreshRun.Samples
                    .Where(s => s.EntryTime.Year == year).ToList()).AvgNetR >
                    Stats(freshBaseRun.Samples
                        .Where(s => s.EntryTime.Year == year).ToList()).AvgNetR);
            int bestYears = bestFreshRun.Samples
                .Select(s => s.EntryTime.Year)
                .Distinct()
                .Count();

            AppendInv(builder, $"- **Fresh stability (best config `{bestFreshRun.Config.Name}`):** better than V(3) in {bestBetterYears}/{bestYears} years, positive in {bestPositivePeriods}/3 periods, and positive in {bestPositivePairs}/{bestEligiblePairs} pairs with at least 10 signals.");

            bool freshImproves = bestFresh.AvgNetR > freshBase.AvgNetR + 0.02d;
            bool freshPositive = bestFresh.AvgNetR > 0d;
            bool freshRobust = bestPositivePeriods >= 2 &&
                               bestPositivePairs >= Math.Max(2, bestEligiblePairs / 2);
            if (freshPositive && freshImproves && freshRobust)
            {
                bool strictBar = bestFresh.AvgNetR >= 0.05d &&
                                 bestFresh.Profitable >= freshBase.Profitable + 0.05d;
                if (strictBar)
                {
                    AppendInv(builder, $"- **Verdict:** the best fresh config (`{bestFreshRun.Config.Name}`) survives the strict §9.19 fresh bar: {bestFresh.N} signals, {bestFresh.Profitable:P1} profitable, {bestFresh.AvgNetR:F3}R, {bestPositivePeriods}/3 positive periods, {bestPositivePairs}/{bestEligiblePairs} positive pairs. It is a candidate for an **h1 pending-order preset**; the production default stays V(3) until the live re-pricing loop is implemented and tested.");
                }
                else
                {
                    AppendInv(builder, $"- **Verdict:** the best fresh config (`{bestFreshRun.Config.Name}`) passes the relaxed fresh check used here ({bestFresh.N} signals, {bestFresh.Profitable:P1} profitable, {bestFresh.AvgNetR:F3}R, {bestPositivePeriods}/3 positive periods, {bestPositivePairs}/{bestEligiblePairs} positive pairs) but **not the strict §9.19 bar** (+0.05R and +5 pp profitable). Keep it as a research preset; the production default stays V(3) until the live re-pricing loop is implemented and tested.");
                }
            }
            else if (freshImproves)
            {
                AppendInv(builder, $"- **Verdict:** the best fresh config (`{bestFreshRun.Config.Name}`) is better than V(3) but not positive or stable enough ({bestFresh.N} signals, {bestFresh.Profitable:P1} profitable, {bestFresh.AvgNetR:F3}R, {bestPositivePeriods}/3 positive periods, {bestPositivePairs}/{bestEligiblePairs} positive pairs). Keep the pending limit as a research preset; do not change the production trigger or default.");
            }
            else
            {
                builder.AppendLine("- **Verdict:** the fresh set does not confirm the pending limit. " +
                                   "The h1 improvement seen on the tuning pairs was likely a tuning " +
                                   "artifact; keep V(3) and the research-only trigger modes.");
            }

            builder.AppendLine("- The pending variant is still a **simulation** of a live limit order: " +
                               "the robot would have to place the order after the wedge is identified " +
                               "and cancel/replace it on every candle. The finder emits the signal at " +
                               "the close of the fill bar, so the same-bar TP/SL is rejected " +
                               "conservatively (`tpSlHit`).");
            builder.AppendLine("- The `minRR 0.5` combo is the §9.22 candidate applied to the pending " +
                               "entry; it is reported separately so the pure trigger effect and the " +
                               "R:R wait are not mixed.");

            return builder.ToString();
        }

        private static string BuildH1PendingRepriceCsv(
            IReadOnlyList<TriggerRun> tuning,
            IReadOnlyList<TriggerRun> fresh)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "set,config,trigger,file,timeframe,entry_time,signal_bar,v3_break_bar," +
                "delay_bars,reprice_count,entry,risk,tp,sl,line13,line_distance_w3," +
                "entry_delta_w3,outcome,net_r,worked236,profitable");

            foreach ((string setName, IReadOnlyList<TriggerRun> runs) in new[]
                     {
                         ("tuning", tuning),
                         ("fresh", fresh)
                     })
            {
                foreach (TriggerRun run in runs)
                {
                    foreach (Sample s in run.Samples
                                 .OrderBy(s => s.File, StringComparer.Ordinal)
                                 .ThenBy(s => s.EntryTime))
                    {
                        builder.AppendLine(string.Join(",",
                            Csv(setName),
                            Csv(run.Config.Name),
                            Csv(TriggerName(run.Config.TriggerMode)),
                            Csv(s.File),
                            Csv(s.TimeFrame),
                            Csv(s.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
                            s.SignalBar.ToString(CultureInfo.InvariantCulture),
                            s.V3BreakBar.ToString(CultureInfo.InvariantCulture),
                            s.DelayBars.ToString(CultureInfo.InvariantCulture),
                            s.RepriceCount.ToString(CultureInfo.InvariantCulture),
                            F(s.Entry), F(s.Risk), F(s.Tp), F(s.Sl),
                            F(s.Line13AtSignal), F(s.LineDistanceW3),
                            F(s.EntryDeltaW3),
                            Csv(s.Outcome), F(s.NetR), B(s.Worked236), B(s.Profitable)));
                    }
                }
            }

            return builder.ToString();
        }

        private static TriggerRun H1RunByName(IReadOnlyList<TriggerRun> runs, string name)
        {
            return runs.First(r => r.Config.Name == name);
        }

        private static List<Sample> H1SamplesOf(
            IReadOnlyList<TriggerRun> runs, string name, string file)
        {
            return H1RunByName(runs, name).Samples
                .Where(s => s.File == file)
                .ToList();
        }

        private static string SessionGroup(DateTime utc)
        {
            string session = SessionOf(utc);
            return session.StartsWith("London", StringComparison.Ordinal) ||
                   session.StartsWith("New York", StringComparison.Ordinal)
                ? "London/NY"
                : "Asia/Late";
        }

        private static string H1RepriceBucket(int count)
        {
            if (count < 0)
                return "—";
            if (count == 0)
                return "0";
            if (count <= 2)
                return "1–2";
            if (count <= 5)
                return "3–5";
            if (count <= 10)
                return "6–10";
            return ">10";
        }
    }
}
