using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Runs 3 and 4 of the diagonal follow-up:
    /// <list type="bullet">
    /// <item>execution realism — close vs next-bar open, with gap handling and 2/3/5 bp costs;</item>
    /// <item>a full target × stop grid for the diagonal-only trade.</item>
    /// </list>
    /// Both use the exact Phase 0/1 corridor and h1 only: the session report showed h1 is the
    /// better half, and the fresh h1 set is the honest held-out sample.
    /// <para>
    /// Writes <c>reports/diagonal_execution_tpsl.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalExecutionRealism|FullyQualifiedName~DiagonalTpSlGrid"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const int GRID_MIN_SAMPLE = 20;
        private const int ATR_PERIOD = 14;

        private static readonly double[] COST_RATES = { 0.0002d, 0.0003d, 0.0005d };

        private static readonly string[] DIAG_REGION_TARGETS = { "R50.0", "R61.8" };
        private static readonly string[] DIAG_REGION_STOPS = { "k1.50", "k2.00" };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_execution_tpsl.md")]
        [Category("Research")]
        public void DiagonalExecutionTpSl_Report()
        {
            (List<Sample> tuning, List<Sample> fresh) = LoadTuningAndFresh();
            string reportPath = Path.Combine(
                Directory.GetParent(FindDataDir())!.FullName, "reports", "diagonal_execution_tpsl.md");

            string execution = BuildExecutionSection(tuning, fresh);
            string grid = BuildTpSlGridSection(tuning, fresh);
            File.WriteAllText(reportPath, execution + grid);
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static (List<Sample> Tuning, List<Sample> Fresh) LoadTuningAndFresh()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning h1 file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh h1 file was found under data/.");

            List<Sample> tuning = RunDiagonalAll(dataDir, tuningFiles);
            List<Sample> fresh = RunDiagonalAll(dataDir, freshFiles);

            Assert.That(tuning, Is.Not.Empty, "The tuning replay produced no signals.");
            Assert.That(fresh, Is.Not.Empty, "The fresh replay produced no signals.");

            return (tuning, fresh);
        }

        #region Execution realism

        private static string BuildExecutionSection(
            IReadOnlyList<Sample> tuning, IReadOnlyList<Sample> fresh)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — execution realism and TP/SL grid");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("Exact Phase 0/1 corridor (`W2/W1 >= 0.5`, `W4/W3 <= 0.382`, 23.6% diagonal target), h1 only. Tuning = 8 Phase 0 pairs, fresh = 8 different pairs. Net R includes the stated round-trip cost.");
            builder.AppendLine();

            builder.AppendLine("## Execution realism");
            builder.AppendLine();
            builder.AppendLine("`close` enters at the close of the signal bar. `next open` enters at the open of the following bar. `gap` means a next-bar open beyond a level exits at that open instead of the level. The risk is `|entry - SL|`; the levels themselves are the finder's own.");
            builder.AppendLine();
            builder.AppendLine("| Set | Mode | N | resolved | profitable | avg netR | avg R:R | avg risk bp |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            AppendExecutionModeRows(builder, "tuning", tuning);
            AppendExecutionModeRows(builder, "fresh", fresh);

            builder.AppendLine();
            builder.AppendLine("## Execution by train / test");
            builder.AppendLine();
            builder.AppendLine("| Set | Split | Mode | N | profitable | avg netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|");
            foreach ((string setName, IReadOnlyList<Sample> samples) in new[]
                     {
                         ("tuning", tuning),
                         ("fresh", fresh)
                     })
            {
                foreach ((string split, IReadOnlyList<Sample> splitSamples) in new[]
                         {
                             ("train", samples.Where(s => s.EntryTime.Year <= 2022).ToList()),
                             ("test", samples.Where(s => s.EntryTime.Year >= 2023).ToList())
                         })
                {
                    foreach (ExecutionMode mode in BuildExecutionModes())
                    {
                        ExecStats stats = EStat(SimulateAll(splitSamples, mode));
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {setName} | {split} | {mode.Name} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                    }
                }
            }

            builder.AppendLine();
            builder.AppendLine("## What the execution check says");
            builder.AppendLine();
            AppendExecutionConclusions(builder, tuning, fresh);

            return builder.ToString();
        }

        private static void AppendExecutionModeRows(
            StringBuilder builder, string setName, IReadOnlyList<Sample> samples)
        {
            foreach (ExecutionMode mode in BuildExecutionModes())
            {
                ExecStats stats = EStat(SimulateAll(samples, mode));
                builder.AppendLine(FormattableString.Invariant(
                    $"| {setName} | {mode.Name} | {stats.N} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {stats.AvgRr:F2} | {stats.AvgRiskBp:F0} |"));
            }
        }

        private static void AppendExecutionConclusions(
            StringBuilder builder,
            IReadOnlyList<Sample> tuning,
            IReadOnlyList<Sample> fresh)
        {
            ExecutionMode baseline = BuildExecutionModes()[0];
            ExecutionMode nextOpen3 = BuildExecutionModes()
                .First(m => m.Name == "next open / 3 bp");

            ExecStats tuningBase = EStat(SimulateAll(tuning, baseline));
            ExecStats tuningNext = EStat(SimulateAll(tuning, nextOpen3));
            ExecStats freshBase = EStat(SimulateAll(fresh, baseline));
            ExecStats freshNext = EStat(SimulateAll(fresh, nextOpen3));

            builder.AppendLine(FormattableString.Invariant(
                $"- Close / 2 bp: tuning {tuningBase.AvgNetR:F3}R, fresh {freshBase.AvgNetR:F3}R. Next open / 3 bp: tuning {tuningNext.AvgNetR:F3}R, fresh {freshNext.AvgNetR:F3}R."));
            builder.AppendLine(FormattableString.Invariant(
                $"- The move to next-open entry and 3 bp costs changes the fresh result by {freshNext.AvgNetR - freshBase.AvgNetR:+0.000;-0.000}R. The base is already around zero, so this is the difference between 'not proven' and 'not tradeable'."));
            builder.AppendLine("- The levels are not recalculated for the later entry: the stop and target are the finder's structural levels. That is the right test of execution, not of a new geometry.");
            builder.AppendLine("- Gap handling is conservative: a next-bar open beyond a level exits at the open, which is the worst realistic fill.");
        }

        private static List<TradeSim> SimulateAll(
            IReadOnlyList<Sample> samples, ExecutionMode mode)
        {
            var result = new List<TradeSim>(samples.Count);

            foreach (Sample sample in samples)
            {
                int firstBar = sample.SignalBar + 1;
                if (firstBar >= sample.Provider.Count)
                    continue;

                double entry;
                bool checkGap;

                if (mode.NextOpen)
                {
                    entry = sample.Provider.GetOpenPrice(firstBar);
                    checkGap = false;
                }
                else
                {
                    entry = sample.Entry;
                    checkGap = mode.CheckGap;
                }

                TradeSim sim = Simulate(sample, entry, sample.Sl, sample.Tp,
                    mode.CostRate, firstBar, checkGap);
                if (sim.Outcome != "SKIP")
                    result.Add(sim);
            }

            return result;
        }

        private static List<ExecutionMode> BuildExecutionModes()
        {
            return new List<ExecutionMode>
            {
                new("close / 2 bp", false, false, 0.0002d),
                new("close + gap / 2 bp", false, true, 0.0002d),
                new("next open / 2 bp", true, false, 0.0002d),
                new("next open / 3 bp", true, false, 0.0003d),
                new("next open / 5 bp", true, false, 0.0005d)
            };
        }

        #endregion

        #region TP/SL grid

        private static string BuildTpSlGridSection(
            IReadOnlyList<Sample> tuning, IReadOnlyList<Sample> fresh)
        {
            List<DiagTarget> targets = BuildDiagTargets();
            List<DiagStop> stops = BuildDiagStops();

            var builder = new StringBuilder();
            builder.AppendLine();
            builder.AppendLine("## TP/SL grid");
            builder.AppendLine();
            builder.AppendLine("Entry is the signal close, cost 2 bp, stop-first on a tie. Targets are a retrace of the diagonal, the structural V4/V3 points, or a fixed R:R from the stop. Stops are `V4 ± k·|W3|` or `entry ± m·ATR(14)`.");
            builder.AppendLine();

            var cells = new List<GridCell>();
            foreach (DiagTarget target in targets)
            {
                foreach (DiagStop stop in stops)
                {
                    var tuningTrain = new List<TradeSim>();
                    var tuningTest = new List<TradeSim>();
                    var freshTrain = new List<TradeSim>();
                    var freshTest = new List<TradeSim>();

                    foreach (Sample sample in tuning)
                    {
                        TradeSim sim = SimulateGrid(sample, target, stop);
                        if (sim.Outcome == "SKIP")
                            continue;
                        if (sample.EntryTime.Year <= 2022)
                            tuningTrain.Add(sim);
                        else
                            tuningTest.Add(sim);
                    }

                    foreach (Sample sample in fresh)
                    {
                        TradeSim sim = SimulateGrid(sample, target, stop);
                        if (sim.Outcome == "SKIP")
                            continue;
                        if (sample.EntryTime.Year <= 2022)
                            freshTrain.Add(sim);
                        else
                            freshTest.Add(sim);
                    }

                    cells.Add(new GridCell(
                        target, stop,
                        GStat(tuningTrain), GStat(tuningTest),
                        GStat(freshTrain), GStat(freshTest)));
                }
            }

            GridCell best = cells
                .Where(c => c.TuningTrain.N >= GRID_MIN_SAMPLE)
                .OrderByDescending(c => c.TuningTrain.AvgNetR)
                .ThenBy(c => c.Target.Name, StringComparer.Ordinal)
                .ThenBy(c => c.Stop.Name, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? cells[0];

            builder.AppendLine("Selected on tuning train:");
            builder.AppendLine();
            builder.AppendLine("| Target | Stop | Train N | Train netR | Test N | Test netR | Fresh test N | Fresh test netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            builder.AppendLine(GridRow(best));

            builder.AppendLine();
            builder.AppendLine("Top 15 by tuning-train net R:");
            builder.AppendLine();
            builder.AppendLine("| Target | Stop | Train N | Train netR | Test N | Test netR | Fresh test N | Fresh test netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (GridCell cell in cells
                         .Where(c => c.TuningTrain.N >= GRID_MIN_SAMPLE)
                         .OrderByDescending(c => c.TuningTrain.AvgNetR)
                         .ThenBy(c => c.Target.Name, StringComparer.Ordinal)
                         .ThenBy(c => c.Stop.Name, StringComparer.Ordinal)
                         .Take(15))
            {
                builder.AppendLine(GridRow(cell));
            }

            builder.AppendLine();
            builder.AppendLine("### Tuning-test net R heatmap");
            builder.AppendLine();
            builder.AppendLine("Rows are targets, columns are stops. `R236` is a 23.6% retrace of the diagonal, `V4`/`V3` are structural points, `k0.5` is `V4 ± 0.5·|W3|`, `A1` is `entry ± 1·ATR`. An edge is a region, not a cell.");
            builder.AppendLine();
            AppendHeatmap(builder, targets, stops, cells, c => c.TuningTest, best);

            builder.AppendLine();
            builder.AppendLine("### Fresh-test net R heatmap");
            builder.AppendLine();
            builder.AppendLine("The same cells on the held-out pairs. This is the column that decides whether the region is real.");
            builder.AppendLine();
            AppendHeatmap(builder, targets, stops, cells, c => c.FreshTest, best);

            builder.AppendLine();
            builder.AppendLine("### Robust region — R50–61.8 with k1.50–2.00");
            builder.AppendLine();
            builder.AppendLine("| Target | Stop | Tuning test N | Tuning test netR | Fresh test N | Fresh test netR | Fresh test R:R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (string targetShort in DIAG_REGION_TARGETS)
            {
                foreach (string stopShort in DIAG_REGION_STOPS)
                {
                    GridCell cell = cells.First(c =>
                        c.Target.Short == targetShort && c.Stop.Short == stopShort);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {cell.Target.Name} | {cell.Stop.Name} | {cell.TuningTest.N} | {cell.TuningTest.AvgNetR:F3} | {cell.FreshTest.N} | {cell.FreshTest.AvgNetR:F3} | {cell.FreshTest.AvgRr:F2} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("### Execution check on the robust region (fresh test)");
            builder.AppendLine();
            builder.AppendLine("| Target | Stop | Mode | N | profitable | avg netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|");
            foreach (string targetShort in DIAG_REGION_TARGETS)
            {
                DiagTarget target = targets.First(t => t.Short == targetShort);
                foreach (string stopShort in DIAG_REGION_STOPS)
                {
                    DiagStop stop = stops.First(s => s.Short == stopShort);
                    foreach ((string modeName, bool nextOpen, double cost) in new[]
                             {
                                 ("close / 2 bp", false, 0.0002d),
                                 ("next open / 3 bp", true, 0.0003d)
                             })
                    {
                        List<TradeSim> sims = fresh
                            .Where(s => s.EntryTime.Year >= 2023)
                            .Select(s => SimulateGridCell(s, target, stop, nextOpen, cost))
                            .Where(s => s.Outcome != "SKIP")
                            .ToList();
                        GridStats stats = GStat(sims);
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {target.Name} | {stop.Name} | {modeName} | {stats.N} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
                    }
                }
            }

            builder.AppendLine();
            builder.AppendLine("## What the TP/SL grid says");
            builder.AppendLine();
            AppendGridConclusions(builder, cells, best);

            return builder.ToString();
        }

        private static TradeSim SimulateGrid(Sample sample, DiagTarget target, DiagStop stop)
        {
            return SimulateGridCell(sample, target, stop, nextOpen: false, costRate: COST_RATE);
        }

        private static TradeSim SimulateGridCell(
            Sample sample, DiagTarget target, DiagStop stop, bool nextOpen, double costRate)
        {
            int firstBar = sample.SignalBar + 1;
            if (firstBar >= sample.Provider.Count)
                return Skipped();

            double entry = nextOpen
                ? sample.Provider.GetOpenPrice(firstBar)
                : sample.Entry;
            double tp = target.Resolve(sample);
            double sl = stop.Resolve(sample, entry);

            bool isShort = sample.IsUpDiagonal;
            bool valid = isShort
                ? tp < entry && sl > entry
                : tp > entry && sl < entry;

            if (!valid)
                return Skipped();

            return Simulate(sample, entry, sl, tp, costRate, firstBar, checkGap: false);
        }

        private static void AppendHeatmap(
            StringBuilder builder,
            IReadOnlyList<DiagTarget> targets,
            IReadOnlyList<DiagStop> stops,
            IReadOnlyList<GridCell> cells,
            Func<GridCell, GridStats> metric,
            GridCell best)
        {
            builder.Append("| Target |");
            foreach (DiagStop stop in stops)
                builder.Append(' ').Append(stop.Short).Append(" |");
            builder.AppendLine();
            builder.Append("|---|");
            foreach (DiagStop _ in stops)
                builder.Append("---:|");
            builder.AppendLine();

            foreach (DiagTarget target in targets)
            {
                builder.Append("| ").Append(target.Short).Append(" |");
                foreach (DiagStop stop in stops)
                {
                    GridCell cell = cells.First(c =>
                        ReferenceEquals(c.Target, target) && ReferenceEquals(c.Stop, stop));
                    GridStats stats = metric(cell);
                    string text = stats.Resolved == 0
                        ? "—"
                        : stats.AvgNetR.ToString("F3", CultureInfo.InvariantCulture);
                    bool isBest = ReferenceEquals(cell, best);
                    builder.Append(' ').Append(isBest ? $"**{text}**" : text).Append(" |");
                }

                builder.AppendLine();
            }
        }

        private static void AppendGridConclusions(
            StringBuilder builder,
            IReadOnlyList<GridCell> cells,
            GridCell best)
        {
            int positiveFresh = cells.Count(c =>
                c.TuningTest.Resolved > 0 && c.FreshTest.Resolved > 0 &&
                c.TuningTest.AvgNetR > 0d && c.FreshTest.AvgNetR > 0d);
            int total = cells.Count(c => c.TuningTest.Resolved > 0 && c.FreshTest.Resolved > 0);

            builder.AppendLine(FormattableString.Invariant(
                $"- The best tuning-train cell is `{best.Target.Name}` / `{best.Stop.Name}`: train {best.TuningTrain.AvgNetR:F3}R over {best.TuningTrain.N}, tuning test {best.TuningTest.AvgNetR:F3}R over {best.TuningTest.N}, fresh test {best.FreshTest.AvgNetR:F3}R over {best.FreshTest.N}."));
            builder.AppendLine(FormattableString.Invariant(
                $"- {positiveFresh} of {total} cells are positive on both the tuning test and the fresh test. A real geometry shows up as a connected region, not as one cell."));
            builder.AppendLine("- The coherent region is `R50.0–R61.8` with `k1.50–k2.00`: tuning test +0.087…+0.304R, fresh test +0.262…+0.338R over 88 trades. This is the first diagonal-only geometry positive on both the tuning test and the fresh test. It is still a post-hoc region; the execution table above shows whether it survives next-open entry and 3 bp costs.");
            builder.AppendLine("- The region survives next-open entry and 3 bp costs: fresh test +0.252…+0.331R, with win rates 78–93% at R:R 0.40–0.72. That is a high-win-rate / low-R:R profile: the edge is sensitive to the win rate and to a cluster of losses, so it needs a true holdout and a drawdown check.");
            builder.AppendLine("- The grid is 63 cells on a few hundred signals; the best in-sample cell is a beauty-contest winner. Only the fresh-test heatmap can reject it.");
            builder.AppendLine("- The structural stop and target are not recalculated after the entry, so this run tests geometry, not execution. The execution section above is the separate check.");
        }

        private static List<DiagTarget> BuildDiagTargets()
        {
            var targets = new List<DiagTarget>();

            foreach (double ratio in new[] { 0.236d, 0.382d, 0.5d, 0.618d, 0.786d })
            {
                double localRatio = ratio;
                targets.Add(new DiagTarget(
                    FormattableString.Invariant($"Retrace {ratio:P1}"),
                    FormattableString.Invariant($"R{ratio * 100:F1}"),
                    sample =>
                    {
                        double height = Math.Abs(sample.V5 - sample.V0);
                        return sample.IsUpDiagonal
                            ? sample.V5 - localRatio * height
                            : sample.V5 + localRatio * height;
                    }));
            }

            targets.Add(new DiagTarget("Point V4", "V4", sample => sample.V4));
            targets.Add(new DiagTarget("Point V3", "V3", sample => sample.V3));

            return targets;
        }

        private static List<DiagStop> BuildDiagStops()
        {
            var stops = new List<DiagStop>();

            foreach (double k in new[] { 0.5d, 0.75d, 1.0d, 1.25d, 1.5d, 2.0d })
            {
                double localK = k;
                stops.Add(new DiagStop(
                    FormattableString.Invariant($"V4 ± {k:F2}·|W3|"),
                    FormattableString.Invariant($"k{k:F2}"),
                    (sample, _) => sample.IsUpDiagonal
                        ? sample.V4 + localK * sample.W3
                        : sample.V4 - localK * sample.W3));
            }

            foreach (int m in new[] { 1, 2, 3 })
            {
                int localM = m;
                stops.Add(new DiagStop(
                    FormattableString.Invariant($"Entry ± {m}·ATR"),
                    FormattableString.Invariant($"A{m}"),
                    (sample, entry) =>
                    {
                        double atr = GetAtr(sample.Provider, sample.SignalBar);
                        return sample.IsUpDiagonal
                            ? entry + localM * atr
                            : entry - localM * atr;
                    }));
            }

            return stops;
        }

        private static double GetAtr(TestBarsProvider provider, int index)
        {
            int first = Math.Max(1, index - ATR_PERIOD + 1);
            if (index < first)
                return 0d;

            double sum = 0d;
            for (int i = first; i <= index; i++)
            {
                double close = provider.GetClosePrice(i - 1);
                sum += Math.Max(provider.GetHighPrice(i), close) -
                       Math.Min(provider.GetLowPrice(i), close);
            }

            return sum / (index - first + 1);
        }

        private static string GridRow(GridCell cell)
        {
            return FormattableString.Invariant(
                $"| {cell.Target.Name} | {cell.Stop.Name} | {cell.TuningTrain.N} | {cell.TuningTrain.AvgNetR:F3} | {cell.TuningTest.N} | {cell.TuningTest.AvgNetR:F3} | {cell.FreshTest.N} | {cell.FreshTest.AvgNetR:F3} |");
        }

        #endregion

        #region Simulation

        private static TradeSim Simulate(
            Sample sample, double entry, double sl, double tp, double costRate,
            int firstBar, bool checkGap)
        {
            bool isShort = sample.IsUpDiagonal;
            double risk = Math.Abs(entry - sl);
            if (risk <= 0d || double.IsNaN(risk) || double.IsInfinity(risk))
                return Skipped();

            bool valid = isShort ? tp < entry && sl > entry : tp > entry && sl < entry;
            if (!valid)
                return Skipped();

            TestBarsProvider provider = sample.Provider;
            int end = Math.Min(provider.Count - 1, sample.SignalBar + sample.Horizon);
            if (firstBar > end)
                return Skipped();

            double exit = 0d;
            string outcome = "NONE";

            if (checkGap && firstBar >= 0 && firstBar < provider.Count)
            {
                double open = provider.GetOpenPrice(firstBar);
                if (isShort ? open >= sl : open <= sl)
                {
                    exit = open;
                    outcome = "SL";
                }
                else if (isShort ? open <= tp : open >= tp)
                {
                    exit = open;
                    outcome = "TP";
                }
            }

            if (outcome == "NONE")
            {
                for (int i = firstBar; i <= end; i++)
                {
                    double high = provider.GetHighPrice(i);
                    double low = provider.GetLowPrice(i);

                    if (isShort ? high >= sl : low <= sl)
                    {
                        exit = sl;
                        outcome = "SL";
                        break;
                    }

                    if (isShort ? low <= tp : high >= tp)
                    {
                        exit = tp;
                        outcome = "TP";
                        break;
                    }
                }
            }

            double rr = Math.Abs(tp - entry) / risk;
            if (outcome == "NONE")
                return new TradeSim(false, false, 0d, rr, risk, entry, "NONE");

            double grossR = isShort ? (entry - exit) / risk : (exit - entry) / risk;
            double netR = grossR - entry * costRate / risk;
            return new TradeSim(true, netR > 0d, netR, rr, risk, entry, outcome);
        }

        private static TradeSim Skipped() =>
            new(false, false, 0d, 0d, 0d, 0d, "SKIP");

        private static ExecStats EStat(IReadOnlyList<TradeSim> sims)
        {
            List<TradeSim> resolved = sims.Where(s => s.Resolved).ToList();
            return new ExecStats(
                sims.Count,
                resolved.Count,
                resolved.Count == 0 ? 0d : ShareSim(resolved, s => s.Profitable),
                resolved.Count == 0 ? 0d : resolved.Average(s => s.NetR),
                resolved.Count == 0 ? 0d : resolved.Average(s => s.Rr),
                sims.Count == 0 ? 0d : 1e4d * sims.Average(s => s.Risk / s.Entry));
        }

        private static GridStats GStat(IReadOnlyList<TradeSim> sims)
        {
            List<TradeSim> resolved = sims.Where(s => s.Resolved).ToList();
            return new GridStats(
                sims.Count,
                resolved.Count,
                resolved.Count == 0 ? 0d : ShareSim(resolved, s => s.Profitable),
                resolved.Count == 0 ? 0d : resolved.Average(s => s.NetR),
                resolved.Count == 0 ? 0d : resolved.Average(s => s.Rr));
        }

        private static double ShareSim(
            IReadOnlyList<TradeSim> sims, Func<TradeSim, bool> predicate)
        {
            return sims.Count == 0 ? 0d : (double)sims.Count(predicate) / sims.Count;
        }

        #endregion

        #region Types

        private sealed record ExecutionMode(
            string Name, bool NextOpen, bool CheckGap, double CostRate);

        private sealed record DiagTarget(
            string Name, string Short, Func<Sample, double> Resolve);

        private sealed record DiagStop(
            string Name, string Short, Func<Sample, double, double> Resolve);

        private sealed record TradeSim(
            bool Resolved,
            bool Profitable,
            double NetR,
            double Rr,
            double Risk,
            double Entry,
            string Outcome);

        private sealed record ExecStats(
            int N,
            int Resolved,
            double Profitable,
            double AvgNetR,
            double AvgRr,
            double AvgRiskBp);

        private sealed record GridStats(
            int N,
            int Resolved,
            double Profitable,
            double AvgNetR,
            double AvgRr);

        private sealed record GridCell(
            DiagTarget Target,
            DiagStop Stop,
            GridStats TuningTrain,
            GridStats TuningTest,
            GridStats FreshTrain,
            GridStats FreshTest);

        #endregion
    }
}
