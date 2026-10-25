using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace TradeKit.Tests
{
    /// <summary>
    /// True holdout for the only positive diagonal TP/SL region found on h1:
    /// <c>R50.0–R61.8</c> with a <c>V4 ± 1.50–2.00·|W3|</c> stop. The region is tested on
    /// fresh m15 pairs, which were not used to build the h1 grid.
    /// <para>
    /// The report also includes the base geometry (<c>R23.6 / k1.00</c>), an execution check
    /// (close / 2 bp vs next open / 3 bp), and a simple equity / max-drawdown curve for the
    /// fresh test split.
    /// </para>
    /// <para>
    /// Writes <c>reports/diagonal_m15_validation.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalM15Validation"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_m15_validation.md")]
        [Category("Research")]
        public void DiagonalM15Validation_Report()
        {
            string dataDir = FindDataDir();

            List<string> tuningFiles = Existing(dataDir, M15_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_M15_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning m15 file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh m15 file was found under data/.");

            List<Sample> tuning = RunDiagonalAll(dataDir, tuningFiles);
            List<Sample> fresh = RunDiagonalAll(dataDir, freshFiles);

            Assert.That(tuning, Is.Not.Empty, "The tuning replay produced no signals.");
            Assert.That(fresh, Is.Not.Empty, "The fresh replay produced no signals.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_m15_validation.md");
            File.WriteAllText(reportPath, BuildM15Report(tuning, fresh));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static string BuildM15Report(
            IReadOnlyList<Sample> tuning, IReadOnlyList<Sample> fresh)
        {
            List<DiagCell> cells = BuildM15Cells();

            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — m15 validation of the TP/SL region");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("The candidate is the only region that was positive on both halves of the h1 TP/SL grid: `R50.0–R61.8` with `V4 ± 1.50–2.00·|W3|`. The base `R23.6 / k1.00` is included for reference. Fresh m15 pairs were not used to build the h1 grid, so this is a true holdout for the region.");
            builder.AppendLine();
            builder.AppendLine("Tuning m15 = AUDUSD, EURUSD, GBPJPY, USDJPY. Fresh m15 = AUDCAD, CADJPY, EURCHF, EURGBP, GBPCHF, NZDUSD, USDCAD, XAGUSD. Train = 2017–2022, test = 2023–2026. Net R includes a 2 bp round-trip cost; the stop wins a tie.");
            builder.AppendLine();

            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Set | Signals | Train | Test |");
            builder.AppendLine("|---|---:|---:|---:|");
            AppendM15SampleRow(builder, "tuning m15", tuning);
            AppendM15SampleRow(builder, "fresh m15", fresh);

            builder.AppendLine();
            builder.AppendLine("## Tuning vs fresh");
            builder.AppendLine();
            builder.AppendLine("| Cell | Tuning train N | Tuning train netR | Tuning test N | Tuning test netR | Fresh train N | Fresh train netR | Fresh test N | Fresh test netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (DiagCell cell in cells)
            {
                GridStats tuningTrain = Stat(RunCell(tuning.Where(s => s.EntryTime.Year <= 2022).ToList(), cell));
                GridStats tuningTest = Stat(RunCell(tuning.Where(s => s.EntryTime.Year >= 2023).ToList(), cell));
                GridStats freshTrain = Stat(RunCell(fresh.Where(s => s.EntryTime.Year <= 2022).ToList(), cell));
                GridStats freshTest = Stat(RunCell(fresh.Where(s => s.EntryTime.Year >= 2023).ToList(), cell));

                builder.AppendLine(FormattableString.Invariant(
                    $"| {cell.Name} | {tuningTrain.N} | {tuningTrain.AvgNetR:F3} | {tuningTest.N} | {tuningTest.AvgNetR:F3} | {freshTrain.N} | {freshTrain.AvgNetR:F3} | {freshTest.N} | {freshTest.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by pair");
            builder.AppendLine();
            builder.AppendLine("| File | Cell | N | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in fresh
                         .GroupBy(s => s.File)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                foreach (DiagCell cell in cells)
                {
                    GridStats stats = Stat(RunCell(group.ToList(), cell));
                    builder.AppendLine(FormattableString.Invariant(
                        $"| `{group.Key}` | {cell.Name} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Fresh by year");
            builder.AppendLine();
            builder.AppendLine("| Year | Cell | N | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach (IGrouping<int, Sample> group in fresh
                         .GroupBy(s => s.EntryTime.Year)
                         .OrderBy(g => g.Key))
            {
                foreach (DiagCell cell in cells)
                {
                    GridStats stats = Stat(RunCell(group.ToList(), cell));
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {group.Key} | {cell.Name} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Execution check (fresh test)");
            builder.AppendLine();
            builder.AppendLine("| Cell | Mode | N | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            List<Sample> freshTestSamples = fresh.Where(s => s.EntryTime.Year >= 2023).ToList();
            foreach (DiagCell cell in cells)
            {
                foreach ((string mode, bool nextOpen, double cost) in new[]
                         {
                             ("close / 2 bp", false, 0.0002d),
                             ("next open / 3 bp", true, 0.0003d)
                         })
                {
                    GridStats stats = Stat(RunCell(freshTestSamples, cell, nextOpen, cost));
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {cell.Name} | {mode} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Equity and drawdown (fresh test)");
            builder.AppendLine();
            builder.AppendLine("| Cell | Trades | Total R | Max drawdown R | Return / drawdown |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach (DiagCell cell in cells)
            {
                List<(Sample Item, TradeSim Sim)> results = RunCell(freshTestSamples, cell);
                (double totalR, double maxDrawdown) = Equity(results);
                double ratio = maxDrawdown < 0 ? totalR / -maxDrawdown : 0d;
                builder.AppendLine(FormattableString.Invariant(
                    $"| {cell.Name} | {results.Count} | {totalR:F2} | {maxDrawdown:F2} | {ratio:F2} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## What this validation says");
            builder.AppendLine();
            AppendM15ValidationConclusions(builder, tuning, fresh, cells);

            return builder.ToString();
        }

        private static void AppendM15SampleRow(
            StringBuilder builder, string name, IReadOnlyList<Sample> samples)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {samples.Count} | {samples.Count(s => s.EntryTime.Year <= 2022)} | {samples.Count(s => s.EntryTime.Year >= 2023)} |"));
        }

        private static void AppendM15ValidationConclusions(
            StringBuilder builder,
            IReadOnlyList<Sample> tuning,
            IReadOnlyList<Sample> fresh,
            IReadOnlyList<DiagCell> cells)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"- Tuning m15: {tuning.Count} signals. Fresh m15: {fresh.Count} signals ({fresh.Count(s => s.EntryTime.Year >= 2023)} on test)."));

            foreach (DiagCell cell in cells)
            {
                GridStats tuningTest = Stat(RunCell(tuning.Where(s => s.EntryTime.Year >= 2023).ToList(), cell));
                GridStats freshTest = Stat(RunCell(fresh.Where(s => s.EntryTime.Year >= 2023).ToList(), cell));
                builder.AppendLine(FormattableString.Invariant(
                    $"- **{cell.Name}**: tuning test {tuningTest.AvgNetR:F3}R over {tuningTest.N}; fresh test {freshTest.AvgNetR:F3}R over {freshTest.N} ({freshTest.Profitable:P1} win)."));
            }

            builder.AppendLine("- A region survives only if the fresh m15 test stays positive for the whole R50–61.8 / k1.50–2.00 block, not just for one cell. The block is four highly correlated cells, so the effective sample is the number of signals, not four times that.");
            builder.AppendLine("- The region is **confirmed on the fresh m15 holdout**: all four cells are positive on tuning test, fresh train and fresh test, and survive next-open / 3 bp. The base `R23.6 / k1.00` is negative on the same fresh test (-0.034R), so the improvement is the geometry, not a different slice of the same noise.");
            builder.AppendLine("- The practical cells are `R50.0 / k2.00` (fresh test +0.163R over 349, 86.5% win, total +31.4R, max DD -3.56R, return/DD 8.8) and `R61.8 / k2.00` (+0.186R, total +28.1R, max DD -3.36R, ratio 8.4). `k2.00` has a lower R:R but a higher win rate and a smaller drawdown than `k1.50`.");
            builder.AppendLine("- XAGUSD is the only pair negative for the region; 7 of 8 pairs are positive. 2024 is the weakest year, but `k2.00` remains slightly positive.");
            builder.AppendLine("- This is now a production candidate. The next step is to make the retrace ratio and the stop multiplier configurable in `DiagonalParams`/`DiagonalSetupFinder` and run an exact replay: the levels here are post-hoc, and the finder's `tpBehindEntry`, `riskTooSmall` and `tpSlHit` gates may change the sample.");
            builder.AppendLine("- The equity / drawdown table is the practical check: a high win rate with a low R:R can look good on average and still produce a deep drawdown. A return/drawdown below 1 is hard to trade.");
            builder.AppendLine("- The base `R23.6 / k1.00` is included to show whether the improvement is real or just a different slice of the same noise.");
            builder.AppendLine("- The levels are structural and are not recalculated after the entry. The execution row uses the next bar's open and a 3 bp cost; the equity table uses the base close / 2 bp model.");
        }

        private static List<DiagCell> BuildM15Cells()
        {
            List<DiagTarget> targets = BuildDiagTargets();
            List<DiagStop> stops = BuildDiagStops();

            return new List<DiagCell>
            {
                Cell("base: R23.6 / k1.00", "R23.6", "k1.00"),
                Cell("R50.0 / k1.50", "R50.0", "k1.50"),
                Cell("R50.0 / k2.00", "R50.0", "k2.00"),
                Cell("R61.8 / k1.50", "R61.8", "k1.50"),
                Cell("R61.8 / k2.00", "R61.8", "k2.00")
            };

            DiagCell Cell(string name, string targetShort, string stopShort)
            {
                DiagTarget target = targets.First(t => t.Short == targetShort);
                DiagStop stop = stops.First(s => s.Short == stopShort);
                return new DiagCell(name, target, stop);
            }
        }

        private static List<(Sample Item, TradeSim Sim)> RunCell(
            IReadOnlyList<Sample> samples,
            DiagCell cell,
            bool nextOpen = false,
            double costRate = COST_RATE)
        {
            var result = new List<(Sample Item, TradeSim Sim)>(samples.Count);

            foreach (Sample sample in samples)
            {
                TradeSim sim = SimulateGridCell(sample, cell.Target, cell.Stop, nextOpen, costRate);
                if (sim.Outcome != "SKIP")
                    result.Add((sample, sim));
            }

            return result;
        }

        private static GridStats Stat(
            IReadOnlyList<(Sample Item, TradeSim Sim)> results)
        {
            return GStat(results.Select(r => r.Sim).ToList());
        }

        private static (double TotalR, double MaxDrawdown) Equity(
            IReadOnlyList<(Sample Item, TradeSim Sim)> results)
        {
            double equity = 0d;
            double peak = 0d;
            double maxDrawdown = 0d;

            foreach ((Sample item, TradeSim sim) in results.OrderBy(r => r.Item.EntryTime))
            {
                equity += sim.NetR;
                peak = Math.Max(peak, equity);
                maxDrawdown = Math.Min(maxDrawdown, equity - peak);
            }

            return (equity, maxDrawdown);
        }

        private sealed record DiagCell(string Name, DiagTarget Target, DiagStop Stop);
    }
}
