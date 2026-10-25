using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace TradeKit.Tests
{
    /// <summary>
    /// Validation harness for the zigzag-confirmed post-end entry (DIAGONAL.md §9.30).
    /// The §9.29 grid found two positive cells: the exact corridor with the base zigzag,
    /// confirm-close entry, stop 20% and target 61.8%, and a permissive ×2.618 / stop 5% /
    /// target 50% variant. This harness does not search another grid; it fixes the cells
    /// and asks whether they survive:
    /// <list type="bullet">
    /// <item>bootstrap CI and a sign-flip p-value for the mean R;</item>
    /// <item>a true time walk-forward: select the plateau cell on prior years, trade the
    /// next year;</item>
    /// <item>leave-one-pair-out: select on the other fresh pairs, trade the held-out pair;</item>
    /// <item>equal-weight pair portfolio, session split, equity curve and max drawdown.</item>
    /// </list>
    /// Writes <c>reports/diagonal_zigzag_validation.md</c> and
    /// <c>reports/diagonal_zigzag_validation_trades.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalZigzagValidation"</c>.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const int VALIDATION_BOOTSTRAP = 10000;
        private const int VALIDATION_MIN_TRAIN = 30;
        private const int VALIDATION_SEED = 20261024;

        private static readonly (double Stop, double Target)[] VALIDATION_PLATEAU =
        {
            (0.20d, 0.382d),
            (0.20d, 0.50d),
            (0.20d, 0.618d),
            (0.30d, 0.382d),
            (0.30d, 0.50d),
            (0.30d, 0.618d)
        };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_zigzag_validation.md")]
        [Category("Research")]
        public void DiagonalZigzagValidation_Report()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning archive file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh archive file was found under data/.");

            var runs = new List<CorrectionRun>();
            runs.AddRange(RunBothConfigs(dataDir, tuningFiles, "tuning"));
            runs.AddRange(RunBothConfigs(dataDir, freshFiles, "fresh"));

            Dictionary<string, FileZigzags> zigzags = BuildZigzags(runs);
            var exactTrades = new List<ZigzagTrade>();
            var permissiveTrades = new List<ZigzagTrade>();

            foreach (CorrectionRun run in runs)
            {
                foreach (CorrectionSample sample in run.Samples)
                {
                    if (!sample.Outcome.Labeled)
                        continue;

                    FileZigzags fileZigzags = zigzags[sample.Base.File];
                    if (run.Config == "exact")
                    {
                        int scale = fileZigzags.ScalesByRank[0];
                        List<ZigzagPivot> pivots = fileZigzags.PivotsByRank[0];
                        foreach ((double stop, double target) in VALIDATION_PLATEAU)
                        {
                            ZigzagTrade? trade = SimulateZigzag(
                                sample, 0, scale, pivots,
                                ZigzagEntryMode.CONFIRM_CLOSE, stop, target);
                            if (trade != null)
                                exactTrades.Add(trade);
                        }
                    }
                    else if (run.Config == "permissive")
                    {
                        int rank = Math.Min(2, fileZigzags.ScalesByRank.Count - 1);
                        int scale = fileZigzags.ScalesByRank[rank];
                        List<ZigzagPivot> pivots = fileZigzags.PivotsByRank[rank];
                        ZigzagTrade? trade = SimulateZigzag(
                            sample, rank, scale, pivots,
                            ZigzagEntryMode.CONFIRM_CLOSE, 0.05d, 0.50d);
                        if (trade != null)
                            permissiveTrades.Add(trade);
                    }
                }
            }

            Assert.That(exactTrades, Is.Not.Empty, "The exact validation cells produced no trades.");
            Assert.That(permissiveTrades, Is.Not.Empty,
                "The permissive validation cell produced no trades.");

            List<ZigzagTrade> exactCell = exactTrades
                .Where(t => Math.Abs(t.StopRatio - 0.20d) < 1e-9 &&
                            Math.Abs(t.TargetRatio - 0.618d) < 1e-9)
                .ToList();

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_zigzag_validation.md");
            File.WriteAllText(
                reportPath,
                BuildValidationReport(exactTrades, exactCell, permissiveTrades));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_zigzag_validation_trades.csv");
            File.WriteAllText(csvPath, BuildValidationCsv(exactTrades, permissiveTrades));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            foreach (CorrectionSample sample in runs.SelectMany(r => r.Samples))
                sample.Base.Provider = null!;
        }

        #region Statistics

        private static ValidationStats Stats(IReadOnlyList<ZigzagTrade> trades)
        {
            if (trades.Count == 0)
                return new ValidationStats(0, 0, 0, 0);

            int wins = trades.Count(t => t.NetR > 0);
            return new ValidationStats(
                trades.Count,
                wins / (double)trades.Count,
                trades.Average(t => t.NetR),
                trades.Sum(t => t.NetR));
        }

        private static BootstrapResult Bootstrap(
            IReadOnlyList<ZigzagTrade> trades, int iterations, int seed)
        {
            if (trades.Count == 0)
                return new BootstrapResult(0, 0, 0, 1);

            double[] values = trades.Select(t => t.NetR).ToArray();
            double observed = values.Average();
            var random = new Random(seed);
            var means = new double[iterations];
            var sample = new double[values.Length];

            for (int i = 0; i < iterations; i++)
            {
                for (int j = 0; j < sample.Length; j++)
                    sample[j] = values[random.Next(values.Length)];
                means[i] = sample.Average();
            }

            Array.Sort(means);
            double low = means[(int)Math.Floor(0.025 * (iterations - 1))];
            double high = means[(int)Math.Ceiling(0.975 * (iterations - 1))];

            // Sign-flip randomization test for H0: mean = 0, one-sided mean > 0.
            int atLeast = 0;
            for (int i = 0; i < iterations; i++)
            {
                double sum = 0;
                for (int j = 0; j < values.Length; j++)
                    sum += random.Next(2) == 0 ? values[j] : -values[j];
                if (sum / values.Length >= observed)
                    atLeast++;
            }

            return new BootstrapResult(
                observed, low, high, (atLeast + 1d) / (iterations + 1d));
        }

        private static (double Mean, double MaxDrawdown, int LongestLosingStreak, double FinalR)
            Equity(IReadOnlyList<ZigzagTrade> trades)
        {
            if (trades.Count == 0)
                return (0, 0, 0, 0);

            List<ZigzagTrade> ordered = trades
                .OrderBy(t => t.EntryTime)
                .ToList();
            double equity = 0;
            double peak = 0;
            double maxDrawdown = 0;
            int streak = 0;
            int longestStreak = 0;

            foreach (ZigzagTrade trade in ordered)
            {
                equity += trade.NetR;
                peak = Math.Max(peak, equity);
                maxDrawdown = Math.Min(maxDrawdown, equity - peak);
                if (trade.NetR < 0)
                {
                    streak++;
                    longestStreak = Math.Max(longestStreak, streak);
                }
                else
                {
                    streak = 0;
                }
            }

            return (equity / ordered.Count, maxDrawdown, longestStreak, equity);
        }

        private static List<(string Pair, int N, double MeanR)> PairMeans(
            IReadOnlyList<ZigzagTrade> trades)
        {
            return trades
                .GroupBy(t => t.Symbol)
                .Select(g => (g.Key, g.Count(), g.Average(t => t.NetR)))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToList();
        }

        #endregion

        #region Report

        private static string BuildValidationReport(
            IReadOnlyList<ZigzagTrade> exactTrades,
            IReadOnlyList<ZigzagTrade> exactCell,
            IReadOnlyList<ZigzagTrade> permissiveTrades)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — zigzag post-end entry, validation");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("The §9.29 grid found two positive cells. This harness fixes them and asks whether they survive out of sample; it does not search another grid.");
            builder.AppendLine();
            builder.AppendLine("## Fixed cells");
            builder.AppendLine();
            builder.AppendLine("- **exact / base zigzag / confirm / stop 20% / target 61.8%** — the tuning-best cell inside the exact plateau (stop 20–30%, target 38.2–61.8%).");
            builder.AppendLine("- **permissive / ×2.618 / confirm / stop 5% / target 50%** — the best permissive cell from §9.29; it was selected with fresh in view, so its fresh number is not a clean holdout.");
            builder.AppendLine("- The exact cell is the primary candidate; the permissive cell is the comparison. Both are causal: the zigzag is fed bar by bar and the pivot is used only after confirmation.");
            builder.AppendLine();

            builder.AppendLine("## Point estimates and significance");
            builder.AppendLine();
            builder.AppendLine("Bootstrap 95% CI of the mean R (10 000 resamples) and a sign-flip randomization p-value for `mean > 0`. The p-value does not correct for the §9.29 grid search, so treat it as a lower bound on the uncertainty.");
            builder.AppendLine();
            builder.AppendLine("| Cell | Set | N | win | mean R | sum R | 95% CI | p (mean > 0) |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---|---:|");

            AppendSignificance(builder, "exact 20/61.8", exactCell);
            AppendSignificance(builder, "permissive 5/50", permissiveTrades);

            builder.AppendLine();
            builder.AppendLine("## Time walk-forward");
            builder.AppendLine();
            builder.AppendLine("At each year, the best of the six exact plateau cells is selected on **prior years only** (minimum 30 trades) and traded on the current year. This is a true time out-of-sample test of the plateau selection.");
            builder.AppendLine();
            AppendWalkForward(builder, exactTrades);

            builder.AppendLine();
            builder.AppendLine("## Leave-one-pair-out");
            builder.AppendLine();
            builder.AppendLine("For each fresh pair, the best of the six plateau cells is selected on the **other seven pairs** and traded on the held-out pair. This is a cross-pair generalization test.");
            builder.AppendLine();
            AppendPairLeaveOneOut(builder, exactTrades);

            builder.AppendLine();
            builder.AppendLine("## Equal-weight pair portfolio");
            builder.AppendLine();
            builder.AppendLine("The trade-weighted mean gives more weight to pairs with more signals. The equal-weight mean is the average of the per-pair means; it is the honest number when the edge is concentrated in one or two pairs.");
            builder.AppendLine();
            AppendPairPortfolio(builder, "exact 20/61.8", exactCell);
            AppendPairPortfolio(builder, "permissive 5/50", permissiveTrades);

            builder.AppendLine();
            builder.AppendLine("## Session split");
            builder.AppendLine();
            builder.AppendLine("The §9.29 sessions flipped between the two cells; this table checks whether a session filter is stable.");
            builder.AppendLine();
            builder.AppendLine("| Cell | Session | N | win | mean R |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            AppendSessions(builder, "exact 20/61.8", exactCell);
            AppendSessions(builder, "permissive 5/50", permissiveTrades);

            builder.AppendLine();
            builder.AppendLine("## Equity and drawdown (fresh)");
            builder.AppendLine();
            builder.AppendLine("| Cell | N | mean R | final R | max drawdown | longest losing streak |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            AppendEquity(builder, "exact 20/61.8", exactCell);
            AppendEquity(builder, "permissive 5/50", permissiveTrades);

            builder.AppendLine();
            builder.AppendLine("## What it says");
            builder.AppendLine();
            AppendConclusions(builder, exactCell, permissiveTrades);

            return builder.ToString();
        }

        private static void AppendSignificance(
            StringBuilder builder, string name, IReadOnlyList<ZigzagTrade> trades)
        {
            foreach (string set in new[] { "tuning", "fresh" })
            {
                List<ZigzagTrade> subset = trades
                    .Where(t => t.Set == set)
                    .ToList();
                if (subset.Count == 0)
                    continue;

                ValidationStats stats = Stats(subset);
                int seed = VALIDATION_SEED + (set == "tuning" ? 11 : 22);
                BootstrapResult bootstrap = Bootstrap(
                    subset, VALIDATION_BOOTSTRAP, seed);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {name} | {set} | {stats.N} | {stats.WinRate:P1} | {stats.MeanR:F3} | {stats.SumR:F2} | [{bootstrap.Low:F3}; {bootstrap.High:F3}] | {bootstrap.PValue:F3} |"));
            }
        }

        private static void AppendWalkForward(
            StringBuilder builder, IReadOnlyList<ZigzagTrade> exactTrades)
        {
            var years = exactTrades
                .Select(t => t.SignalTime.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            var rows = new List<(int Year, double Stop, double Target, int N, double MeanR)>();
            foreach (int year in years)
            {
                List<ZigzagTrade> prior = exactTrades
                    .Where(t => t.SignalTime.Year < year)
                    .ToList();
                if (prior.Count < VALIDATION_MIN_TRAIN)
                    continue;

                (double Stop, double Target, int N, double MeanR)? best = null;
                foreach ((double stop, double target) in VALIDATION_PLATEAU)
                {
                    List<ZigzagTrade> cell = prior
                        .Where(t => Math.Abs(t.StopRatio - stop) < 1e-9 &&
                                    Math.Abs(t.TargetRatio - target) < 1e-9)
                        .ToList();
                    if (cell.Count < VALIDATION_MIN_TRAIN)
                        continue;
                    double mean = cell.Average(t => t.NetR);
                    if (best == null || mean > best.Value.MeanR)
                        best = (stop, target, cell.Count, mean);
                }

                if (best == null)
                    continue;

                List<ZigzagTrade> test = exactTrades
                    .Where(t => t.SignalTime.Year == year &&
                                Math.Abs(t.StopRatio - best.Value.Stop) < 1e-9 &&
                                Math.Abs(t.TargetRatio - best.Value.Target) < 1e-9)
                    .ToList();
                if (test.Count == 0)
                    continue;

                rows.Add((year, best.Value.Stop, best.Value.Target, test.Count,
                    test.Average(t => t.NetR)));
            }

            if (rows.Count == 0)
            {
                builder.AppendLine("Not enough prior trades to run the walk-forward.");
                return;
            }

            builder.AppendLine("| Year | Selected stop | Selected target | N | mean R |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach ((int year, double stop, double target, int n, double mean) in rows)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {year} | {stop:P0} | {target:P1} | {n} | {mean:F3} |"));
            }

            int total = rows.Sum(r => r.N);
            double totalMean = total == 0 ? 0 : rows.Sum(r => r.N * r.MeanR) / total;
            int positiveYears = rows.Count(r => r.MeanR > 0);
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"**Aggregate:** {total} trades, mean {totalMean:F3}R, {positiveYears}/{rows.Count} years positive."));
        }

        private static void AppendPairLeaveOneOut(
            StringBuilder builder, IReadOnlyList<ZigzagTrade> exactTrades)
        {
            List<ZigzagTrade> fresh = exactTrades
                .Where(t => t.Set == "fresh")
                .ToList();
            List<string> pairs = fresh
                .Select(t => t.Symbol)
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var rows = new List<(string Pair, double Stop, double Target, int N, double MeanR)>();
            foreach (string pair in pairs)
            {
                List<ZigzagTrade> prior = fresh
                    .Where(t => t.Symbol != pair)
                    .ToList();
                if (prior.Count < VALIDATION_MIN_TRAIN)
                    continue;

                (double Stop, double Target, double MeanR)? best = null;
                foreach ((double stop, double target) in VALIDATION_PLATEAU)
                {
                    List<ZigzagTrade> cell = prior
                        .Where(t => Math.Abs(t.StopRatio - stop) < 1e-9 &&
                                    Math.Abs(t.TargetRatio - target) < 1e-9)
                        .ToList();
                    if (cell.Count < VALIDATION_MIN_TRAIN)
                        continue;
                    double mean = cell.Average(t => t.NetR);
                    if (best == null || mean > best.Value.MeanR)
                        best = (stop, target, mean);
                }

                if (best == null)
                    continue;

                List<ZigzagTrade> test = fresh
                    .Where(t => t.Symbol == pair &&
                                Math.Abs(t.StopRatio - best.Value.Stop) < 1e-9 &&
                                Math.Abs(t.TargetRatio - best.Value.Target) < 1e-9)
                    .ToList();
                if (test.Count == 0)
                    continue;

                rows.Add((pair, best.Value.Stop, best.Value.Target, test.Count,
                    test.Average(t => t.NetR)));
            }

            if (rows.Count == 0)
            {
                builder.AppendLine("Not enough prior trades to run the leave-one-pair-out.");
                return;
            }

            builder.AppendLine("| Held-out pair | Selected stop | Selected target | N | mean R |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach ((string pair, double stop, double target, int n, double mean) in rows)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {pair} | {stop:P0} | {target:P1} | {n} | {mean:F3} |"));
            }

            int total = rows.Sum(r => r.N);
            double totalMean = total == 0 ? 0 : rows.Sum(r => r.N * r.MeanR) / total;
            int positivePairs = rows.Count(r => r.MeanR > 0);
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"**Aggregate:** {total} trades, mean {totalMean:F3}R, {positivePairs}/{rows.Count} pairs positive."));
        }

        private static void AppendPairPortfolio(
            StringBuilder builder, string name, IReadOnlyList<ZigzagTrade> trades)
        {
            List<ZigzagTrade> fresh = trades
                .Where(t => t.Set == "fresh")
                .ToList();
            if (fresh.Count == 0)
                return;

            List<(string Pair, int N, double MeanR)> pairs = PairMeans(fresh);
            double tradeWeighted = fresh.Average(t => t.NetR);
            double equalWeight = pairs.Average(p => p.MeanR);
            int positive = pairs.Count(p => p.MeanR > 0);

            builder.AppendLine(FormattableString.Invariant(
                $"**{name}:** {fresh.Count} trades, trade-weighted {tradeWeighted:F3}R, equal-weight {equalWeight:F3}R, {positive}/{pairs.Count} pairs positive."));
            builder.AppendLine();
            builder.AppendLine("| Pair | N | mean R |");
            builder.AppendLine("|---|---:|---:|");
            foreach ((string pair, int n, double mean) in pairs)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {pair} | {n} | {mean:F3} |"));
            }
            builder.AppendLine();
        }

        private static void AppendSessions(
            StringBuilder builder, string name, IReadOnlyList<ZigzagTrade> trades)
        {
            List<ZigzagTrade> fresh = trades
                .Where(t => t.Set == "fresh")
                .ToList();
            foreach (IGrouping<string, ZigzagTrade> group in fresh
                         .GroupBy(t => SessionOf(t.SignalTime))
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<ZigzagTrade> subset = group.ToList();
                builder.AppendLine(FormattableString.Invariant(
                    $"| {name} | {group.Key} | {subset.Count} | {subset.Count(t => t.NetR > 0) / (double)subset.Count:P1} | {subset.Average(t => t.NetR):F3} |"));
            }
        }

        private static void AppendEquity(
            StringBuilder builder, string name, IReadOnlyList<ZigzagTrade> trades)
        {
            List<ZigzagTrade> fresh = trades
                .Where(t => t.Set == "fresh")
                .ToList();
            (double mean, double maxDrawdown, int longestStreak, double finalR) =
                Equity(fresh);
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {fresh.Count} | {mean:F3} | {finalR:F2} | {maxDrawdown:F2} | {longestStreak} |"));
        }

        private static void AppendConclusions(
            StringBuilder builder,
            IReadOnlyList<ZigzagTrade> exactTrades,
            IReadOnlyList<ZigzagTrade> permissiveTrades)
        {
            List<ZigzagTrade> exactFresh = exactTrades
                .Where(t => t.Set == "fresh")
                .ToList();
            List<ZigzagTrade> permissiveFresh = permissiveTrades
                .Where(t => t.Set == "fresh")
                .ToList();
            if (exactFresh.Count == 0 || permissiveFresh.Count == 0)
            {
                builder.AppendLine("- No fresh sample; nothing to conclude.");
                return;
            }

            ValidationStats exact = Stats(exactFresh);
            ValidationStats permissive = Stats(permissiveFresh);
            BootstrapResult exactBootstrap = Bootstrap(
                exactFresh, VALIDATION_BOOTSTRAP, VALIDATION_SEED + 1);
            BootstrapResult permissiveBootstrap = Bootstrap(
                permissiveFresh, VALIDATION_BOOTSTRAP, VALIDATION_SEED + 2);
            double exactEqual = PairMeans(exactFresh).Average(p => p.MeanR);
            double permissiveEqual = PairMeans(permissiveFresh).Average(p => p.MeanR);

            builder.AppendLine(FormattableString.Invariant(
                $"- **Exact cell.** Fresh: {exact.N} trades, {exact.WinRate:P1} win, {exact.MeanR:F3}R, bootstrap 95% CI [{exactBootstrap.Low:F3}; {exactBootstrap.High:F3}], p={exactBootstrap.PValue:F3}. Equal-weight pair mean {exactEqual:F3}R."));
            builder.AppendLine(FormattableString.Invariant(
                $"- **Permissive cell.** Fresh: {permissive.N} trades, {permissive.WinRate:P1} win, {permissive.MeanR:F3}R, bootstrap 95% CI [{permissiveBootstrap.Low:F3}; {permissiveBootstrap.High:F3}], p={permissiveBootstrap.PValue:F3}. Equal-weight pair mean {permissiveEqual:F3}R."));

            builder.AppendLine("- **Significance.** A 95% CI that contains zero is not evidence of an edge; the p-value is a one-sided sign-flip test and does not correct for the §9.29 grid search, so the true uncertainty is larger.");
            builder.AppendLine("- **Time walk-forward and leave-one-pair-out are the decisive checks.** If the aggregate OOS mean is not positive, the cell is a §9.29 artifact, not a strategy.");
            builder.AppendLine("- **Caveats.** The archive is bid-only, has no spread, and the cost is a flat 2 bp. The fresh set is a pair holdout, not a new period; the time walk-forward is the closest thing to a new period in this archive. The sample is small (104–135 trades), so even a positive OOS mean needs a live check.");
            builder.AppendLine();
        }

        #endregion

        #region CSV

        private static string BuildValidationCsv(
            IReadOnlyList<ZigzagTrade> exactTrades,
            IReadOnlyList<ZigzagTrade> permissiveTrades)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "cell,config,set,file,symbol,signal_time,scale,mode,stop,target,end_time,end_bar," +
                "end_price,height,entry_time,entry_bar,entry,stop_price,target_price,risk,reward," +
                "planned_rr,outcome,gross_r,cost_r,net_r,resolved,bars_to_exit,mfe_r,mae_r," +
                "cap_overshoot,depth_diag");

            foreach ((string cell, ZigzagTrade t) in exactTrades
                         .Select(t => ("exact 20/61.8", t))
                         .Concat(permissiveTrades.Select(t => ("permissive 5/50", t)))
                         .Where(x => x.Item2.Mode == ZigzagEntryMode.CONFIRM_CLOSE)
                         .OrderBy(x => x.Item1, StringComparer.Ordinal)
                         .ThenBy(x => x.Item2.Set, StringComparer.Ordinal)
                         .ThenBy(x => x.Item2.SignalTime))
            {
                if (cell == "exact 20/61.8" &&
                    (Math.Abs(t.StopRatio - 0.20d) > 1e-9 ||
                     Math.Abs(t.TargetRatio - 0.618d) > 1e-9))
                {
                    continue;
                }

                builder.AppendLine(string.Join(",",
                    Csv(cell),
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

        #endregion

        #region Types

        private sealed record ValidationStats(
            int N,
            double WinRate,
            double MeanR,
            double SumR);

        private sealed record BootstrapResult(
            double Mean,
            double Low,
            double High,
            double PValue);

        #endregion
    }
}
