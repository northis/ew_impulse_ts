using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Pre-registered validation of the A-B-C size filter found in
    /// <see cref="DiagonalImpulseAbcResearchTests"/> (DIAGONAL.md §9.42). The threshold
    /// <c>MovementStatistic.Size >= 0.012</c> was fixed before this run and is not tuned
    /// here. Two unseen datasets are replayed with the exact corridor and the <c>ALL</c>
    /// mode only: the eight fresh m15 pairs and eight h1 pairs that are in neither the
    /// h1 tuning nor the h1 fresh set of the Phase 1 report.
    /// <para>
    /// Primary execution is <c>nextopen</c> (no same-bar fill), robustness is <c>close</c>;
    /// EnterRatio 0.35 and 0.50. For every cell the unfiltered baseline and the filtered
    /// result are reported. Writes <c>reports/diagonal_impulse_validation.md</c> and
    /// <c>reports/diagonal_impulse_validation_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalImpulseValidation"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string VALIDATION_REPORT = "diagonal_impulse_validation.md";
        private const string VALIDATION_CSV = "diagonal_impulse_validation_samples.csv";

        /// <summary>The pre-registered filter: <c>Size = |A| / price</c>.</summary>
        private const double VALIDATION_SIZE_FILTER = 0.012d;

        private const int VALIDATION_TRAIN_LAST_YEAR = 2022;

        private static readonly double[] VALIDATION_ENTER_RATIOS = { 0.35d, 0.50d };

        /// <summary>Primary execution first, robustness second.</summary>
        private static readonly string[] VALIDATION_EXECUTIONS = { "nextopen", "close" };

        /// <summary>
        /// Eight h1 pairs that never took part in the Phase 1 threshold search: they are in
        /// neither <see cref="H1_FILES"/> (tuning) nor <see cref="FRESH_H1_FILES"/> (fresh).
        /// </summary>
        private static readonly string[] H1_HOLDOUT_FILES =
        {
            "AUDCHF_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "CADCHF_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "CHFJPY_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "EURAUD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "EURCAD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "EURNZD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "GBPAUD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "GBPNZD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv"
        };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_impulse_validation.md")]
        [Category("Research")]
        public void DiagonalImpulseValidation_Report()
        {
            string dataDir = FindDataDir();
            List<string> m15Files = Existing(dataDir, FRESH_M15_FILES);
            List<string> h1Files = Existing(dataDir, H1_HOLDOUT_FILES);

            string? onlySet = Environment.GetEnvironmentVariable("IMPULSE_VALIDATION_DATASET");
            if (!string.IsNullOrEmpty(onlySet))
            {
                bool wantM15 = onlySet.Contains("m15", StringComparison.OrdinalIgnoreCase);
                bool wantH1 = onlySet.Contains("h1", StringComparison.OrdinalIgnoreCase);
                if (!wantM15)
                    m15Files.Clear();
                if (!wantH1)
                    h1Files.Clear();
            }

            string? onlyFile = Environment.GetEnvironmentVariable("IMPULSE_VALIDATION_FILE");
            if (!string.IsNullOrEmpty(onlyFile))
            {
                m15Files = m15Files.Where(f => f == onlyFile).ToList();
                h1Files = h1Files.Where(f => f == onlyFile).ToList();
                Assert.That(m15Files.Count + h1Files.Count, Is.GreaterThan(0),
                    $"'{onlyFile}' is not in the m15 fresh or h1 holdout sets.");
            }

            Assert.That(m15Files.Count + h1Files.Count, Is.GreaterThan(0),
                "No fresh m15 or new h1 holdout archive file was found.");

            var datasets = new List<ValidationDataset>();
            foreach ((string name, List<string> files) in new[]
                     {
                         ("m15 fresh", m15Files), ("h1 holdout", h1Files)
                     })
            {
                if (files.Count == 0)
                    continue;

                var watch = System.Diagnostics.Stopwatch.StartNew();
                List<AbcAnchorFile> anchors = LoadAbcAnchors(
                    dataDir, DiagonalInitialMode.ALL, files);
                var dataset = new ValidationDataset(
                    name, files.Count, anchors.Sum(a => a.Anchors.Count));

                Dictionary<string, TestBarsProvider> providers = anchors
                    .ToDictionary(a => a.File, a => a.Provider, StringComparer.Ordinal);

                foreach (string execution in VALIDATION_EXECUTIONS)
                {
                    foreach (double enterRatio in VALIDATION_ENTER_RATIOS)
                    {
                        var set = new AbcSet();
                        SimulateAbcSet(anchors, enterRatio, execution, set);

                        List<ValidationTrade> trades = set.Trades
                            .Select(t => new ValidationTrade(
                                t, MedianBarRangeOfA(providers[t.File], t)))
                            .ToList();

                        dataset.Runs.Add(new ValidationRun(
                            execution, enterRatio, set.Anchors, set.ALegs, trades));
                    }
                }

                dataset.LoadSeconds = watch.Elapsed.TotalSeconds;
                datasets.Add(dataset);

                TestContext.Out.WriteLine(
                    $"{name}: files={files.Count}, bars={providers.Values.Sum(p => p.Count)}, " +
                    $"anchors={dataset.Anchors}, {watch.Elapsed.TotalSeconds:F0}s");

                foreach (AbcAnchorFile file in anchors)
                    file.Provider = null!;
            }

            Assert.That(datasets.Sum(d => d.Runs.Sum(r => r.Trades.Count)),
                Is.GreaterThan(0), "The A-B-C validation produced no trades.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, VALIDATION_REPORT);
            File.WriteAllText(reportPath, BuildAbcValidationReport(datasets));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, VALIDATION_CSV);
            File.WriteAllText(csvPath, BuildAbcValidationCsv(datasets));
            TestContext.Out.WriteLine($"Wrote {csvPath}");
        }

        #region Metrics

        private static double MedianBarRangeOfA(IBarsProvider provider, AbcTrade trade)
        {
            int from = Math.Max(0, trade.AStartBar);
            int to = Math.Min(provider.Count - 1, trade.AEndBar);
            if (to < from)
                return double.NaN;

            var ranges = new double[to - from + 1];
            for (int i = from; i <= to; i++)
                ranges[i - from] = provider.GetHighPrice(i) - provider.GetLowPrice(i);
            Array.Sort(ranges);

            int mid = ranges.Length / 2;
            return ranges.Length % 2 == 1
                ? ranges[mid]
                : 0.5d * (ranges[mid - 1] + ranges[mid]);
        }

        private static AbcValidationStats AbcValidationStatsOf(IReadOnlyList<ValidationTrade> trades)
        {
            List<ValidationTrade> resolved = trades
                .Where(t => t.Trade.Resolved)
                .ToList();

            return new AbcValidationStats(
                trades.Count,
                resolved.Count,
                resolved.Count == 0
                    ? 0
                    : resolved.Count(t => t.Trade.Profitable) / (double)resolved.Count,
                resolved.Count == 0 ? 0 : resolved.Average(t => t.Trade.NetR),
                resolved.Count == 0
                    ? 0
                    : resolved.Average(t =>
                        Math.Abs(t.Trade.TakeProfit - t.Trade.Entry) /
                        Math.Max(1e-12, t.Trade.Risk)));
        }

        private static List<ValidationTrade> AbcSizeFiltered(IReadOnlyList<ValidationTrade> trades)
        {
            return trades.Where(t => t.Trade.Size >= VALIDATION_SIZE_FILTER).ToList();
        }

        private static double AbcValidationAuc(
            IReadOnlyList<ValidationTrade> trades,
            Func<ValidationTrade, double> value)
        {
            List<ValidationTrade> resolved = trades
                .Where(t => t.Trade.Resolved && IsAbcFinite(value(t)))
                .ToList();
            int positives = resolved.Count(t => t.Trade.NetR > 0);
            int negatives = resolved.Count - positives;
            if (positives == 0 || negatives == 0)
                return 0.5;

            double sum = 0;
            foreach (ValidationTrade positive in resolved.Where(t => t.Trade.NetR > 0))
            {
                foreach (ValidationTrade negative in resolved.Where(t => t.Trade.NetR <= 0))
                {
                    double p = value(positive);
                    double n = value(negative);
                    sum += p > n ? 1 : p < n ? 0 : 0.5;
                }
            }

            return sum / (positives * (double)negatives);
        }

        private static double AbcValidationQuantileNetR(
            IReadOnlyList<ValidationTrade> trades,
            Func<ValidationTrade, double> value,
            double quantile)
        {
            List<ValidationTrade> resolved = trades
                .Where(t => t.Trade.Resolved && IsAbcFinite(value(t)))
                .ToList();
            if (resolved.Count < 5)
                return 0;

            List<ValidationTrade> ordered = resolved.OrderBy(value).ToList();
            int from = (int)(quantile * ordered.Count);
            int to = Math.Min(ordered.Count, from + Math.Max(1, ordered.Count / 5));
            from = Math.Min(from, ordered.Count - 1);
            List<ValidationTrade> slice = ordered.Skip(from).Take(to - from).ToList();
            return slice.Count == 0 ? 0 : slice.Average(t => t.Trade.NetR);
        }

        private static bool IsAbcFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        #endregion

        #region Report

        private static string BuildAbcValidationReport(IReadOnlyList<ValidationDataset> datasets)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal → impulse: pre-registered validation of the A-B-C size filter");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(
                "Pre-registered rule, fixed **before** this run and not re-tuned: trade only " +
                $"when `MovementStatistic.Size >= {VALIDATION_SIZE_FILTER.ToString("F3", CultureInfo.InvariantCulture)}` " +
                "(`Size = |A| / price`). The same threshold is applied to every cell; no " +
                "other filter, no ENDING split, no pair selection.");
            builder.AppendLine();
            builder.AppendLine(
                "Datasets: the eight fresh **m15** pairs of Phase 1 and eight **new h1 " +
                "holdout** pairs that are in neither the h1 tuning nor the h1 fresh set " +
                "(`AUDCHF`, `CADCHF`, `CHFJPY`, `EURAUD`, `EURCAD`, `EURNZD`, `GBPAUD`, " +
                "`GBPNZD`). Exact corridor (`W2/W1 >= 0.5`, `W4/W3 <= 0.382`), `ALL` mode, " +
                "2 bp costs, stop-first on a tie. Stop = start of A, target = end of A " +
                "(TakeRatio 1.0). Primary execution `nextopen`; `close` is the robustness " +
                "check. EnterRatio 0.35 and 0.50.");
            builder.AppendLine();
            builder.AppendLine(
                "`Size` is measured on the A leg only (`MovementStatistic`), so the filter " +
                "is causal. `avg R:R` is the planned target/risk of resolved entries, not a " +
                "realized payoff.");
            builder.AppendLine();

            AppendAbcValidationMainTable(builder, datasets);
            AppendAbcValidationSplitTable(builder, datasets);
            AppendAbcValidationPairTable(builder, datasets);
            AppendAbcValidationYearTable(builder, datasets);
            AppendAbcValidationNormalizedTable(builder, datasets);
            AppendAbcValidationVerdict(builder, datasets);

            return builder.ToString();
        }

        private static void AppendAbcValidationMainTable(
            StringBuilder builder,
            IReadOnlyList<ValidationDataset> datasets)
        {
            builder.AppendLine("## Main results");
            builder.AppendLine();
            builder.AppendLine("Unfiltered baseline and the pre-registered `size >= 0.012` " +
                               "filter side by side.");
            builder.AppendLine();
            builder.AppendLine("| Dataset | Exec | Enter | Variant | N | Resolved | profitable | avg netR | avg R:R |");
            builder.AppendLine("|---|---|---:|---|---:|---:|---:|---:|---:|");

            foreach (ValidationDataset dataset in datasets)
            {
                foreach (ValidationRun run in dataset.Runs)
                {
                    AppendAbcValidationVariantRow(builder, dataset.Name, run, "unfiltered", run.Trades);
                    AppendAbcValidationVariantRow(builder, dataset.Name, run, "size>=0.012",
                        AbcSizeFiltered(run.Trades));
                }
            }
        }

        private static void AppendAbcValidationVariantRow(
            StringBuilder builder,
            string dataset,
            ValidationRun run,
            string variant,
            IReadOnlyList<ValidationTrade> trades)
        {
            AbcValidationStats stats = AbcValidationStatsOf(trades);
            builder.AppendLine(FormattableString.Invariant(
                $"| {dataset} | {run.Execution} | {run.EnterRatio:F2} | {variant} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {stats.AvgRr:F2} |"));
        }

        private static void AppendAbcValidationSplitTable(
            StringBuilder builder,
            IReadOnlyList<ValidationDataset> datasets)
        {
            builder.AppendLine();
            builder.AppendLine("## Train / test");
            builder.AppendLine();
            builder.AppendLine(
                "Pre-registered split: train 2019–2022, test 2023–2026. The three m15 files " +
                "that start in 2017 also have pre-2019 entries; they are shown as a separate " +
                "row so no trade is silently dropped. Primary `nextopen` only.");
            builder.AppendLine();
            builder.AppendLine("| Dataset | Enter | Variant | Period | N | profitable | avg netR |");
            builder.AppendLine("|---|---:|---|---|---:|---:|---:|");

            foreach (ValidationDataset dataset in datasets)
            {
                foreach (ValidationRun run in dataset.Runs
                             .Where(r => r.Execution == "nextopen"))
                {
                    foreach ((string variant, IReadOnlyList<ValidationTrade> trades) in new[]
                             {
                                 ("unfiltered", (IReadOnlyList<ValidationTrade>)run.Trades),
                                 ("size>=0.012", AbcSizeFiltered(run.Trades))
                             })
                    {
                        AppendAbcValidationPeriodRow(builder, dataset.Name, run, variant,
                            "pre-2019", trades.Where(t => t.Trade.EntryTime.Year < 2019).ToList());
                        AppendAbcValidationPeriodRow(builder, dataset.Name, run, variant,
                            "2019–2022", trades.Where(t =>
                                t.Trade.EntryTime.Year >= 2019 &&
                                t.Trade.EntryTime.Year <= VALIDATION_TRAIN_LAST_YEAR).ToList());
                        AppendAbcValidationPeriodRow(builder, dataset.Name, run, variant,
                            "2023–2026", trades.Where(t =>
                                t.Trade.EntryTime.Year > VALIDATION_TRAIN_LAST_YEAR).ToList());
                    }
                }
            }
        }

        private static void AppendAbcValidationPeriodRow(
            StringBuilder builder,
            string dataset,
            ValidationRun run,
            string variant,
            string period,
            IReadOnlyList<ValidationTrade> trades)
        {
            if (trades.Count == 0)
                return;

            AbcValidationStats stats = AbcValidationStatsOf(trades);
            builder.AppendLine(FormattableString.Invariant(
                $"| {dataset} | {run.EnterRatio:F2} | {variant} | {period} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
        }

        private static void AppendAbcValidationPairTable(
            StringBuilder builder,
            IReadOnlyList<ValidationDataset> datasets)
        {
            builder.AppendLine();
            builder.AppendLine("## By pair");
            builder.AppendLine();
            builder.AppendLine("Primary `nextopen`, both enter ratios, both variants. Pairs " +
                               "with no filtered trade are shown as zero rows.");
            builder.AppendLine();
            builder.AppendLine("| Dataset | Pair | Enter | Variant | N | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---|---:|---:|---:|");

            foreach (ValidationDataset dataset in datasets)
            {
                foreach (ValidationRun run in dataset.Runs
                             .Where(r => r.Execution == "nextopen"))
                {
                    foreach (IGrouping<string, ValidationTrade> pair in run.Trades
                                 .GroupBy(t => t.Trade.Symbol)
                                 .OrderBy(g => g.Key, StringComparer.Ordinal))
                    {
                        AppendAbcValidationPairRow(builder, dataset.Name, pair.Key, run,
                            "unfiltered", pair.ToList());
                        AppendAbcValidationPairRow(builder, dataset.Name, pair.Key, run,
                            "size>=0.012", AbcSizeFiltered(pair.ToList()));
                    }
                }
            }
        }

        private static void AppendAbcValidationPairRow(
            StringBuilder builder,
            string dataset,
            string pair,
            ValidationRun run,
            string variant,
            IReadOnlyList<ValidationTrade> trades)
        {
            AbcValidationStats stats = AbcValidationStatsOf(trades);
            builder.AppendLine(FormattableString.Invariant(
                $"| {dataset} | {pair} | {run.EnterRatio:F2} | {variant} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
        }

        private static void AppendAbcValidationYearTable(
            StringBuilder builder,
            IReadOnlyList<ValidationDataset> datasets)
        {
            builder.AppendLine();
            builder.AppendLine("## By year");
            builder.AppendLine();
            builder.AppendLine("Primary `nextopen`, both enter ratios, both variants.");
            builder.AppendLine();
            builder.AppendLine("| Dataset | Year | Enter | Variant | N | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---|---:|---:|---:|");

            foreach (ValidationDataset dataset in datasets)
            {
                foreach (ValidationRun run in dataset.Runs
                             .Where(r => r.Execution == "nextopen"))
                {
                    foreach (IGrouping<int, ValidationTrade> year in run.Trades
                                 .GroupBy(t => t.Trade.EntryTime.Year)
                                 .OrderBy(g => g.Key))
                    {
                        AppendAbcValidationYearRow(builder, dataset.Name, year.Key, run,
                            "unfiltered", year.ToList());
                        AppendAbcValidationYearRow(builder, dataset.Name, year.Key, run,
                            "size>=0.012", AbcSizeFiltered(year.ToList()));
                    }
                }
            }
        }

        private static void AppendAbcValidationYearRow(
            StringBuilder builder,
            string dataset,
            int year,
            ValidationRun run,
            string variant,
            IReadOnlyList<ValidationTrade> trades)
        {
            AbcValidationStats stats = AbcValidationStatsOf(trades);
            builder.AppendLine(FormattableString.Invariant(
                $"| {dataset} | {year} | {run.EnterRatio:F2} | {variant} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
        }

        /// <summary>
        /// Secondary, explicitly not a replacement for the pre-registered rule: the same A
        /// size divided by the median bar range of A. That ratio is dimensionless (how many
        /// median A bars fit into A), so it is comparable across instruments. Only its
        /// separator quality is audited; no new threshold is selected here.
        /// </summary>
        private static void AppendAbcValidationNormalizedTable(
            StringBuilder builder,
            IReadOnlyList<ValidationDataset> datasets)
        {
            builder.AppendLine();
            builder.AppendLine("## Secondary: ATR-normalized size of A");
            builder.AppendLine();
            builder.AppendLine(
                "`size_norm = |A| / median(high−low of the A bars)` — the A size expressed " +
                "in median A bars, independent of price. This is a **separator audit only**: " +
                "AUC for `netR > 0` among resolved trades and the mean netR of the bottom and " +
                "top quintiles. No threshold is applied and nothing here replaces the " +
                "pre-registered `size >= 0.012` rule. Primary `nextopen`.");
            builder.AppendLine();
            builder.AppendLine("| Dataset | Enter | N | AUC size | AUC size_norm | q1 size_norm netR | q5 size_norm netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");

            foreach (ValidationDataset dataset in datasets)
            {
                foreach (ValidationRun run in dataset.Runs
                             .Where(r => r.Execution == "nextopen"))
                {
                    List<ValidationTrade> trades = run.Trades;
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {dataset.Name} | {run.EnterRatio:F2} | {trades.Count} | {AbcValidationAuc(trades, t => t.Trade.Size):F3} | {AbcValidationAuc(trades, t => t.NormalizedSize):F3} | {AbcValidationQuantileNetR(trades, t => t.NormalizedSize, 0.2):F3} | {AbcValidationQuantileNetR(trades, t => t.NormalizedSize, 0.8):F3} |"));
                }
            }
        }

        private static void AppendAbcValidationVerdict(
            StringBuilder builder,
            IReadOnlyList<ValidationDataset> datasets)
        {
            builder.AppendLine();
            builder.AppendLine("## Verdict");
            builder.AppendLine();

            foreach (ValidationDataset dataset in datasets)
            {
                foreach (ValidationRun run in dataset.Runs
                             .Where(r => r.Execution == "nextopen"))
                {
                    AbcValidationStats unfiltered = AbcValidationStatsOf(run.Trades);
                    List<ValidationTrade> filteredTrades = AbcSizeFiltered(run.Trades);
                    AbcValidationStats filtered = AbcValidationStatsOf(filteredTrades);
                    AbcValidationStats train = AbcValidationStatsOf(filteredTrades
                        .Where(t => t.Trade.EntryTime.Year >= 2019 &&
                                    t.Trade.EntryTime.Year <= VALIDATION_TRAIN_LAST_YEAR)
                        .ToList());
                    AbcValidationStats test = AbcValidationStatsOf(filteredTrades
                        .Where(t => t.Trade.EntryTime.Year > VALIDATION_TRAIN_LAST_YEAR)
                        .ToList());

                    builder.AppendLine(FormattableString.Invariant(
                        $"- **{dataset.Name} / nextopen / {run.EnterRatio:F2}.** Unfiltered: {unfiltered.N} trades, {unfiltered.Profitable:P1} profitable, {unfiltered.AvgNetR:F3}R. Filtered: {filtered.N}, {filtered.Profitable:P1}, {filtered.AvgNetR:F3}R ({filtered.N}/{unfiltered.N} kept). Train 2019–2022: {train.N}, {train.AvgNetR:F3}R; test 2023–2026: {test.N}, {test.AvgNetR:F3}R."));
                }
            }

            builder.AppendLine();
            builder.AppendLine("### Checks");
            builder.AppendLine();
            builder.AppendLine("| Dataset | Enter | Filtered N | Filtered netR | > unfiltered | Train > 0 | Test > 0 | Positive years |");
            builder.AppendLine("|---|---:|---:|---:|---|---|---|---:|");

            int checksPassed = 0;
            int checksTotal = 0;
            foreach (ValidationDataset dataset in datasets)
            {
                foreach (ValidationRun run in dataset.Runs
                             .Where(r => r.Execution == "nextopen"))
                {
                    List<ValidationTrade> filteredTrades = AbcSizeFiltered(run.Trades);
                    AbcValidationStats filtered = AbcValidationStatsOf(filteredTrades);
                    AbcValidationStats unfiltered = AbcValidationStatsOf(run.Trades);
                    AbcValidationStats train = AbcValidationStatsOf(filteredTrades
                        .Where(t => t.Trade.EntryTime.Year >= 2019 &&
                                    t.Trade.EntryTime.Year <= VALIDATION_TRAIN_LAST_YEAR)
                        .ToList());
                    AbcValidationStats test = AbcValidationStatsOf(filteredTrades
                        .Where(t => t.Trade.EntryTime.Year > VALIDATION_TRAIN_LAST_YEAR)
                        .ToList());

                    List<IGrouping<int, ValidationTrade>> years = filteredTrades
                        .GroupBy(t => t.Trade.EntryTime.Year)
                        .OrderBy(g => g.Key)
                        .ToList();
                    int positiveYears = years.Count(g => AbcValidationStatsOf(g.ToList()).AvgNetR > 0);
                    bool better = filtered.AvgNetR > unfiltered.AvgNetR;
                    bool trainPositive = train.N > 0 && train.AvgNetR > 0;
                    bool testPositive = test.N > 0 && test.AvgNetR > 0;

                    if (better && trainPositive && testPositive)
                        checksPassed++;
                    checksTotal++;

                    builder.AppendLine(FormattableString.Invariant(
                        $"| {dataset.Name} | {run.EnterRatio:F2} | {filtered.N} | {filtered.AvgNetR:F3} | {(better ? "yes" : "no")} | {(trainPositive ? "yes" : "no")} | {(testPositive ? "yes" : "no")} | {positiveYears}/{years.Count} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine(
                "A cell passes only when the filtered result beats the unfiltered baseline " +
                "**and** is positive on both halves of the split. This is a sign test on two " +
                "datasets, not a significance test; the trade counts are small, so a single " +
                "cell is not evidence of an edge.");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"**Mechanical result: {checksPassed}/{checksTotal} primary cells pass all three checks.**"));
        }

        private static string BuildAbcValidationCsv(
            IReadOnlyList<ValidationDataset> datasets)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "dataset,execution,enter,file,symbol,entry_time,is_up,size,median_range_a," +
                "size_norm,filtered,bars_a,a_to_diagonal,entry,stop,tp,risk,entry_delay," +
                "b_ratio,outcome,exit_price,outcome_bar,net_r,resolved,profitable,mfe_r," +
                "mae_r,overlap_depth,overlap_degree,ratio_zigzag,heterogeneity,area," +
                "max_distance,candles,elliott_v1");

            foreach (ValidationDataset dataset in datasets)
            {
                foreach (ValidationRun run in dataset.Runs)
                {
                    foreach (ValidationTrade item in run.Trades
                                 .OrderBy(t => t.Trade.Symbol, StringComparer.Ordinal)
                                 .ThenBy(t => t.Trade.EntryTime))
                    {
                        AbcTrade t = item.Trade;
                        builder.AppendLine(string.Join(",",
                            Csv(dataset.Name),
                            Csv(run.Execution),
                            F(run.EnterRatio),
                            Csv(t.File),
                            Csv(t.Symbol),
                            Csv(t.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
                            B(t.IsUpDiagonal),
                            F(t.Size),
                            F(item.MedianRangeA),
                            F(item.NormalizedSize),
                            B(t.Size >= VALIDATION_SIZE_FILTER),
                            t.BarsA.ToString(CultureInfo.InvariantCulture),
                            F(t.AToDiagonal),
                            F(t.Entry),
                            F(t.StopLoss),
                            F(t.TakeProfit),
                            F(t.Risk),
                            t.EntryDelayBars.ToString(CultureInfo.InvariantCulture),
                            F(t.BRatio),
                            Csv(t.Outcome),
                            F(t.ExitPrice),
                            t.OutcomeBar.ToString(CultureInfo.InvariantCulture),
                            F(t.NetR),
                            B(t.Resolved),
                            B(t.Profitable),
                            F(t.MfeR),
                            F(t.MaeR),
                            F(t.OverlapDepth),
                            F(t.OverlapDegree),
                            F(t.RatioZigzag),
                            F(t.Heterogeneity),
                            F(t.Area),
                            F(t.MaxDistance),
                            t.Candles.ToString(CultureInfo.InvariantCulture),
                            B(t.ElliottV1)));
                    }
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Types

        private sealed class ValidationDataset
        {
            public ValidationDataset(string name, int fileCount, int anchors)
            {
                Name = name;
                FileCount = fileCount;
                Anchors = anchors;
            }

            public string Name { get; }
            public int FileCount { get; }
            public int Anchors { get; }
            public double LoadSeconds { get; set; }
            public List<ValidationRun> Runs { get; } = new();
        }

        private sealed class ValidationRun
        {
            public ValidationRun(
                string execution,
                double enterRatio,
                int anchors,
                int aLegs,
                List<ValidationTrade> trades)
            {
                Execution = execution;
                EnterRatio = enterRatio;
                Anchors = anchors;
                ALegs = aLegs;
                Trades = trades;
            }

            public string Execution { get; }
            public double EnterRatio { get; }
            public int Anchors { get; }
            public int ALegs { get; }
            public List<ValidationTrade> Trades { get; }
        }

        private sealed class ValidationTrade
        {
            public ValidationTrade(AbcTrade trade, double medianRangeA)
            {
                Trade = trade;
                MedianRangeA = medianRangeA;
            }

            public AbcTrade Trade { get; }
            public double MedianRangeA { get; }

            /// <summary>|A| in median A bars; dimensionless and price-independent.</summary>
            public double NormalizedSize =>
                Math.Abs(Trade.AEnd - Trade.AStart) / Math.Max(1e-12, MedianRangeA);
        }

        private sealed record AbcValidationStats(
            int N,
            int Resolved,
            double Profitable,
            double AvgNetR,
            double AvgRr);

        #endregion
    }
}
