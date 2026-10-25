using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Pre-registered m15 test of the permissive zigzag cell (DIAGONAL.md §9.31).
    /// The cell was selected on h1 in §9.29 and survived the h1 validation only as a
    /// "maybe" (fresh +0.165R, p=0.065, equal-weight +0.129R). This harness fixes the
    /// cell exactly — permissive config, ×2.618 zigzag, confirm-close entry, stop 5% of
    /// height, target 50% — and applies it to the m15 archive without any parameter search.
    /// <para>
    /// The m15 tuning and fresh pairs were not used to select the cell, so both are
    /// out-of-sample for it; the fresh m15 set (different pairs) is the stronger holdout.
    /// The report covers bootstrap CI, sign-flip p, equal-weight pair portfolio, sessions,
    /// years, equity and max drawdown, and compares the result with the h1 reference.
    /// Writes <c>reports/diagonal_zigzag_m15_validation.md</c> and
    /// <c>reports/diagonal_zigzag_m15_validation_trades.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalZigzagM15Validation"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const int M15_VALIDATION_BOOTSTRAP = 10000;
        private const int M15_VALIDATION_SEED = 20261025;

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_zigzag_m15_validation.md")]
        [Category("Research")]
        public void DiagonalZigzagM15Validation_Report()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = Existing(dataDir, M15_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_M15_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning m15 file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh m15 file was found under data/.");

            var runs = new List<CorrectionRun>
            {
                RunPermissive(dataDir, tuningFiles, "tuning"),
                RunPermissive(dataDir, freshFiles, "fresh")
            };

            Dictionary<string, FileZigzags> zigzags = BuildZigzags(runs);
            var trades = new List<ZigzagTrade>();

            foreach (CorrectionRun run in runs)
            {
                foreach (CorrectionSample sample in run.Samples)
                {
                    if (!sample.Outcome.Labeled)
                        continue;

                    FileZigzags fileZigzags = zigzags[sample.Base.File];
                    int rank = Math.Min(2, fileZigzags.ScalesByRank.Count - 1);
                    int scale = fileZigzags.ScalesByRank[rank];
                    List<ZigzagPivot> pivots = fileZigzags.PivotsByRank[rank];

                    ZigzagTrade? trade = SimulateZigzag(
                        sample, rank, scale, pivots,
                        ZigzagEntryMode.CONFIRM_CLOSE, 0.05d, 0.50d);
                    if (trade != null)
                        trades.Add(trade);
                }
            }

            Assert.That(trades, Is.Not.Empty,
                "The pre-registered m15 cell produced no trades.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_zigzag_m15_validation.md");
            File.WriteAllText(reportPath, BuildM15ValidationReport(runs, trades));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_zigzag_m15_validation_trades.csv");
            File.WriteAllText(csvPath, BuildM15ValidationCsv(trades));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            foreach (CorrectionSample sample in runs.SelectMany(r => r.Samples))
                sample.Base.Provider = null!;
        }

        private static CorrectionRun RunPermissive(
            string dataDir, IReadOnlyList<string> files, string set)
        {
            var samples = new List<CorrectionSample>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                file =>
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    ITimeFrame timeFrame = file.Contains("_m15_")
                        ? TimeFrameHelper.Minute15
                        : TimeFrameHelper.Hour1;

                    var provider = new TestBarsProvider(timeFrame);
                    provider.LoadCandles(Path.Combine(dataDir, file));

                    List<Sample> local = RunDiagonalSamples(provider, file, PERMISSIVE);
                    var converted = new List<CorrectionSample>(local.Count);
                    foreach (Sample sample in local)
                    {
                        converted.Add(new CorrectionSample(
                            sample,
                            MeasureCorrection(sample, CORRECTION_HORIZON_MULTIPLIER),
                            "permissive",
                            set));
                    }

                    lock (sync)
                    {
                        samples.AddRange(converted);
                    }

                    TestContext.Out.WriteLine(
                        $"{set}/{file}: signals={converted.Count}, " +
                        $"elapsed={stopwatch.Elapsed.TotalSeconds:F1}s");
                });

            return new CorrectionRun("permissive", set, samples);
        }

        private static string BuildM15ValidationReport(
            IReadOnlyList<CorrectionRun> runs,
            IReadOnlyList<ZigzagTrade> trades)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — pre-registered m15 test of the permissive zigzag cell");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("The cell is fixed exactly as selected on h1 in §9.29: **permissive config, ×2.618 zigzag, confirm-close entry, stop 5% of height, target 50%**. No parameter search on m15. Both m15 sets were unused by the cell selection; the fresh m15 set (different pairs) is the stronger holdout.");
            builder.AppendLine();

            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Set | Files | Labelled signals | Trades |");
            builder.AppendLine("|---|---:|---:|---:|");
            foreach (CorrectionRun run in runs)
            {
                int files = run.Samples
                    .Select(x => x.Base.File)
                    .Distinct()
                    .Count();
                int labelled = run.Samples.Count(x => x.Outcome.Labeled);
                int count = trades.Count(t => t.Set == run.Set);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {run.Set} | {files} | {labelled} | {count} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Pre-registered cell");
            builder.AppendLine();
            builder.AppendLine("| Set | N | win | mean R | sum R | 95% CI | p (mean > 0) | equal-weight pair | max DD | final R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---|---:|---:|---:|---:|");

            foreach (string set in new[] { "tuning", "fresh" })
            {
                List<ZigzagTrade> subset = trades
                    .Where(t => t.Set == set)
                    .ToList();
                if (subset.Count == 0)
                    continue;

                ValidationStats stats = Stats(subset);
                BootstrapResult bootstrap = Bootstrap(
                    subset,
                    M15_VALIDATION_BOOTSTRAP,
                    M15_VALIDATION_SEED + (set == "tuning" ? 11 : 22));
                double equalWeight = PairMeans(subset).Average(p => p.MeanR);
                (double _, double maxDrawdown, int _, double finalR) = Equity(subset);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {set} | {stats.N} | {stats.WinRate:P1} | {stats.MeanR:F3} | {stats.SumR:F2} | [{bootstrap.Low:F3}; {bootstrap.High:F3}] | {bootstrap.PValue:F3} | {equalWeight:F3} | {maxDrawdown:F2} | {finalR:F2} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## h1 reference");
            builder.AppendLine();
            builder.AppendLine("The same cell on h1 (§9.30): fresh 104 trades, 65.4% win, +0.165R, bootstrap 95% CI [−0.041; +0.372], p=0.065, equal-weight pair +0.129R, max DD −4.75R, final +17.14R.");
            builder.AppendLine();

            builder.AppendLine("## By pair (fresh)");
            builder.AppendLine();
            builder.AppendLine("| Pair | N | mean R |");
            builder.AppendLine("|---|---:|---:|");
            List<ZigzagTrade> fresh = trades
                .Where(t => t.Set == "fresh")
                .ToList();
            foreach ((string pair, int n, double mean) in PairMeans(fresh))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {pair} | {n} | {mean:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## By year");
            builder.AppendLine();
            builder.AppendLine("| Set | Year | N | win | mean R |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach (string set in new[] { "tuning", "fresh" })
            {
                foreach (IGrouping<int, ZigzagTrade> group in trades
                             .Where(t => t.Set == set)
                             .GroupBy(t => t.SignalTime.Year)
                             .OrderBy(g => g.Key))
                {
                    List<ZigzagTrade> subset = group.ToList();
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {set} | {group.Key} | {subset.Count} | {subset.Count(t => t.NetR > 0) / (double)subset.Count:P1} | {subset.Average(t => t.NetR):F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## By session (fresh)");
            builder.AppendLine();
            builder.AppendLine("| Session | N | win | mean R |");
            builder.AppendLine("|---|---:|---:|---:|");
            foreach (IGrouping<string, ZigzagTrade> group in fresh
                         .GroupBy(t => SessionOf(t.SignalTime))
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<ZigzagTrade> subset = group.ToList();
                builder.AppendLine(FormattableString.Invariant(
                    $"| {group.Key} | {subset.Count} | {subset.Count(t => t.NetR > 0) / (double)subset.Count:P1} | {subset.Average(t => t.NetR):F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## What it says");
            builder.AppendLine();
            AppendM15Conclusions(builder, trades);

            return builder.ToString();
        }

        private static void AppendM15Conclusions(
            StringBuilder builder,
            IReadOnlyList<ZigzagTrade> trades)
        {
            List<ZigzagTrade> fresh = trades
                .Where(t => t.Set == "fresh")
                .ToList();
            List<ZigzagTrade> tuning = trades
                .Where(t => t.Set == "tuning")
                .ToList();
            if (fresh.Count == 0)
            {
                builder.AppendLine("- No fresh m15 trades; nothing to conclude.");
                return;
            }

            ValidationStats freshStats = Stats(fresh);
            ValidationStats tuningStats = Stats(tuning);
            BootstrapResult freshBootstrap = Bootstrap(
                fresh, M15_VALIDATION_BOOTSTRAP, M15_VALIDATION_SEED + 1);
            double freshEqual = PairMeans(fresh).Average(p => p.MeanR);
            (double _, double freshDrawdown, int freshStreak, double freshFinal) =
                Equity(fresh);

            builder.AppendLine(FormattableString.Invariant(
                $"- **m15 tuning:** {tuningStats.N} trades, {tuningStats.WinRate:P1} win, {tuningStats.MeanR:F3}R."));
            builder.AppendLine(FormattableString.Invariant(
                $"- **m15 fresh:** {freshStats.N} trades, {freshStats.WinRate:P1} win, {freshStats.MeanR:F3}R, bootstrap 95% CI [{freshBootstrap.Low:F3}; {freshBootstrap.High:F3}], p={freshBootstrap.PValue:F3}. Equal-weight pair {freshEqual:F3}R, max DD {freshDrawdown:F2}R, final {freshFinal:F2}R, longest losing streak {freshStreak}."));

            bool transfer = freshStats.MeanR > 0 &&
                            freshBootstrap.Low > 0 &&
                            freshEqual > 0;
            builder.AppendLine(transfer
                ? "- **Verdict.** The fixed h1 cell transfers to m15: the fresh mean is positive, the 95% CI excludes zero, and the equal-weight pair mean is positive. This is a genuine pre-registered out-of-sample result and the first diagonal result worth a live forward test. It still needs spread/slippage and a live run."
                : "- **Verdict.** The fixed h1 cell does not transfer cleanly to m15: at least one of the fresh mean, the 95% CI, or the equal-weight pair mean fails. The h1 result remains a timeframe-specific maybe, not a strategy. Do not ship; the honest next step is a live forward test of the h1 cell, not another timeframe/grid search.");

            builder.AppendLine("- **Caveats.** The m15 archive is bid-only and has no spread; the cost is a flat 2 bp. The m15 fresh set is a pair holdout, not a new period. The sample is larger than h1 but the per-trade R distribution is heavy-tailed; the equal-weight pair mean is the honest number. The cell parameters are fixed, so this is a pre-registered test, not a search.");
            builder.AppendLine();
        }

        private static string BuildM15ValidationCsv(IReadOnlyList<ZigzagTrade> trades)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "config,set,file,symbol,signal_time,scale,mode,stop,target,end_time,end_bar," +
                "end_price,height,entry_time,entry_bar,entry,stop_price,target_price,risk,reward," +
                "planned_rr,outcome,gross_r,cost_r,net_r,resolved,bars_to_exit,mfe_r,mae_r," +
                "cap_overshoot,depth_diag");

            foreach (ZigzagTrade t in trades
                         .OrderBy(t => t.Set, StringComparer.Ordinal)
                         .ThenBy(t => t.File, StringComparer.Ordinal)
                         .ThenBy(t => t.EntryTime))
            {
                builder.AppendLine(string.Join(",",
                    Csv(t.Config),
                    Csv(t.Set),
                    Csv(t.File),
                    Csv(t.Symbol),
                    Csv(t.SignalTime.ToString("o", CultureInfo.InvariantCulture)),
                    t.Scale.ToString(CultureInfo.InvariantCulture),
                    Csv(t.Mode == ZigzagEntryMode.CONFIRM_CLOSE ? "confirm" : "pivot"),
                    F(t.StopRatio), F(t.TargetRatio),
                    Csv(t.EndTime.ToString("o", CultureInfo.InvariantCulture)),
                    t.EndBar.ToString(CultureInfo.InvariantCulture),
                    F(t.EndPrice), F(t.Height),
                    Csv(t.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
                    t.EntryBar.ToString(CultureInfo.InvariantCulture),
                    F(t.Entry), F(t.Stop), F(t.Target), F(t.Risk), F(t.Reward),
                    F(t.PlannedRr), Csv(t.Outcome), F(t.GrossR), F(t.CostR), F(t.NetR),
                    B(t.Resolved), t.BarsToExit.ToString(CultureInfo.InvariantCulture),
                    F(t.MfeR), F(t.MaeR), B(t.CapOvershoot), F(t.DepthDiag)));
            }

            return builder.ToString();
        }
    }
}
