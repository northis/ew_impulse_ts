using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for a zigzag-confirmed diagonal end (DIAGONAL.md §9.29). The
    /// fixed-retrace confirmation of §9.28 entered inside wave 5 too often: the running
    /// extreme moved, the new extreme hit the stop, and the net result was around zero.
    /// This harness replaces the fixed retrace with a <see cref="DeviationExtremumFinder"/>
    /// pivot: the end is the last high (low) pivot before a confirmed lower high (higher
    /// low), and the entry is the close of the bar that confirms that reversal.
    /// <para>
    /// The zigzag is built once per file and scale. Three scales are tested around the
    /// auto impulse period (base, ×1.618, ×2.618). Two entries are compared: the causal
    /// confirmation bar close and the pivot bar close (an upper bound that ignores the
    /// confirmation lag). The stop/target grid is the same as §9.28. Writes
    /// <c>reports/diagonal_zigzag_end.md</c> and <c>reports/diagonal_zigzag_end_trades.csv</c>.
    /// Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalZigzagEnd"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const double ZIGZAG_COST_RATE = 0.0002d;
        private const int ZIGZAG_MIN_BARS = 10;
        private const int ZIGZAG_MIN_TRADES = 100;

        private static readonly double[] ZIGZAG_STOPS = { 0.05d, 0.10d, 0.20d, 0.30d };
        private static readonly double[] ZIGZAG_TARGETS = { 0.236d, 0.382d, 0.50d, 0.618d, 0.786d };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_zigzag_end.md")]
        [Category("Research")]
        public void DiagonalZigzagEnd_Report()
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
            var trades = new List<ZigzagTrade>();

            foreach (CorrectionRun run in runs)
            {
                foreach (CorrectionSample sample in run.Samples)
                {
                    if (!sample.Outcome.Labeled)
                        continue;

                    FileZigzags fileZigzags = zigzags[sample.Base.File];
                    foreach (KeyValuePair<int, List<ZigzagPivot>> pair in fileZigzags.PivotsByRank)
                    {
                        int rank = pair.Key;
                        int scale = fileZigzags.ScalesByRank[rank];
                        foreach (ZigzagEntryMode mode in new[]
                                 {
                                     ZigzagEntryMode.CONFIRM_CLOSE,
                                     ZigzagEntryMode.PIVOT_CLOSE
                                 })
                        {
                            foreach (double stop in ZIGZAG_STOPS)
                            {
                                foreach (double target in ZIGZAG_TARGETS)
                                {
                                    ZigzagTrade? trade = SimulateZigzag(
                                        sample, rank, scale, pair.Value,
                                        mode, stop, target);
                                    if (trade != null)
                                        trades.Add(trade);
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(trades, Is.Not.Empty, "The zigzag-end grid produced no trades.");

            List<ZigzagCell> cells = BuildCells(runs, trades);
            List<ZigzagBest> best = BuildBest(cells);

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_zigzag_end.md");
            File.WriteAllText(
                reportPath,
                BuildZigzagReport(runs, cells, best, trades, zigzags));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_zigzag_end_trades.csv");
            File.WriteAllText(csvPath, BuildZigzagCsv(trades));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            foreach (CorrectionSample sample in runs.SelectMany(r => r.Samples))
                sample.Base.Provider = null!;
        }

        #region Zigzag

        private static Dictionary<string, FileZigzags> BuildZigzags(
            IReadOnlyList<CorrectionRun> runs)
        {
            var result = new Dictionary<string, FileZigzags>();
            foreach (IGrouping<string, CorrectionSample> group in runs
                         .SelectMany(r => r.Samples)
                         .GroupBy(x => x.Base.File))
            {
                TestBarsProvider provider = group.First().Base.Provider;
                int basePeriod = Math.Max(1, AutoPeriodEstimator.EstimateImpulsePeriod(provider));
                var scaleList = new List<int>();
                var pivotsByRank = new Dictionary<int, List<ZigzagPivot>>();
                foreach (double ratio in new[] { 1d, 1.618d, 2.618d })
                {
                    int scale = Math.Max(1, (int)Math.Round(basePeriod * ratio));
                    if (scaleList.Contains(scale))
                        continue;

                    int rank = scaleList.Count;
                    scaleList.Add(scale);
                    pivotsByRank[rank] = BuildPivots(provider, scale);
                }

                result[group.Key] = new FileZigzags(
                    basePeriod, scaleList, pivotsByRank);
            }

            return result;
        }

        /// <summary>
        /// Builds the confirmed zigzag pivots. The finder fires <c>OnSetExtremum</c> when it
        /// switches direction; at that moment the previous pivot is final and the new one is
        /// still tentative. The recorded <c>ConfirmBar</c> is the bar at which the previous
        /// pivot became final. The first pivot is a high (the finder starts in the down
        /// direction from the first bar's high), so the parity gives the pivot type.
        /// </summary>
        private static List<ZigzagPivot> BuildPivots(TestBarsProvider provider, int scale)
        {
            var finder = new DeviationExtremumFinder(scale, provider);
            var pivots = new List<ZigzagPivot>();
            int currentBar = -1;
            BarPoint? previous = null;

            finder.OnSetExtremum += (_, e) =>
            {
                if (previous != null)
                {
                    pivots.Add(new ZigzagPivot(
                        previous,
                        currentBar,
                        IsHigh: pivots.Count % 2 == 0));
                }

                previous = e.EventExtremum;
            };

            for (int i = 0; i < provider.Count; i++)
            {
                currentBar = i;
                finder.OnCalculate(provider.GetOpenTime(i));
            }

            return pivots;
        }

        private static ZigzagTrade? SimulateZigzag(
            CorrectionSample sample,
            int scaleRank,
            int scale,
            IReadOnlyList<ZigzagPivot> pivots,
            ZigzagEntryMode mode,
            double stopRatio,
            double targetRatio)
        {
            Sample s = sample.Base;
            TestBarsProvider provider = s.Provider;
            int sgn = s.IsUpDiagonal ? 1 : -1;
            int end = Math.Min(provider.Count - 1, s.SignalBar + sample.Outcome.Horizon);
            double endPrice = s.V5;
            int endBar = Math.Max(0, s.P5Bar);

            foreach (ZigzagPivot pivot in pivots)
            {
                if (pivot.ConfirmBar <= s.SignalBar ||
                    pivot.Point.BarIndex <= s.SignalBar)
                {
                    continue;
                }

                if (s.IsUpDiagonal)
                {
                    if (!pivot.IsHigh)
                        continue;

                    if (pivot.Point.Value > endPrice)
                    {
                        endPrice = pivot.Point.Value;
                        endBar = pivot.Point.BarIndex;
                        continue;
                    }

                    // A lower high confirms the end at the last high.
                    return BuildTrade(
                        sample, scaleRank, scale, mode, stopRatio, targetRatio,
                        endBar, endPrice, Math.Abs(endPrice - s.V0), pivot, end);
                }

                if (pivot.IsHigh)
                    continue;

                if (pivot.Point.Value < endPrice)
                {
                    endPrice = pivot.Point.Value;
                    endBar = pivot.Point.BarIndex;
                    continue;
                }

                // A higher low confirms the end at the last low.
                return BuildTrade(
                    sample, scaleRank, scale, mode, stopRatio, targetRatio,
                    endBar, endPrice, Math.Abs(endPrice - s.V0), pivot, end);
            }

            return null;
        }

        private static ZigzagTrade? BuildTrade(
            CorrectionSample sample,
            int scaleRank,
            int scale,
            ZigzagEntryMode mode,
            double stopRatio,
            double targetRatio,
            int endBar,
            double endPrice,
            double height,
            ZigzagPivot trigger,
            int end)
        {
            Sample s = sample.Base;
            TestBarsProvider provider = s.Provider;
            int sgn = s.IsUpDiagonal ? 1 : -1;
            int tradeDir = -sgn;
            int entryBar = mode == ZigzagEntryMode.CONFIRM_CLOSE
                ? trigger.ConfirmBar
                : trigger.Point.BarIndex;

            if (height <= 1e-12 || entryBar > end - ZIGZAG_MIN_BARS)
                return null;

            double entry = provider.GetClosePrice(entryBar);
            double stop = s.IsUpDiagonal
                ? endPrice + stopRatio * height
                : endPrice - stopRatio * height;
            double target = s.IsUpDiagonal
                ? endPrice - targetRatio * height
                : endPrice + targetRatio * height;

            double risk;
            double reward;
            if (tradeDir > 0)
            {
                if (entry <= stop || entry >= target)
                    return null;
                risk = entry - stop;
                reward = target - entry;
            }
            else
            {
                if (entry >= stop || entry <= target)
                    return null;
                risk = stop - entry;
                reward = entry - target;
            }

            if (risk <= 0 || reward <= 0)
                return null;

            double exit = double.NaN;
            int exitBar = -1;
            string outcome = "TIMEOUT";
            double mfeR = 0;
            double maeR = 0;

            for (int i = entryBar + 1; i <= end; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);

                double favorable = tradeDir > 0 ? high - entry : entry - low;
                double adverse = tradeDir > 0 ? entry - low : high - entry;
                mfeR = Math.Max(mfeR, favorable / risk);
                maeR = Math.Max(maeR, adverse / risk);

                bool stopHit = tradeDir > 0 ? low <= stop : high >= stop;
                bool targetHit = tradeDir > 0 ? high >= target : low <= target;

                if (stopHit)
                {
                    outcome = "SL";
                    exit = stop;
                    exitBar = i;
                    break;
                }

                if (targetHit)
                {
                    outcome = "TP";
                    exit = target;
                    exitBar = i;
                    break;
                }
            }

            if (exitBar < 0)
            {
                exit = provider.GetClosePrice(end);
                exitBar = end;
            }

            double grossR = tradeDir * (exit - entry) / risk;
            double costR = entry * ZIGZAG_COST_RATE / risk;
            double netR = grossR - costR;
            double cap = s.IsUpDiagonal ? s.V4 + s.W3 : s.V4 - s.W3;
            bool capOvershoot = sgn * (endPrice - cap) > 0;

            return new ZigzagTrade(
                Config: sample.Config,
                Set: sample.Set,
                File: s.File,
                Symbol: s.Symbol,
                SignalTime: s.EntryTime,
                ScaleRank: scaleRank,
                Scale: scale,
                Mode: mode,
                StopRatio: stopRatio,
                TargetRatio: targetRatio,
                EndTime: provider.GetOpenTime(endBar),
                EndBar: endBar,
                EndPrice: endPrice,
                Height: height,
                EntryTime: provider.GetOpenTime(entryBar),
                EntryBar: entryBar,
                Entry: entry,
                Stop: stop,
                Target: target,
                Risk: risk,
                Reward: reward,
                PlannedRr: reward / risk,
                Outcome: outcome,
                GrossR: grossR,
                CostR: costR,
                NetR: netR,
                Resolved: outcome != "TIMEOUT",
                BarsToExit: exitBar - entryBar,
                MfeR: mfeR,
                MaeR: maeR,
                CapOvershoot: capOvershoot,
                DepthDiag: sample.Outcome.DepthDiag);
        }

        #endregion

        #region Aggregation

        private static List<ZigzagCell> BuildCells(
            IReadOnlyList<CorrectionRun> runs, IReadOnlyList<ZigzagTrade> trades)
        {
            var samplesPerRun = runs.ToDictionary(
                r => (r.Config, r.Set),
                r => r.Samples.Count(x => x.Outcome.Labeled));

            return trades
                .GroupBy(t => new ZigzagCellKey(
                    t.Config, t.Set, t.ScaleRank, t.Mode, t.StopRatio, t.TargetRatio))
                .Select(g => ZigzagCell.Create(
                    g.Key,
                    g.ToList(),
                    samplesPerRun.GetValueOrDefault((g.Key.Config, g.Key.Set))))
                .ToList();
        }

        private static List<ZigzagBest> BuildBest(IReadOnlyList<ZigzagCell> cells)
        {
            var result = new List<ZigzagBest>();
            foreach (string config in new[] { "permissive", "exact" })
            {
                List<ZigzagCell> tuning = cells
                    .Where(c => c.Config == config && c.Set == "tuning")
                    .ToList();
                var freshByKey = cells
                    .Where(c => c.Config == config && c.Set == "fresh")
                    .ToDictionary(c => c.Key, c => c);

                var candidates = new List<ZigzagBest>();
                foreach (ZigzagCell t in tuning)
                {
                    ZigzagCellKey freshKey = t.Key with { Set = "fresh" };
                    if (!freshByKey.TryGetValue(freshKey, out ZigzagCell? f))
                        continue;
                    if (t.Eligible < ZIGZAG_MIN_TRADES || f.Eligible < ZIGZAG_MIN_TRADES)
                        continue;
                    candidates.Add(new ZigzagBest(t, f));
                }

                ZigzagBest? best = candidates
                    .OrderByDescending(x => Math.Min(x.Tuning.AvgNetR, x.Fresh.AvgNetR))
                    .FirstOrDefault();
                if (best != null)
                    result.Add(best);
            }

            return result;
        }

        #endregion

        #region Report

        private static string BuildZigzagReport(
            IReadOnlyList<CorrectionRun> runs,
            IReadOnlyList<ZigzagCell> cells,
            IReadOnlyList<ZigzagBest> best,
            IReadOnlyList<ZigzagTrade> trades,
            IReadOnlyDictionary<string, FileZigzags> zigzags)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — zigzag-confirmed end, then trade the correction");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("The fixed-retrace confirmation of §9.28 entered inside wave 5 too often: the new extreme hit the stop and the net result was around zero. This harness replaces the retrace with a confirmed `DeviationExtremumFinder` pivot.");
            builder.AppendLine();
            builder.AppendLine("## Method");
            builder.AppendLine();
            builder.AppendLine("- **End.** For an up diagonal, the last high pivot before a **lower high** pivot; for a down diagonal, the last low pivot before a **higher low**. The reversal pivot is the trigger.");
            builder.AppendLine("- **Entry.** `confirm close` — the close of the bar that confirms the reversal pivot (causal). `pivot close` — the close of the pivot bar itself (upper bound, ignores the confirmation lag).");
            builder.AppendLine("- **Zigzag scales.** The auto impulse period and ×1.618 / ×2.618 of it. The finder's `DeviationExtremumFinder` uses the scale as `scale × 0.0001` of price, so a larger scale confirms later and filters more noise.");
            builder.AppendLine("- **Grid.** Stop 5/10/20/30% of the height beyond the end; target 23.6/38.2/50/61.8/78.6% of the height from the end. R is the actual entry-to-stop risk; a 2 bp round-trip cost is charged; fewer than 10 bars after the entry is skipped.");
            builder.AppendLine("- Tuning = the Phase 0/1 h1 pairs; fresh = the §9.23 h1 pairs. The zigzag is causal: it is fed bar by bar and a pivot is only used after `OnSetExtremum` has confirmed it.");
            builder.AppendLine();

            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Labelled diagonals | Zigzag scales | Reference trades (base / confirm / 10% / 50%) |");
            builder.AppendLine("|---|---|---:|---|---:|");
            foreach (CorrectionRun run in runs)
            {
                int labelled = run.Samples.Count(x => x.Outcome.Labeled);
                int reference = cells.FirstOrDefault(c =>
                    c.Config == run.Config && c.Set == run.Set &&
                    c.Key.ScaleRank == 0 &&
                    c.Key.Mode == ZigzagEntryMode.CONFIRM_CLOSE &&
                    Math.Abs(c.Key.StopRatio - 0.10d) < 1e-9 &&
                    Math.Abs(c.Key.TargetRatio - 0.50d) < 1e-9)?.Eligible ?? 0;
                builder.AppendLine(FormattableString.Invariant(
                    $"| {run.Config} | {run.Set} | {labelled} | base / ×1.618 / ×2.618 | {reference} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Reference cell");
            builder.AppendLine();
            builder.AppendLine("Base zigzag scale, confirm-close entry, stop 10% of height, target 50%. The last column is the fixed-retrace causal reference from §9.28 (confirm 10% / stop 10% / target 50%) for comparison.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | N | win | avg TP R | avg SL R | avg netR | avg R:R | timeout | median bars | fixed-retrace netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (CorrectionRun run in runs)
            {
                ZigzagCell? cell = FindCell(
                    cells, run.Config, run.Set, 0,
                    ZigzagEntryMode.CONFIRM_CLOSE, 0.10d, 0.50d);
                if (cell == null)
                    continue;

                double fixedRetrace = run.Set == "tuning"
                    ? (run.Config == "permissive" ? -0.089d : -0.074d)
                    : (run.Config == "permissive" ? -0.116d : -0.330d);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {run.Config} | {run.Set} | {cell.Eligible} | {cell.WinRate:P1} | {cell.AvgTpR:F2} | {cell.AvgSlR:F2} | {cell.AvgNetR:F3} | {cell.AvgRr:F2} | {cell.TimeoutRate:P1} | {cell.MedianBars:F0} | {fixedRetrace:F3} |"));
            }

            AppendBest(builder, best);
            AppendScaleSweep(builder, cells);
            AppendEntryModeSweep(builder, cells);
            AppendTargetSweep(builder, best, cells);
            AppendStopSweep(builder, best, cells);
            AppendBestGroups(builder, best, trades);
            AppendConclusions(builder, runs, best, trades);

            return builder.ToString();
        }

        private static void AppendBest(StringBuilder builder, IReadOnlyList<ZigzagBest> best)
        {
            builder.AppendLine("## Best holdout cells");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Selected by `min(tuning netR, fresh netR)` with at least {ZIGZAG_MIN_TRADES} trades on both sides."));
            builder.AppendLine();
            builder.AppendLine("| Config | Scale | Mode | Stop | Target | Tuning N | Tuning win | Tuning netR | Fresh N | Fresh win | Fresh netR |");
            builder.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (ZigzagBest row in best)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {row.Tuning.Config} | {ScaleLabel(row.Tuning.Key.ScaleRank)} | {(row.Tuning.Key.Mode == ZigzagEntryMode.CONFIRM_CLOSE ? "confirm" : "pivot")} | {row.Tuning.Key.StopRatio:P0} | {row.Tuning.Key.TargetRatio:P0} | {row.Tuning.Eligible} | {row.Tuning.WinRate:P1} | {row.Tuning.AvgNetR:F3} | {row.Fresh.Eligible} | {row.Fresh.WinRate:P1} | {row.Fresh.AvgNetR:F3} |"));
            }

            builder.AppendLine();
        }

        private static void AppendScaleSweep(
            StringBuilder builder,
            IReadOnlyList<ZigzagCell> cells)
        {
            builder.AppendLine("## Zigzag scale sweep");
            builder.AppendLine();
            builder.AppendLine("Confirm-close entry, stop 10%, target 50%. `base` is the auto impulse period; the other rows are ×1.618 and ×2.618. The absolute scale is per file; the rank is the comparable parameter.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Scale | N | win | avg netR | avg R:R | timeout |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");

            foreach (string config in new[] { "permissive", "exact" })
            {
                foreach (string set in new[] { "tuning", "fresh" })
                {
                    for (int rank = 0; rank < 3; rank++)
                    {
                        ZigzagCell? cell = FindCell(
                            cells, config, set, rank,
                            ZigzagEntryMode.CONFIRM_CLOSE, 0.10d, 0.50d);
                        if (cell == null)
                            continue;
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {config} | {set} | {ScaleLabel(rank)} | {cell.Eligible} | {cell.WinRate:P1} | {cell.AvgNetR:F3} | {cell.AvgRr:F2} | {cell.TimeoutRate:P1} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendEntryModeSweep(
            StringBuilder builder,
            IReadOnlyList<ZigzagCell> cells)
        {
            builder.AppendLine("## Entry mode: confirm close vs pivot close");
            builder.AppendLine();
            builder.AppendLine("Base scale, stop 10%, target 50%. `pivot close` is an upper bound: it uses the pivot bar before the zigzag has confirmed it.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Mode | N | win | avg netR | avg R:R | timeout |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");

            foreach (string config in new[] { "permissive", "exact" })
            {
                foreach (string set in new[] { "tuning", "fresh" })
                {
                    foreach (ZigzagEntryMode mode in new[]
                             {
                                 ZigzagEntryMode.CONFIRM_CLOSE,
                                 ZigzagEntryMode.PIVOT_CLOSE
                             })
                    {
                        ZigzagCell? cell = FindCell(
                            cells, config, set, 0, mode, 0.10d, 0.50d);
                        if (cell == null)
                            continue;
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {config} | {set} | {(mode == ZigzagEntryMode.CONFIRM_CLOSE ? "confirm" : "pivot")} | {cell.Eligible} | {cell.WinRate:P1} | {cell.AvgNetR:F3} | {cell.AvgRr:F2} | {cell.TimeoutRate:P1} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendTargetSweep(
            StringBuilder builder,
            IReadOnlyList<ZigzagBest> best,
            IReadOnlyList<ZigzagCell> cells)
        {
            builder.AppendLine("## Target sweep of the best cells");
            builder.AppendLine();
            builder.AppendLine("Each best cell at its own scale/mode/stop; only the target changes. This shows whether the chosen target is a plateau or a lucky cell.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Scale | Target | N | win | avg netR | avg R:R | timeout |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");

            foreach (ZigzagBest bestCell in best)
            {
                ZigzagCellKey key = bestCell.Tuning.Key;
                foreach (string set in new[] { "tuning", "fresh" })
                {
                    foreach (double target in ZIGZAG_TARGETS)
                    {
                        ZigzagCell? cell = FindCell(
                            cells, key.Config, set, key.ScaleRank, key.Mode,
                            key.StopRatio, target);
                        if (cell == null)
                            continue;
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {key.Config} | {set} | {ScaleLabel(key.ScaleRank)} | {target:P1} | {cell.Eligible} | {cell.WinRate:P1} | {cell.AvgNetR:F3} | {cell.AvgRr:F2} | {cell.TimeoutRate:P1} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendStopSweep(
            StringBuilder builder,
            IReadOnlyList<ZigzagBest> best,
            IReadOnlyList<ZigzagCell> cells)
        {
            builder.AppendLine("## Stop sweep of the best cells");
            builder.AppendLine();
            builder.AppendLine("Each best cell at its own scale/mode/target; only the stop changes.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Scale | Stop | N | win | avg netR | avg R:R | timeout |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");

            foreach (ZigzagBest bestCell in best)
            {
                ZigzagCellKey key = bestCell.Tuning.Key;
                foreach (string set in new[] { "tuning", "fresh" })
                {
                    foreach (double stop in ZIGZAG_STOPS)
                    {
                        ZigzagCell? cell = FindCell(
                            cells, key.Config, set, key.ScaleRank, key.Mode,
                            stop, key.TargetRatio);
                        if (cell == null)
                            continue;
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {key.Config} | {set} | {ScaleLabel(key.ScaleRank)} | {stop:P0} | {cell.Eligible} | {cell.WinRate:P1} | {cell.AvgNetR:F3} | {cell.AvgRr:F2} | {cell.TimeoutRate:P1} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendBestGroups(
            StringBuilder builder,
            IReadOnlyList<ZigzagBest> best,
            IReadOnlyList<ZigzagTrade> trades)
        {
            builder.AppendLine("## Best cells on fresh, by group");
            builder.AppendLine();

            var dimensions = new (string Name, Func<ZigzagTrade, string> Key)[]
            {
                ("pair", t => t.Symbol),
                ("year", t => t.SignalTime.Year.ToString(CultureInfo.InvariantCulture)),
                ("session", t => SessionOf(t.SignalTime)),
                ("cap overshoot", t => t.CapOvershoot ? "yes" : "no")
            };

            foreach (ZigzagBest bestCell in best)
            {
                ZigzagCellKey key = bestCell.Tuning.Key;
                List<ZigzagTrade> fresh = trades
                    .Where(t => t.Config == key.Config && t.Set == "fresh" &&
                                t.ScaleRank == key.ScaleRank && t.Mode == key.Mode &&
                                Math.Abs(t.StopRatio - key.StopRatio) < 1e-9 &&
                                Math.Abs(t.TargetRatio - key.TargetRatio) < 1e-9)
                    .ToList();
                if (fresh.Count == 0)
                    continue;

                builder.AppendLine($"### {key.Config}");
                builder.AppendLine();
                builder.AppendLine(FormattableString.Invariant(
                    $"Scale {ScaleLabel(key.ScaleRank)}, {(key.Mode == ZigzagEntryMode.CONFIRM_CLOSE ? "confirm" : "pivot")}, stop {key.StopRatio:P0}, target {key.TargetRatio:P0}."));
                builder.AppendLine();
                builder.AppendLine("| Group | Value | N | win | avg netR |");
                builder.AppendLine("|---|---|---:|---:|---:|");

                foreach ((string name, Func<ZigzagTrade, string> keySelector) in dimensions)
                {
                    foreach (IGrouping<string, ZigzagTrade> group in fresh
                                 .GroupBy(keySelector)
                                 .OrderBy(g => g.Key, StringComparer.Ordinal))
                    {
                        List<ZigzagTrade> subset = group.ToList();
                        double winRate = subset.Count(t => t.Outcome == "TP") /
                                         (double)subset.Count;
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {name} | {group.Key} | {subset.Count} | {winRate:P1} | {subset.Average(t => t.NetR):F3} |"));
                    }
                }

                builder.AppendLine();
            }
        }

        private static void AppendConclusions(
            StringBuilder builder,
            IReadOnlyList<CorrectionRun> runs,
            IReadOnlyList<ZigzagBest> best,
            IReadOnlyList<ZigzagTrade> trades)
        {
            builder.AppendLine("## What it says");
            builder.AppendLine();

            ZigzagBest? permissive = best.FirstOrDefault(b => b.Tuning.Config == "permissive");
            if (permissive != null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- **Best zigzag cell.** Scale {ScaleLabel(permissive.Tuning.Key.ScaleRank)}, {(permissive.Tuning.Key.Mode == ZigzagEntryMode.CONFIRM_CLOSE ? "confirm" : "pivot")}, stop {permissive.Tuning.Key.StopRatio:P0}, target {permissive.Tuning.Key.TargetRatio:P0}: tuning {permissive.Tuning.Eligible} trades, {permissive.Tuning.WinRate:P1} win, {permissive.Tuning.AvgNetR:F3}R; fresh {permissive.Fresh.Eligible}, {permissive.Fresh.WinRate:P1}, {permissive.Fresh.AvgNetR:F3}R."));
            }
            else
            {
                builder.AppendLine("- **No zigzag cell passed the 100-trade holdout bar.** The detector is too rare or too weak at this sample size.");
            }

            ZigzagBest? pivot = best.FirstOrDefault(b =>
                b.Tuning.Config == "permissive" &&
                b.Tuning.Key.Mode == ZigzagEntryMode.PIVOT_CLOSE);
            if (pivot != null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- **Confirmation cost.** The best pivot-close cell (scale {ScaleLabel(pivot.Tuning.Key.ScaleRank)}, stop {pivot.Tuning.Key.StopRatio:P0}, target {pivot.Tuning.Key.TargetRatio:P0}) is {pivot.Tuning.AvgNetR:F3}R tuning / {pivot.Fresh.AvgNetR:F3}R fresh. Compare it with the confirm-close best above: the difference is the price of waiting for the zigzag to confirm the reversal."));
            }

            double? minNetR = permissive == null
                ? null
                : Math.Min(permissive.Tuning.AvgNetR, permissive.Fresh.AvgNetR);
            if (minNetR.HasValue)
            {
                builder.AppendLine(minNetR.Value > 0
                    ? "- **Verdict.** The zigzag-confirmed post-end entry is positive on both tuning and fresh. This is the first diagonal result that does not depend on the original stop surviving; it still needs a walk-forward, a live implementation of the pivot confirmation, and spread/slippage modelling before it can be a robot rule."
                    : "- **Verdict.** The zigzag-confirmed entry is not positive on both tuning and fresh. It is a better detector than the fixed retrace conceptually, but the confirmation lag and the stop beyond the end still eat the edge. The next step is a different stop/target basis (structural/ATR stop, partial TP) rather than another end detector.");
            }

            // Signal-entry baseline on the same sample.
            CorrectionRun tuning = runs.First(r =>
                r.Config == "permissive" && r.Set == "tuning");
            List<CorrectionSample> resolved = tuning.Samples
                .Where(x => x.Outcome.Labeled && x.Base.Resolved)
                .ToList();
            if (resolved.Count > 0)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- **Signal entry on the same sample.** All resolved signals: {resolved.Average(x => x.Base.NetR):F3}R. The post-end trade uses a different risk (entry-to-stop beyond the end), so compare the sign and the fresh stability, not only the average."));
            }

            builder.AppendLine("- **Caveats.** The zigzag is causal (fed bar by bar, pivots used only after confirmation), but the sample is the finder's signals with enough forward bars. The archive is bid-only, has no spread, and the cost is a flat 2 bp. The fresh set is a pair holdout, not a new time period. The grid is wide, so only the tuning+fresh agreement is evidence.");
            builder.AppendLine();
        }

        private static string ScaleLabel(int rank) => rank switch
        {
            0 => "base",
            1 => "×1.618",
            2 => "×2.618",
            _ => $"rank {rank}"
        };

        private static ZigzagCell? FindCell(
            IReadOnlyList<ZigzagCell> cells,
            string config,
            string set,
            int scaleRank,
            ZigzagEntryMode mode,
            double stop,
            double target)
        {
            return cells.FirstOrDefault(c =>
                c.Config == config && c.Set == set && c.Key.ScaleRank == scaleRank &&
                c.Key.Mode == mode &&
                Math.Abs(c.Key.StopRatio - stop) < 1e-9 &&
                Math.Abs(c.Key.TargetRatio - target) < 1e-9);
        }

        #endregion

        #region CSV

        private static string BuildZigzagCsv(IReadOnlyList<ZigzagTrade> trades)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "config,set,file,symbol,signal_time,scale,mode,stop,target,end_time,end_bar," +
                "end_price,height,entry_time,entry_bar,entry,stop_price,target_price,risk,reward," +
                "planned_rr,outcome,gross_r,cost_r,net_r,resolved,bars_to_exit,mfe_r,mae_r," +
                "cap_overshoot,depth_diag");

            foreach (ZigzagTrade t in trades
                         .Where(t => t.Mode == ZigzagEntryMode.CONFIRM_CLOSE &&
                                     Math.Abs(t.StopRatio - 0.10d) < 1e-9 &&
                                     Math.Abs(t.TargetRatio - 0.50d) < 1e-9)
                         .OrderBy(t => t.Config, StringComparer.Ordinal)
                         .ThenBy(t => t.Set, StringComparer.Ordinal)
                         .ThenBy(t => t.Scale)
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

        #endregion

        #region Types

        private enum ZigzagEntryMode
        {
            CONFIRM_CLOSE,
            PIVOT_CLOSE
        }

        private sealed record ZigzagPivot(BarPoint Point, int ConfirmBar, bool IsHigh);

        private sealed record FileZigzags(
            int BasePeriod,
            List<int> ScalesByRank,
            Dictionary<int, List<ZigzagPivot>> PivotsByRank);

        private readonly record struct ZigzagCellKey(
            string Config,
            string Set,
            int ScaleRank,
            ZigzagEntryMode Mode,
            double StopRatio,
            double TargetRatio);

        private sealed record ZigzagTrade(
            string Config,
            string Set,
            string File,
            string Symbol,
            DateTime SignalTime,
            int ScaleRank,
            int Scale,
            ZigzagEntryMode Mode,
            double StopRatio,
            double TargetRatio,
            DateTime EndTime,
            int EndBar,
            double EndPrice,
            double Height,
            DateTime EntryTime,
            int EntryBar,
            double Entry,
            double Stop,
            double Target,
            double Risk,
            double Reward,
            double PlannedRr,
            string Outcome,
            double GrossR,
            double CostR,
            double NetR,
            bool Resolved,
            int BarsToExit,
            double MfeR,
            double MaeR,
            bool CapOvershoot,
            double DepthDiag);

        private sealed record ZigzagCell(
            ZigzagCellKey Key,
            int Eligible,
            int Tp,
            int Sl,
            int Timeout,
            int Skipped,
            double WinRate,
            double AvgNetR,
            double AvgRr,
            double AvgTpR,
            double AvgSlR,
            double TimeoutRate,
            double MedianBars,
            double MedianMfeR,
            double MedianMaeR)
        {
            public string Config => Key.Config;
            public string Set => Key.Set;

            public static ZigzagCell Create(
                ZigzagCellKey key,
                IReadOnlyList<ZigzagTrade> trades,
                int sampleCount)
            {
                int tp = trades.Count(t => t.Outcome == "TP");
                int sl = trades.Count(t => t.Outcome == "SL");
                int timeout = trades.Count(t => t.Outcome == "TIMEOUT");
                int eligible = trades.Count;
                List<ZigzagTrade> tpTrades = trades
                    .Where(t => t.Outcome == "TP")
                    .ToList();
                List<ZigzagTrade> slTrades = trades
                    .Where(t => t.Outcome == "SL")
                    .ToList();

                return new ZigzagCell(
                    key,
                    eligible,
                    tp,
                    sl,
                    timeout,
                    Math.Max(0, sampleCount - eligible),
                    eligible == 0 ? 0 : tp / (double)eligible,
                    eligible == 0 ? 0 : trades.Average(t => t.NetR),
                    eligible == 0 ? 0 : trades.Average(t => t.PlannedRr),
                    tpTrades.Count == 0 ? 0 : tpTrades.Average(t => t.NetR),
                    slTrades.Count == 0 ? 0 : slTrades.Average(t => t.NetR),
                    eligible == 0 ? 0 : timeout / (double)eligible,
                    eligible == 0 ? 0 : Quantile(trades.Select(t => (double)t.BarsToExit).ToList(), 0.5),
                    eligible == 0 ? 0 : Quantile(trades.Select(t => t.MfeR).ToList(), 0.5),
                    eligible == 0 ? 0 : Quantile(trades.Select(t => t.MaeR).ToList(), 0.5));
            }
        }

        private sealed record ZigzagBest(ZigzagCell Tuning, ZigzagCell Fresh);

        #endregion
    }
}
