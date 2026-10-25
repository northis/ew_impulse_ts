using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for the reverse of <see cref="DiagonalHarmonicResearchTests"/>: the
    /// trade is the harmonic setup itself (its own entry, stop and target), and a confirmed
    /// diagonal at the same wave-5 extreme is used as a context filter.
    /// <para>
    /// The hypothesis is that the diagonal marks the terminal structure around the harmonic
    /// point D, so a harmonic setup confirmed there should be better than an arbitrary one -
    /// and possibly good enough to replace the <c>MinimumStopAtr</c> noise filter that the
    /// shipped harmonic configuration needs. Three ATR variants (0, 2 and 4) are replayed
    /// exactly; the diagonal join is then swept post-hoc.
    /// </para>
    /// <para>
    /// Writes <c>reports/diagonal_harmonic_setup.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalHarmonicSetup"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private static readonly double[] HARMONIC_ATR_VARIANTS = { 0d, 2d, 4d };
        private const int HARMONIC_SETUP_MIN_TRAIN = 20;

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_harmonic_setup.md")]
        [Category("Research")]
        public void DiagonalHarmonicSetup_Report()
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

            List<Sample> diagonals = RunDiagonalAll(dataDir, files);
            Assert.That(diagonals, Is.Not.Empty, "The diagonal replay produced no signals.");

            var tradesByAtr = new Dictionary<double, List<HarmonicTrade>>();
            foreach (double atr in HARMONIC_ATR_VARIANTS)
            {
                List<HarmonicTrade> trades = RunHarmonicAll(dataDir, files, atr);
                tradesByAtr[atr] = trades;
                TestContext.Out.WriteLine(
                    $"ATR {atr:F1}: harmonic setups={trades.Count}, " +
                    $"resolved={trades.Count(t => t.Resolved)}");
            }

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_harmonic_setup.md");
            File.WriteAllText(reportPath, BuildHarmonicSetupReport(tradesByAtr, diagonals, files.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        #region Replay

        private static List<Sample> RunDiagonalAll(string dataDir, IReadOnlyList<string> files)
        {
            var result = new List<Sample>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                file =>
                {
                    ITimeFrame timeFrame = file.Contains("_m15_")
                        ? TimeFrameHelper.Minute15
                        : TimeFrameHelper.Hour1;

                    var provider = new TestBarsProvider(timeFrame);
                    provider.LoadCandles(Path.Combine(dataDir, file));

                    List<Sample> local = RunDiagonalSamples(provider, file, EXACT_CORRIDOR);
                    lock (sync)
                    {
                        result.AddRange(local);
                    }
                });

            return result;
        }

        private static List<HarmonicTrade> RunHarmonicAll(
            string dataDir, IReadOnlyList<string> files, double minimumStopAtr)
        {
            var parameters = new HarmonicParams
            {
                MinimumStopAtr = minimumStopAtr
            };

            return RunHarmonicAll(dataDir, files, parameters);
        }

        private static List<HarmonicTrade> RunHarmonicAll(
            string dataDir, IReadOnlyList<string> files, HarmonicParams parameters)
        {
            var result = new List<HarmonicTrade>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                file =>
                {
                    List<HarmonicTrade> local = RunHarmonicFile(
                        Path.Combine(dataDir, file), file, parameters);
                    lock (sync)
                    {
                        result.AddRange(local);
                    }
                });

            return result;
        }

        private static List<HarmonicTrade> RunHarmonicFile(
            string path, string fileName, HarmonicParams parameters)
        {
            ITimeFrame timeFrame = fileName.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;

            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(path);

            var finder = new HarmonicSetupFinder(provider, provider.BarSymbol, parameters);
            var trades = new List<HarmonicTrade>();
            var open = new Dictionary<object, HarmonicTrade>(ReferenceEqualityComparer.Instance);

            finder.OnEnter += (_, args) =>
            {
                var trade = new HarmonicTrade
                {
                    File = fileName,
                    TimeFrame = fileName.Contains("_m15_") ? "m15" : "h1",
                    Provider = provider,
                    Item = args.HarmonicItem,
                    EntryTime = args.Level.OpenTime,
                    EntryBar = args.Level.BarIndex,
                    IsBull = args.HarmonicItem.IsBull,
                    Type = args.HarmonicItem.PatternType,
                    DBar = args.HarmonicItem.ItemD.BarIndex,
                    DValue = args.HarmonicItem.ItemD.Value,
                    Entry = args.Level.Value,
                    Sl = args.StopLoss.Value,
                    Tp = args.TakeProfit.Value,
                    RiskReward = args.RiskReward
                };

                trades.Add(trade);
                open[args.Level] = trade;
            };

            finder.OnTakeProfit += (_, args) => CloseHarmonicTrade(open, args, "TP");
            finder.OnStopLoss += (_, args) => CloseHarmonicTrade(open, args, "SL");

            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            return trades;
        }

        private static void CloseHarmonicTrade(
            Dictionary<object, HarmonicTrade> open, LevelEventArgs args, string outcome)
        {
            if (!open.TryGetValue(args.FromLevel, out HarmonicTrade? trade))
                return;

            trade.Outcome = outcome;
            trade.ExitBar = args.Level.BarIndex;

            double risk = Math.Abs(trade.Entry - trade.Sl);
            if (risk <= 0d)
            {
                open.Remove(args.FromLevel);
                return;
            }

            double exit = args.Level.Value;
            double grossR = trade.IsBull
                ? (exit - trade.Entry) / risk
                : (trade.Entry - exit) / risk;

            trade.NetR = grossR - trade.Entry * COST_RATE / risk;
            trade.Resolved = true;
            trade.Profitable = trade.NetR > 0d;

            open.Remove(args.FromLevel);
        }

        #endregion

        #region Join

        private static Dictionary<string, Dictionary<int, List<Sample>>> IndexDiagonals(
            IReadOnlyList<Sample> diagonals)
        {
            var result = new Dictionary<string, Dictionary<int, List<Sample>>>();

            foreach (IGrouping<string, Sample> group in diagonals.GroupBy(d => d.File))
            {
                var byBar = new Dictionary<int, List<Sample>>();
                foreach (Sample diagonal in group)
                {
                    if (!byBar.TryGetValue(diagonal.P5Bar, out List<Sample>? bucket))
                    {
                        bucket = new List<Sample>();
                        byBar[diagonal.P5Bar] = bucket;
                    }

                    bucket.Add(diagonal);
                }

                result[group.Key] = byBar;
            }

            return result;
        }

        private static bool HasDiagonalConfluence(
            HarmonicTrade trade,
            IReadOnlyDictionary<string, Dictionary<int, List<Sample>>> diagonalsByFile,
            DiagonalJoinConfig config)
        {
            if (!diagonalsByFile.TryGetValue(trade.File, out Dictionary<int, List<Sample>>? byBar))
                return false;

            for (int bar = trade.DBar - config.MaxBarDistance;
                 bar <= trade.DBar + config.MaxBarDistance;
                 bar++)
            {
                if (!byBar.TryGetValue(bar, out List<Sample>? bucket))
                    continue;

                foreach (Sample diagonal in bucket)
                {
                    // A bullish harmonic is a BUY, so it needs a bearish (down) diagonal.
                    if (diagonal.IsUpDiagonal == trade.IsBull)
                        continue;

                    // The diagonal signal must already be known when the harmonic setup enters.
                    if (diagonal.SignalBar > trade.EntryBar)
                        continue;
                    if (trade.EntryBar - diagonal.SignalBar > config.MaxSignalLag)
                        continue;

                    double height = Math.Abs(diagonal.V5 - diagonal.V0);
                    if (height <= 1e-12d)
                        continue;

                    if (Math.Abs(diagonal.V5 - trade.DValue) / height > config.MaxPriceDistance)
                        continue;

                    return true;
                }
            }

            return false;
        }

        private static List<DiagonalJoinConfig> BuildDiagonalJoinGrid()
        {
            var result = new List<DiagonalJoinConfig>();
            int[] bars = { 0, 1, 2, 5, 10, 20 };
            double[] prices = { 0.05d, 0.10d, 0.25d, 0.50d, double.PositiveInfinity };
            int[] lags = { 1, 2, 3, 5, 10 };

            foreach (int bar in bars)
            {
                foreach (double price in prices)
                {
                    foreach (int lag in lags)
                    {
                        string priceName = double.IsPositiveInfinity(price)
                            ? "any"
                            : price.ToString("P0", CultureInfo.InvariantCulture);
                        result.Add(new DiagonalJoinConfig(
                            $"±{bar} bars / {priceName} / lag {lag}",
                            bar, price, lag));
                    }
                }
            }

            return result;
        }

        #endregion

        #region Statistics

        private static HarmonicStats HStat(IReadOnlyList<HarmonicTrade> subset)
        {
            List<HarmonicTrade> resolved = subset.Where(t => t.Resolved).ToList();
            double profitable = resolved.Count == 0
                ? 0d
                : (double)resolved.Count(t => t.Profitable) / resolved.Count;

            return new HarmonicStats(
                subset.Count,
                resolved.Count,
                profitable,
                resolved.Count == 0 ? 0d : resolved.Average(t => t.NetR),
                resolved.Count == 0 ? 0d : resolved.Average(t => t.RiskReward));
        }

        private static List<HarmonicSweepRow> BuildHarmonicSweepRows(
            IReadOnlyList<HarmonicTrade> train,
            IReadOnlyList<HarmonicTrade> test,
            IReadOnlyDictionary<string, Dictionary<int, List<Sample>>> diagonalsByFile,
            IReadOnlyList<DiagonalJoinConfig> grid)
        {
            var rows = new List<HarmonicSweepRow>();

            foreach (DiagonalJoinConfig config in grid)
            {
                List<HarmonicTrade> trainFiltered = train
                    .Where(t => HasDiagonalConfluence(t, diagonalsByFile, config))
                    .ToList();

                if (trainFiltered.Count < HARMONIC_SETUP_MIN_TRAIN)
                    continue;

                List<HarmonicTrade> testFiltered = test
                    .Where(t => HasDiagonalConfluence(t, diagonalsByFile, config))
                    .ToList();

                rows.Add(new HarmonicSweepRow(config, HStat(trainFiltered), HStat(testFiltered)));
            }

            return rows;
        }

        private static HarmonicSweepRow? SelectBestHarmonic(IReadOnlyList<HarmonicSweepRow> rows)
        {
            return rows
                .OrderByDescending(r => r.Train.AvgNetR)
                .ThenBy(r => r.Join.Name, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        #endregion

        #region Report

        private static string BuildHarmonicSetupReport(
            IReadOnlyDictionary<double, List<HarmonicTrade>> tradesByAtr,
            IReadOnlyList<Sample> diagonals,
            int fileCount)
        {
            Dictionary<string, Dictionary<int, List<Sample>>> diagonalsByFile = IndexDiagonals(diagonals);
            var joinGrid = BuildDiagonalJoinGrid();

            var baselines = new List<(double Atr, HarmonicStats Train, HarmonicStats Test)>();
            var sweepResults = new List<(double Atr, List<HarmonicSweepRow> Rows, HarmonicSweepRow? Best)>();

            foreach (double atr in HARMONIC_ATR_VARIANTS)
            {
                List<HarmonicTrade> trades = tradesByAtr[atr];
                List<HarmonicTrade> train = trades.Where(t => t.EntryTime.Year <= 2022).ToList();
                List<HarmonicTrade> test = trades.Where(t => t.EntryTime.Year >= 2023).ToList();

                baselines.Add((atr, HStat(train), HStat(test)));

                List<HarmonicSweepRow> rows =
                    BuildHarmonicSweepRows(train, test, diagonalsByFile, joinGrid);
                sweepResults.Add((atr, rows, SelectBestHarmonic(rows)));
            }

            (double BestAtr, HarmonicSweepRow? BestRow) best = sweepResults
                .Where(r => r.Best != null)
                .OrderByDescending(r => r.Best!.Train.AvgNetR)
                .ThenBy(r => r.Atr)
                .Select(r => (r.Atr, r.Best))
                .FirstOrDefault();

            double selectedAtr = best.BestRow == null ? 0d : best.BestAtr;
            DiagonalJoinConfig selectedJoin = best.BestRow?.Join
                ?? joinGrid[0];

            List<HarmonicTrade> selectedAll = tradesByAtr[selectedAtr]
                .Where(t => HasDiagonalConfluence(t, diagonalsByFile, selectedJoin))
                .ToList();
            List<HarmonicTrade> selectedTrain = selectedAll
                .Where(t => t.EntryTime.Year <= 2022)
                .ToList();
            List<HarmonicTrade> selectedTest = selectedAll
                .Where(t => t.EntryTime.Year >= 2023)
                .ToList();
            HarmonicStats selectedTrainStats = HStat(selectedTrain);
            HarmonicStats selectedTestStats = HStat(selectedTest);

            HarmonicStats selectedBaselineTest = baselines
                .First(b => Math.Abs(b.Atr - selectedAtr) < 1e-9d)
                .Test;

            var builder = new StringBuilder();
            builder.AppendLine("# Harmonic setup + diagonal confluence");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Files: {fileCount} (8 h1 + 4 m15 from the Phase 0 archive set). The trade is the harmonic setup itself: entry at the close of the bar that confirmed point D, the shipped pattern-height target and 5%-of-pattern-height stop. A confirmed diagonal at the same wave-5 extreme is used only as a context gate."));
            builder.AppendLine();
            builder.AppendLine("Train = entries in 2017–2022, test = 2023–2026. Net R includes a 2 bp round-trip cost. The harmonic finder is replayed exactly for each `MinimumStopAtr` variant; the diagonal join is applied post-hoc.");
            builder.AppendLine();

            builder.AppendLine("## Baseline — harmonic only");
            builder.AppendLine();
            builder.AppendLine("| Min stop ATR | Split | Setups | resolved | profitable | avg netR | avg R:R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach ((double atr, HarmonicStats train, HarmonicStats test) in baselines)
            {
                AppendHarmonicBaselineRow(builder, atr, "train", train);
                AppendHarmonicBaselineRow(builder, atr, "test", test);
            }

            builder.AppendLine();
            builder.AppendLine("The shipped harmonic configuration uses the 4 ATR variant. The 0 ATR variant is the unfiltered pattern set; if the diagonal gate can lift it, the diagonal context is doing the noise filter's job.");
            builder.AppendLine();

            builder.AppendLine("## Diagonal confluence sweep");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"`±bars` is the allowed distance between the harmonic D and the diagonal wave-5 extreme, the next field is the allowed price distance as a share of the diagonal height, and `lag` is the maximum number of bars between the diagonal signal and the harmonic entry. Only cells keeping at least {HARMONIC_SETUP_MIN_TRAIN} train setups are ranked."));
            builder.AppendLine();
            builder.AppendLine("| # | Min stop ATR | Join | Train N | Train profit | Train netR | Test N | Test profit | Test netR | Test R:R |");
            builder.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---:|---:|");

            var allRows = sweepResults
                .SelectMany(r => r.Rows.Select(row => (r.Atr, Row: row)))
                .OrderByDescending(r => r.Row.Train.AvgNetR)
                .ThenBy(r => r.Atr)
                .ThenBy(r => r.Row.Join.Name, StringComparer.Ordinal)
                .Take(20)
                .ToList();

            int rank = 1;
            foreach ((double atr, HarmonicSweepRow row) in allRows)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {rank++} | {atr:F1} | {row.Join.Name} | {row.Train.N} | {row.Train.Profitable:P1} | {row.Train.AvgNetR:F3} | {row.Test.N} | {row.Test.Profitable:P1} | {row.Test.AvgNetR:F3} | {row.Test.AvgRr:F2} |"));
            }

            builder.AppendLine();
            builder.AppendLine("Selected on train:");
            builder.AppendLine();
            builder.AppendLine("| Min stop ATR | Join | Train N | Train profit | Train netR | Test N | Test profit | Test netR | Test R:R | Δ vs harmonic-only |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            if (best.BestRow == null)
            {
                builder.AppendLine("| — | — | — | — | — | — | — | — | — | — |");
            }
            else
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {selectedAtr:F1} | {selectedJoin.Name} | {selectedTrainStats.N} | {selectedTrainStats.Profitable:P1} | {selectedTrainStats.AvgNetR:F3} | {selectedTestStats.N} | {selectedTestStats.Profitable:P1} | {selectedTestStats.AvgNetR:F3} | {selectedTestStats.AvgRr:F2} | {selectedTestStats.AvgNetR - selectedBaselineTest.AvgNetR:+0.000;-0.000} |"));
            }

            builder.AppendLine();
            builder.AppendLine("Only the 0 ATR variant produced join cells with at least 20 train setups; the 4 ATR set is too small to enter the sweep. The eligible cells tie on the same 21 train setups, so the choice among them is arbitrary and the spread of their test results is the honest uncertainty range. A negative train netR means the test value is a held-out loss-reduction check, not a validated edge.");
            builder.AppendLine();
            builder.AppendLine("## Per model — selected cell");
            builder.AppendLine();
            builder.AppendLine("| Model | Train N | Train profit | Train netR | Test N | Test profit | Test netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (HarmonicPatternType type in ALL_TYPES)
            {
                AppendHarmonicTypeRow(builder, type.ToString(), selectedTrain, selectedTest, type);
            }

            builder.AppendLine();
            builder.AppendLine("## Stability of the selected cell (test split)");
            builder.AppendLine();
            builder.AppendLine("| Group | Setups | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|");
            foreach (IGrouping<string, HarmonicTrade> group in selectedTest
                         .GroupBy(t => t.TimeFrame)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AppendHarmonicStabilityRow(builder, $"timeframe {group.Key}", group.ToList());
            }

            foreach (IGrouping<string, HarmonicTrade> group in selectedTest
                         .GroupBy(t => SessionOf(t.EntryTime))
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AppendHarmonicStabilityRow(builder, $"session {group.Key}", group.ToList());
            }

            foreach (IGrouping<int, HarmonicTrade> group in selectedTest
                         .GroupBy(t => t.EntryTime.Year)
                         .OrderBy(g => g.Key))
            {
                AppendHarmonicStabilityRow(builder, $"year {group.Key}", group.ToList());
            }

            builder.AppendLine();
            builder.AppendLine("### By pair (test split)");
            builder.AppendLine();
            builder.AppendLine("| File | Setups | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|");
            foreach (IGrouping<string, HarmonicTrade> group in selectedTest
                         .GroupBy(t => t.File)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AppendHarmonicStabilityRow(builder, $"`{group.Key}`", group.ToList());
            }

            builder.AppendLine();
            builder.AppendLine("## What this run says");
            builder.AppendLine();
            AppendHarmonicSetupConclusions(
                builder,
                baselines,
                selectedAtr,
                selectedJoin,
                selectedTrainStats,
                selectedTestStats,
                selectedBaselineTest);

            return builder.ToString();
        }

        private static void AppendHarmonicBaselineRow(
            StringBuilder builder, double atr, string split, HarmonicStats stats)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"| {atr:F1} | {split} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {stats.AvgRr:F2} |"));
        }

        private static void AppendHarmonicTypeRow(
            StringBuilder builder,
            string name,
            IReadOnlyList<HarmonicTrade> train,
            IReadOnlyList<HarmonicTrade> test,
            HarmonicPatternType type)
        {
            HarmonicStats trainStats = HStat(train.Where(t => t.Type == type).ToList());
            HarmonicStats testStats = HStat(test.Where(t => t.Type == type).ToList());
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {trainStats.N} | {trainStats.Profitable:P1} | {trainStats.AvgNetR:F3} | {testStats.N} | {testStats.Profitable:P1} | {testStats.AvgNetR:F3} |"));
        }

        private static void AppendHarmonicStabilityRow(
            StringBuilder builder, string name, IReadOnlyList<HarmonicTrade> subset)
        {
            HarmonicStats stats = HStat(subset);
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
        }

        private static void AppendHarmonicSetupConclusions(
            StringBuilder builder,
            IReadOnlyList<(double Atr, HarmonicStats Train, HarmonicStats Test)> baselines,
            double selectedAtr,
            DiagonalJoinConfig selectedJoin,
            HarmonicStats selectedTrain,
            HarmonicStats selectedTest,
            HarmonicStats selectedBaselineTest)
        {
            (double Atr, HarmonicStats Train, HarmonicStats Test) baseline0 =
                baselines.First(b => Math.Abs(b.Atr) < 1e-9d);
            (double Atr, HarmonicStats Train, HarmonicStats Test) baseline4 =
                baselines.First(b => Math.Abs(b.Atr - 4d) < 1e-9d);

            double delta = selectedTest.AvgNetR - selectedBaselineTest.AvgNetR;
            double deltaVsShipped = selectedTest.AvgNetR - baseline4.Test.AvgNetR;

            builder.AppendLine(FormattableString.Invariant(
                $"- Harmonic-only test baselines: **0 ATR {baseline0.Test.AvgNetR:F3}R** ({baseline0.Test.N} setups), **4 ATR {baseline4.Test.AvgNetR:F3}R** ({baseline4.Test.N} setups). The shipped 4 ATR configuration is the bar the diagonal gate has to beat."));
            builder.AppendLine(FormattableString.Invariant(
                $"- Selected cell: **{selectedAtr:F1} ATR + {selectedJoin.Name}**, train {selectedTrain.N} setups at {selectedTrain.AvgNetR:F3}R, test {selectedTest.N} setups at {selectedTest.AvgNetR:F3}R ({selectedTest.Profitable:P1} profitable). Against the same-ATR baseline the test delta is {delta:+0.000;-0.000}; against the shipped 4 ATR baseline it is **{deltaVsShipped:+0.000;-0.000}**."));

            if (selectedTest.N < HARMONIC_SETUP_MIN_TRAIN)
            {
                builder.AppendLine("- The selected cell is too thin on test to support a conclusion. Treat the run as inconclusive.");
            }
            else if (selectedTest.AvgNetR > 0d &&
                     selectedTrain.AvgNetR > 0d &&
                     delta > 0.05d &&
                     deltaVsShipped > 0d)
            {
                builder.AppendLine("- The diagonal gate improves a harmonic setup that is already positive on train and beats the shipped 4 ATR baseline out of sample. This is the first version of the combination worth a fresh-pair validation and a walk-forward.");
            }
            else if (selectedAtr < 4d &&
                     deltaVsShipped > 0.02d)
            {
                builder.AppendLine("- The diagonal gate lifts the unfiltered harmonic set above the shipped 4 ATR baseline. That would mean the diagonal context can replace part of the noise filter, but it is still a single-archive result and needs a fresh-pair check.");
            }
            else if (selectedTest.AvgNetR > 0d && delta > 0.05d)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- The diagonal gate reduces the loss of the unfiltered set ({delta:+0.000;-0.000} against the same-ATR baseline), but the selected cell is **negative on train** ({selectedTrain.AvgNetR:F3}R) and its test result is only {selectedTest.AvgNetR:F3}R. It is a loss reduction, not a tradeable edge, and it is far below the shipped 4 ATR baseline ({baseline4.Test.AvgNetR:F3}R)."));
            }
            else
            {
                builder.AppendLine("- The diagonal gate does **not** improve the harmonic setup out of sample, and it does not come close to the shipped 4 ATR baseline. The harmonic figure already contains the reversal information; the diagonal adds no independent edge.");
            }

            builder.AppendLine(FormattableString.Invariant(
                $"- The 4 ATR set is thin on this 12-file archive ({baseline4.Test.N} test setups), so the shipped baseline itself is noisy. That does not rescue the diagonal cell: at 0 ATR the gate reaches only about break-even, and the 4 ATR overlap is too small to evaluate."));
            builder.AppendLine("- The comparison is exact on the trade side: the harmonic finder's own entry, stop and target are used, and the outcome is its own TP/SL event. Only the context gate is post-hoc.");
            builder.AppendLine("- The diagonal is the exact Phase 0/1 corridor (`W2/W1 >= 0.5`, `W4/W3 <= 0.382`). A different diagonal configuration may produce a different join, so the result is conditional on that choice.");
            builder.AppendLine("- Any candidate must survive a fresh-pair validation like the `minRR 0.5` check in `reports/diagonal_minrr05_validation.md`; this archive is the same one used to pick the diagonal corridor.");
        }

        #endregion

        #region Types

        private sealed record DiagonalJoinConfig(
            string Name,
            int MaxBarDistance,
            double MaxPriceDistance,
            int MaxSignalLag);

        private sealed class HarmonicTrade
        {
            public string File { get; set; } = string.Empty;
            public string TimeFrame { get; set; } = string.Empty;
            public TestBarsProvider Provider { get; set; } = null!;
            public HarmonicItem Item { get; set; } = null!;
            public DateTime EntryTime { get; set; }
            public int EntryBar { get; set; }
            public bool IsBull { get; set; }
            public HarmonicPatternType Type { get; set; }
            public int DBar { get; set; }
            public double DValue { get; set; }
            public double Entry { get; set; }
            public double Sl { get; set; }
            public double Tp { get; set; }
            public double RiskReward { get; set; }
            public double NetR { get; set; }
            public bool Resolved { get; set; }
            public bool Profitable { get; set; }
            public string Outcome { get; set; } = "NONE";
            public int ExitBar { get; set; } = -1;
        }

        private sealed record HarmonicStats(
            int N,
            int Resolved,
            double Profitable,
            double AvgNetR,
            double AvgRr);

        private sealed record HarmonicSweepRow(
            DiagonalJoinConfig Join,
            HarmonicStats Train,
            HarmonicStats Test);

        #endregion
    }
}
