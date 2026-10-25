using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for a post-diagonal entry (DIAGONAL.md §9.28). The signal-entry
    /// study (§9.27) showed the correction is usually deep, but ~75% of the original
    /// V(3)-break entries hit the theoretical stop before the diagonal ends. This harness
    /// asks the next question: if we wait for the diagonal to end and then trade the
    /// correction itself, is there a positive region?
    /// <para>
    /// Two end definitions are simulated:
    /// <list type="bullet">
    /// <item><b>ex-post next open</b> — the actual end is known and the trade enters at the
    /// open of the next bar. This is an upper bound, not a live system.</item>
    /// <item><b>causal confirmation</b> — the running extreme is tracked from the signal;
    /// the end is declared when price retraces 5/10/15/20% of the current diagonal height,
    /// and the trade enters at that bar's close. This is the live-like variant.</item>
    /// </list>
    /// The stop is placed beyond the end by a fraction of the height, the target is a
    /// retrace of the height from the end, and the grid is evaluated on the Phase 0/1 h1
    /// pairs (tuning) and the §9.23 h1 pairs (fresh). Writes
    /// <c>reports/diagonal_post_end_entry.md</c> and
    /// <c>reports/diagonal_post_end_entry_trades.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalPostEndEntry"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const double POST_END_COST_RATE = 0.0002d;
        private const int POST_END_MIN_BARS = 10;
        private const int POST_END_MIN_TRADES = 100;

        private static readonly double[] POST_END_STOPS = { 0.05d, 0.10d, 0.20d, 0.30d };
        private static readonly double[] POST_END_TARGETS = { 0.236d, 0.382d, 0.50d, 0.618d, 0.786d };
        private static readonly double[] POST_END_CONFIRMS = { 0.05d, 0.10d, 0.15d, 0.20d };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_post_end_entry.md")]
        [Category("Research")]
        public void DiagonalPostEndEntry_Report()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning archive file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh archive file was found under data/.");

            // Reuse the correction harness: it already replays the finder and computes the
            // ex-post end, depth and the original trade outcome for every signal.
            var runs = new List<CorrectionRun>();
            runs.AddRange(RunBothConfigs(dataDir, tuningFiles, "tuning"));
            runs.AddRange(RunBothConfigs(dataDir, freshFiles, "fresh"));

            var trades = new List<PostEndTrade>();
            foreach (CorrectionRun run in runs)
            {
                foreach (CorrectionSample sample in run.Samples)
                {
                    if (!sample.Outcome.Labeled)
                        continue;

                    foreach (double stop in POST_END_STOPS)
                    {
                        foreach (double target in POST_END_TARGETS)
                        {
                            PostEndTrade? trade = SimulateExPost(
                                sample, stop, target);
                            if (trade != null)
                                trades.Add(trade);
                        }
                    }

                    foreach (double confirm in POST_END_CONFIRMS)
                    {
                        foreach (double stop in POST_END_STOPS)
                        {
                            foreach (double target in POST_END_TARGETS)
                            {
                                PostEndTrade? trade = SimulateCausal(
                                    sample, confirm, stop, target);
                                if (trade != null)
                                    trades.Add(trade);
                            }
                        }
                    }
                }
            }

            Assert.That(trades, Is.Not.Empty, "The post-end grid produced no trades.");

            List<PostEndCell> cells = BuildCells(runs, trades);
            List<PostEndBest> best = BuildBestCells(cells);

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_post_end_entry.md");
            File.WriteAllText(
                reportPath,
                BuildPostEndReport(runs, cells, best, trades));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_post_end_entry_trades.csv");
            File.WriteAllText(csvPath, BuildPostEndCsv(trades));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            foreach (CorrectionSample sample in runs.SelectMany(r => r.Samples))
                sample.Base.Provider = null!;
        }

        #region Simulation

        /// <summary>
        /// Upper bound: the actual end is known, enter at the next bar open. The stop is
        /// <paramref name="stopRatio"/> of the height beyond the end, the target is
        /// <paramref name="targetRatio"/> of the height back from the end.
        /// </summary>
        private static PostEndTrade? SimulateExPost(
            CorrectionSample sample, double stopRatio, double targetRatio)
        {
            Sample s = sample.Base;
            CorrectionOutcome o = sample.Outcome;
            TestBarsProvider provider = s.Provider;
            int sgn = s.IsUpDiagonal ? 1 : -1;
            int end = Math.Min(provider.Count - 1, s.SignalBar + o.Horizon);
            int entryBar = Math.Max(o.EndBar + 1, s.SignalBar + 1);

            if (entryBar > end - POST_END_MIN_BARS)
                return null;

            double entry = provider.GetOpenPrice(entryBar);
            return BuildTrade(
                sample,
                PostEndMode.EX_POST,
                0d,
                stopRatio,
                targetRatio,
                o.EndBar,
                o.EndPrice,
                o.Height,
                entryBar,
                entry,
                entryBar,
                end,
                isEntryAtOpen: true);
        }

        /// <summary>
        /// Live-like: track the running extreme from the signal; declare the end on the first
        /// bar whose counter-move reaches <paramref name="confirmRatio"/> of the current
        /// height, and enter at that bar's close. A new extreme after the entry is a normal
        /// stop risk, not a re-entry.
        /// </summary>
        private static PostEndTrade? SimulateCausal(
            CorrectionSample sample, double confirmRatio, double stopRatio, double targetRatio)
        {
            Sample s = sample.Base;
            CorrectionOutcome o = sample.Outcome;
            TestBarsProvider provider = s.Provider;
            int sgn = s.IsUpDiagonal ? 1 : -1;
            int end = Math.Min(provider.Count - 1, s.SignalBar + o.Horizon);

            double extreme = s.V5;
            int extremeBar = Math.Max(0, s.P5Bar);

            for (int i = s.SignalBar + 1; i <= end; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                double barExtreme = s.IsUpDiagonal ? high : low;

                if (sgn * (barExtreme - extreme) > 0)
                {
                    extreme = barExtreme;
                    extremeBar = i;
                }

                double height = Math.Abs(extreme - s.V0);
                if (height <= 1e-12)
                    continue;

                double retrace = sgn * (extreme - (s.IsUpDiagonal ? low : high));
                if (retrace < confirmRatio * height)
                    continue;

                if (i > end - POST_END_MIN_BARS)
                    return null;

                double entry = provider.GetClosePrice(i);
                return BuildTrade(
                    sample,
                    PostEndMode.CAUSAL,
                    confirmRatio,
                    stopRatio,
                    targetRatio,
                    extremeBar,
                    extreme,
                    height,
                    i,
                    entry,
                    i + 1,
                    end,
                    isEntryAtOpen: false);
            }

            return null;
        }

        private static PostEndTrade? BuildTrade(
            CorrectionSample sample,
            PostEndMode mode,
            double confirmRatio,
            double stopRatio,
            double targetRatio,
            int endBar,
            double endPrice,
            double height,
            int entryBar,
            double entry,
            int firstExitBar,
            int end,
            bool isEntryAtOpen)
        {
            Sample s = sample.Base;
            TestBarsProvider provider = s.Provider;
            int sgn = s.IsUpDiagonal ? 1 : -1;
            int tradeDir = -sgn; // counter to the diagonal

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

            // An entry at the open can be stopped inside the entry bar; an entry at the
            // close cannot, so its first tradeable bar is the next one.
            int start = isEntryAtOpen ? entryBar : firstExitBar;
            if (start > end)
                return null;

            double exit = double.NaN;
            int exitBar = -1;
            string outcome = "TIMEOUT";
            double mfeR = 0;
            double maeR = 0;

            for (int i = start; i <= end; i++)
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
            double costR = entry * POST_END_COST_RATE / risk;
            double netR = grossR - costR;

            // Causal cap check: the confirmed end is compared with the theoretical cap
            // V(4) ± |W3|. Both are known at the entry, so filtering on this is free.
            double cap = s.IsUpDiagonal ? s.V4 + s.W3 : s.V4 - s.W3;
            bool capOvershoot = sgn * (endPrice - cap) > 0;

            return new PostEndTrade(
                Config: sample.Config,
                Set: sample.Set,
                File: s.File,
                Symbol: s.Symbol,
                SignalTime: s.EntryTime,
                Mode: mode,
                ConfirmRatio: confirmRatio,
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
                ExitBar: exitBar,
                BarsToExit: exitBar - entryBar,
                MfeR: mfeR,
                MaeR: maeR,
                CapOvershoot: capOvershoot,
                DepthDiag: sample.Outcome.DepthDiag,
                Resolved: outcome != "TIMEOUT");
        }

        #endregion

        #region Aggregation

        private static List<PostEndCell> BuildCells(
            IReadOnlyList<CorrectionRun> runs, IReadOnlyList<PostEndTrade> trades)
        {
            var samplesPerRun = runs.ToDictionary(
                r => (r.Config, r.Set),
                r => r.Samples.Count(x => x.Outcome.Labeled));

            var expanded = new List<(PostEndTrade Trade, PostEndFilter Filter)>(
                trades.Count * 2);
            foreach (PostEndTrade trade in trades)
            {
                expanded.Add((trade, PostEndFilter.ALL));
                if (!trade.CapOvershoot)
                    expanded.Add((trade, PostEndFilter.WITHIN_CAP));
            }

            return expanded
                .GroupBy(x => new PostEndCellKey(
                    x.Trade.Config,
                    x.Trade.Set,
                    x.Trade.Mode,
                    x.Filter,
                    x.Trade.ConfirmRatio,
                    x.Trade.StopRatio,
                    x.Trade.TargetRatio))
                .Select(g => PostEndCell.Create(
                    g.Key,
                    g.Select(x => x.Trade).ToList(),
                    samplesPerRun.GetValueOrDefault((g.Key.Config, g.Key.Set))))
                .ToList();
        }

        private static List<PostEndBest> BuildBestCells(IReadOnlyList<PostEndCell> cells)
        {
            var result = new List<PostEndBest>();
            foreach (string config in new[] { "permissive", "exact" })
            {
                List<PostEndCell> tuning = cells
                    .Where(c => c.Config == config && c.Set == "tuning")
                    .ToList();
                List<PostEndCell> fresh = cells
                    .Where(c => c.Config == config && c.Set == "fresh")
                    .ToList();

                foreach ((PostEndMode mode, PostEndFilter filter) in new[]
                         {
                             (PostEndMode.EX_POST, PostEndFilter.ALL),
                             (PostEndMode.CAUSAL, PostEndFilter.ALL),
                             (PostEndMode.CAUSAL, PostEndFilter.WITHIN_CAP)
                         })
                {
                    var freshByKey = fresh.ToDictionary(c => c.Key, c => c);
                    var candidates = new List<PostEndBest>();
                    foreach (PostEndCell t in tuning.Where(c =>
                                 c.Key.Mode == mode && c.Key.Filter == filter))
                    {
                        PostEndCellKey freshKey = t.Key with { Set = "fresh" };
                        if (!freshByKey.TryGetValue(freshKey, out PostEndCell? f))
                            continue;
                        if (t.Eligible < POST_END_MIN_TRADES ||
                            f.Eligible < POST_END_MIN_TRADES)
                        {
                            continue;
                        }

                        candidates.Add(new PostEndBest(t, f));
                    }

                    PostEndBest? best = candidates
                        .OrderByDescending(x => Math.Min(x.Tuning.AvgNetR, x.Fresh.AvgNetR))
                        .FirstOrDefault();
                    if (best != null)
                        result.Add(best);
                }
            }

            return result;
        }

        #endregion

        #region Report

        private static string BuildPostEndReport(
            IReadOnlyList<CorrectionRun> runs,
            IReadOnlyList<PostEndCell> cells,
            IReadOnlyList<PostEndBest> best,
            IReadOnlyList<PostEndTrade> trades)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — post-end entry into the correction");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("The signal-entry study (§9.27) showed the correction is usually deep, but most V(3)-break entries hit the theoretical stop before the diagonal ends. This harness enters **after** the diagonal, counter to it, and sweeps the entry mode, stop and target.");
            builder.AppendLine();
            builder.AppendLine("## Method");
            builder.AppendLine();
            builder.AppendLine("- **Ex-post next open** — the actual end is known; entry at the next bar open. This is an upper bound, not a live system.");
            builder.AppendLine("- **Causal confirmation** — the running extreme is tracked from the signal; the end is declared when price retraces 5/10/15/20% of the current diagonal height, and the entry is that bar's close. A new extreme after the entry is a normal stop risk, not a re-entry.");
            builder.AppendLine("- **Stop** — beyond the end by 5/10/20/30% of the height. **Target** — a 23.6/38.2/50/61.8/78.6% retrace of the height from the end.");
            builder.AppendLine("- R is computed from the actual entry-to-stop risk; a 2 bp round-trip cost is charged. A trade with the target behind the entry, no confirmation, or fewer than 10 bars after the entry is skipped. The average planned R:R is not the average winner: tight stops with far targets lose more often, so the reference table shows the realized average TP and SL R.");
            builder.AppendLine("- Tuning = the Phase 0/1 h1 pairs; fresh = the §9.23 h1 pairs. The ex-post variant uses the actual end and is an upper bound. The causal variant uses only the running extreme and the confirmation, so it is a real backtest of that entry rule; the sample is the finder's signals with enough forward bars.");
            builder.AppendLine();

            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Labelled diagonals | Ex-post trades (10%/50%) | Causal trades (10%/10%/50%) |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach (CorrectionRun run in runs)
            {
                int labelled = run.Samples.Count(x => x.Outcome.Labeled);
                int exPost = cells.FirstOrDefault(c =>
                    c.Config == run.Config && c.Set == run.Set &&
                    c.Key.Mode == PostEndMode.EX_POST &&
                    Math.Abs(c.Key.StopRatio - 0.10d) < 1e-9 &&
                    Math.Abs(c.Key.TargetRatio - 0.50d) < 1e-9)?.Eligible ?? 0;
                int causal = cells.FirstOrDefault(c =>
                    c.Config == run.Config && c.Set == run.Set &&
                    c.Key.Mode == PostEndMode.CAUSAL &&
                    Math.Abs(c.Key.ConfirmRatio - 0.10d) < 1e-9 &&
                    Math.Abs(c.Key.StopRatio - 0.10d) < 1e-9 &&
                    Math.Abs(c.Key.TargetRatio - 0.50d) < 1e-9)?.Eligible ?? 0;
                builder.AppendLine(FormattableString.Invariant(
                    $"| {run.Config} | {run.Set} | {labelled} | {exPost} | {causal} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Reference cells");
            builder.AppendLine();
            builder.AppendLine("Ex-post: next open, stop 10% of height, target 50%. Causal: confirm 10%, stop 10%, target 50%. The last two columns are the original signal-entry result on the same sample (finder's own TP/SL, all resolved signals; `tradeable236` is the subset that survived to the end).");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Mode | N | win | avg TP R | avg SL R | avg netR | avg planned R:R | timeout | median bars | signal avg netR | tradeable236 netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (CorrectionRun run in runs)
            {
                List<CorrectionSample> labelled = run.Samples
                    .Where(x => x.Outcome.Labeled)
                    .ToList();
                List<CorrectionSample> resolved = labelled
                    .Where(x => x.Base.Resolved)
                    .ToList();
                List<CorrectionSample> tradeable = labelled
                    .Where(x => x.Outcome.Tradeable236)
                    .ToList();
                double signalNetR = resolved.Count == 0
                    ? 0
                    : resolved.Average(x => x.Base.NetR);
                double tradeableNetR = tradeable.Count == 0
                    ? 0
                    : tradeable.Average(x => x.Base.NetR);

                foreach (PostEndCell? cell in new[]
                         {
                             FindCell(cells, run.Config, run.Set, PostEndMode.EX_POST, PostEndFilter.ALL, 0d, 0.10d, 0.50d),
                             FindCell(cells, run.Config, run.Set, PostEndMode.CAUSAL, PostEndFilter.ALL, 0.10d, 0.10d, 0.50d),
                             FindCell(cells, run.Config, run.Set, PostEndMode.CAUSAL, PostEndFilter.WITHIN_CAP, 0.10d, 0.10d, 0.50d)
                         })
                {
                    if (cell == null)
                        continue;

                    string modeName = cell.Key.Mode == PostEndMode.EX_POST
                        ? "ex-post"
                        : cell.Key.Filter == PostEndFilter.WITHIN_CAP
                            ? "causal, within cap"
                            : "causal, all";
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Config} | {run.Set} | {modeName} | {cell.Eligible} | {cell.WinRate:P1} | {cell.AvgTpR:F2} | {cell.AvgSlR:F2} | {cell.AvgNetR:F3} | {cell.AvgRr:F2} | {cell.TimeoutRate:P1} | {cell.MedianBars:F0} | {signalNetR:F3} | {tradeableNetR:F3} |"));
                }
            }

            AppendCapFilter(builder, trades);
            AppendBest(builder, best);
            AppendConfirmSweep(builder, cells);
            AppendTargetSweep(builder, cells);
            AppendStopSweep(builder, cells);
            AppendBestGroups(builder, best, trades);
            AppendConclusions(builder, runs, cells, best, trades);

            return builder.ToString();
        }

        private static void AppendCapFilter(
            StringBuilder builder, IReadOnlyList<PostEndTrade> trades)
        {
            builder.AppendLine("## The cap filter");
            builder.AppendLine();
            builder.AppendLine("The confirmed end is compared with the theoretical cap `V(4) ± |W3|` at the entry. Both the cap and the running extreme are known at the confirmation, so the filter is causal and free. The reference causal cell (confirm 10%, stop 10%, target 50%) split by the filter:");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Group | N | win | avg TP R | avg SL R | avg netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");

            List<PostEndTrade> reference = trades
                .Where(t => t.Mode == PostEndMode.CAUSAL &&
                            Math.Abs(t.ConfirmRatio - 0.10d) < 1e-9 &&
                            Math.Abs(t.StopRatio - 0.10d) < 1e-9 &&
                            Math.Abs(t.TargetRatio - 0.50d) < 1e-9)
                .ToList();

            foreach (string config in new[] { "permissive", "exact" })
            {
                foreach (string set in new[] { "tuning", "fresh" })
                {
                    foreach ((string group, Func<PostEndTrade, bool> predicate) in new[]
                             {
                                 ("within cap", (Func<PostEndTrade, bool>)(t => !t.CapOvershoot)),
                                 ("overshoot", t => t.CapOvershoot)
                             })
                    {
                        List<PostEndTrade> subset = reference
                            .Where(t => t.Config == config && t.Set == set && predicate(t))
                            .ToList();
                        if (subset.Count == 0)
                            continue;

                        List<PostEndTrade> tp = subset
                            .Where(t => t.Outcome == "TP")
                            .ToList();
                        List<PostEndTrade> sl = subset
                            .Where(t => t.Outcome == "SL")
                            .ToList();
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {config} | {set} | {group} | {subset.Count} | {tp.Count / (double)subset.Count:P1} | {(tp.Count == 0 ? 0 : tp.Average(t => t.NetR)):F2} | {(sl.Count == 0 ? 0 : sl.Average(t => t.NetR)):F2} | {subset.Average(t => t.NetR):F3} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendBest(StringBuilder builder, IReadOnlyList<PostEndBest> best)
        {
            builder.AppendLine("## Best holdout cells");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"A cell is selected by `min(tuning netR, fresh netR)` with at least {POST_END_MIN_TRADES} trades on both sides. This is a holdout criterion, not a tuning-best table."));
            builder.AppendLine();
            builder.AppendLine("| Config | Mode | Filter | Confirm | Stop | Target | Tuning N | Tuning win | Tuning netR | Fresh N | Fresh win | Fresh netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (PostEndBest row in best)
            {
                string modeName = row.Tuning.Key.Mode == PostEndMode.EX_POST
                    ? "ex-post"
                    : "causal";
                string filterName = row.Tuning.Key.Filter == PostEndFilter.WITHIN_CAP
                    ? "within cap"
                    : "all";
                builder.AppendLine(FormattableString.Invariant(
                    $"| {row.Tuning.Config} | {modeName} | {filterName} | {row.Tuning.Key.ConfirmRatio:P0} | {row.Tuning.Key.StopRatio:P0} | {row.Tuning.Key.TargetRatio:P0} | {row.Tuning.Eligible} | {row.Tuning.WinRate:P1} | {row.Tuning.AvgNetR:F3} | {row.Fresh.Eligible} | {row.Fresh.WinRate:P1} | {row.Fresh.AvgNetR:F3} |"));
            }

            builder.AppendLine();
        }

        private static void AppendConfirmSweep(
            StringBuilder builder, IReadOnlyList<PostEndCell> cells)
        {
            builder.AppendLine("## Confirmation sweep (stop 10%, target 50%)");
            builder.AppendLine();
            builder.AppendLine("How much entry delay does the causal confirmation cost?");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Confirm | N | win | avg netR | avg R:R | timeout |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");

            foreach (string config in new[] { "permissive", "exact" })
            {
                foreach (string set in new[] { "tuning", "fresh" })
                {
                    foreach (double confirm in POST_END_CONFIRMS)
                    {
                        PostEndCell? cell = FindCell(
                            cells, config, set, PostEndMode.CAUSAL, PostEndFilter.ALL,
                            confirm, 0.10d, 0.50d);
                        if (cell == null)
                            continue;
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {config} | {set} | {confirm:P0} | {cell.Eligible} | {cell.WinRate:P1} | {cell.AvgNetR:F3} | {cell.AvgRr:F2} | {cell.TimeoutRate:P1} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendTargetSweep(
            StringBuilder builder, IReadOnlyList<PostEndCell> cells)
        {
            builder.AppendLine("## Target sweep (confirm 10%, stop 10%)");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Target | N | win | avg netR | avg R:R | timeout |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");

            foreach (string config in new[] { "permissive", "exact" })
            {
                foreach (string set in new[] { "tuning", "fresh" })
                {
                    foreach (double target in POST_END_TARGETS)
                    {
                        PostEndCell? cell = FindCell(
                            cells, config, set, PostEndMode.CAUSAL, PostEndFilter.ALL,
                            0.10d, 0.10d, target);
                        if (cell == null)
                            continue;
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {config} | {set} | {target:P1} | {cell.Eligible} | {cell.WinRate:P1} | {cell.AvgNetR:F3} | {cell.AvgRr:F2} | {cell.TimeoutRate:P1} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendStopSweep(
            StringBuilder builder, IReadOnlyList<PostEndCell> cells)
        {
            builder.AppendLine("## Stop sweep (confirm 10%, target 50%)");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Stop | N | win | avg netR | avg R:R | timeout |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");

            foreach (string config in new[] { "permissive", "exact" })
            {
                foreach (string set in new[] { "tuning", "fresh" })
                {
                    foreach (double stop in POST_END_STOPS)
                    {
                        PostEndCell? cell = FindCell(
                            cells, config, set, PostEndMode.CAUSAL, PostEndFilter.ALL,
                            0.10d, stop, 0.50d);
                        if (cell == null)
                            continue;
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {config} | {set} | {stop:P0} | {cell.Eligible} | {cell.WinRate:P1} | {cell.AvgNetR:F3} | {cell.AvgRr:F2} | {cell.TimeoutRate:P1} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendBestGroups(
            StringBuilder builder,
            IReadOnlyList<PostEndBest> best,
            IReadOnlyList<PostEndTrade> trades)
        {
            PostEndBest? permissive = best.FirstOrDefault(
                b => b.Tuning.Config == "permissive" &&
                     b.Tuning.Key.Mode == PostEndMode.CAUSAL &&
                     b.Tuning.Key.Filter == PostEndFilter.WITHIN_CAP);
            if (permissive == null)
                return;

            PostEndCellKey key = permissive.Tuning.Key;
            List<PostEndTrade> fresh = trades
                .Where(t => t.Config == key.Config && t.Set == "fresh" &&
                            t.Mode == key.Mode &&
                            Math.Abs(t.ConfirmRatio - key.ConfirmRatio) < 1e-9 &&
                            Math.Abs(t.StopRatio - key.StopRatio) < 1e-9 &&
                            Math.Abs(t.TargetRatio - key.TargetRatio) < 1e-9 &&
                            !t.CapOvershoot)
                .ToList();

            builder.AppendLine("## Best causal cell on fresh, by group");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Cell: confirm {key.ConfirmRatio:P0}, stop {key.StopRatio:P0}, target {key.TargetRatio:P0}, permissive, within cap."));
            builder.AppendLine();
            builder.AppendLine("| Group | Value | N | win | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");

            var dimensions = new (string Name, Func<PostEndTrade, string> Key)[]
            {
                ("pair", t => t.Symbol),
                ("year", t => t.SignalTime.Year.ToString(CultureInfo.InvariantCulture)),
                ("session", t => SessionOf(t.SignalTime))
            };

            foreach ((string name, Func<PostEndTrade, string> keySelector) in dimensions)
            {
                foreach (IGrouping<string, PostEndTrade> group in fresh
                             .GroupBy(keySelector)
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    List<PostEndTrade> subset = group.ToList();
                    double winRate = subset.Count(t => t.Outcome == "TP") /
                                     (double)subset.Count;
                    double avgNetR = subset.Average(t => t.NetR);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {name} | {group.Key} | {subset.Count} | {winRate:P1} | {avgNetR:F3} |"));
                }
            }

            builder.AppendLine();
        }

        private static void AppendConclusions(
            StringBuilder builder,
            IReadOnlyList<CorrectionRun> runs,
            IReadOnlyList<PostEndCell> cells,
            IReadOnlyList<PostEndBest> best,
            IReadOnlyList<PostEndTrade> trades)
        {
            builder.AppendLine("## What it says");
            builder.AppendLine();

            PostEndBest? causalAll = best.FirstOrDefault(
                b => b.Tuning.Config == "permissive" &&
                     b.Tuning.Key.Mode == PostEndMode.CAUSAL &&
                     b.Tuning.Key.Filter == PostEndFilter.ALL);
            PostEndBest? causalCap = best.FirstOrDefault(
                b => b.Tuning.Config == "permissive" &&
                     b.Tuning.Key.Mode == PostEndMode.CAUSAL &&
                     b.Tuning.Key.Filter == PostEndFilter.WITHIN_CAP);
            PostEndBest? exPost = best.FirstOrDefault(
                b => b.Tuning.Config == "permissive" &&
                     b.Tuning.Key.Mode == PostEndMode.EX_POST);

            if (exPost != null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- **Upper bound (ex-post next open).** Best holdout cell: stop {exPost.Tuning.Key.StopRatio:P0}, target {exPost.Tuning.Key.TargetRatio:P0}. Tuning {exPost.Tuning.Eligible} trades, {exPost.Tuning.WinRate:P1} win, {exPost.Tuning.AvgNetR:F3}R; fresh {exPost.Fresh.Eligible}, {exPost.Fresh.WinRate:P1}, {exPost.Fresh.AvgNetR:F3}R. This uses the actual end; it is not a live result."));
            }

            if (causalAll != null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- **Live-like, all signals.** Best causal cell without the cap filter: confirm {causalAll.Tuning.Key.ConfirmRatio:P0}, stop {causalAll.Tuning.Key.StopRatio:P0}, target {causalAll.Tuning.Key.TargetRatio:P0}. Tuning {causalAll.Tuning.Eligible} trades, {causalAll.Tuning.WinRate:P1} win, {causalAll.Tuning.AvgNetR:F3}R; fresh {causalAll.Fresh.Eligible}, {causalAll.Fresh.WinRate:P1}, {causalAll.Fresh.AvgNetR:F3}R. The confirmation delay and the invalidated diagonals together eat the edge."));
            }

            if (causalCap != null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- **Live-like, within cap.** Best causal cell after the causal cap filter: confirm {causalCap.Tuning.Key.ConfirmRatio:P0}, stop {causalCap.Tuning.Key.StopRatio:P0}, target {causalCap.Tuning.Key.TargetRatio:P0}. Tuning {causalCap.Tuning.Eligible} trades, {causalCap.Tuning.WinRate:P1} win, {causalCap.Tuning.AvgNetR:F3}R; fresh {causalCap.Fresh.Eligible}, {causalCap.Fresh.WinRate:P1}, {causalCap.Fresh.AvgNetR:F3}R."));
            }
            else
            {
                builder.AppendLine("- **No causal cell passed the 100-trade holdout bar.** The post-end entry is either too rare or too weak to support a conclusion at this sample size.");
            }

            builder.AppendLine("- **The cap filter does not survive causality.** Classifying by the **final** end looked like a switch in a separate ex-post check: the within-cap group was strongly positive and the overshoot group strongly negative. But the causal confirmed end is already inside the cap for ~92% of the entries, and that group still contains the diagonals that later extend to a new extreme; its netR stays negative. The apparent edge was an ex-post artifact, not a filter you can apply at the entry.");

            // The signal-entry baseline on the same sample, for a direct comparison.
            CorrectionRun tuningPermissive = runs.First(r =>
                r.Config == "permissive" && r.Set == "tuning");
            List<CorrectionSample> tuningLabelled = tuningPermissive.Samples
                .Where(x => x.Outcome.Labeled && x.Base.Resolved)
                .ToList();
            if (tuningLabelled.Count > 0)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"- **Signal entry on the same sample.** All resolved signals: {tuningLabelled.Average(x => x.Base.NetR):F3}R. The post-end trade is measured on a different risk (entry-to-stop beyond the end), so compare the sign and the fresh stability, not only the average."));
            }

            if (causalCap != null)
            {
                double minNetR = Math.Min(
                    causalCap.Tuning.AvgNetR, causalCap.Fresh.AvgNetR);
                builder.AppendLine(minNetR > 0
                    ? "- **Verdict.** A causal post-end entry with the cap filter is positive on both tuning and fresh. This is the first diagonal result that does not depend on the original stop surviving; it still needs a true walk-forward, a live end detector, and spread/slippage modelling before it can be a robot rule."
                    : "- **Verdict.** Even with the cap filter the best causal cell is not positive on both tuning and fresh. The post-end idea is structurally attractive (the correction is deep), but the confirmation delay and the stop beyond the end eat the edge. Do not ship it; the next step is a better end detector or a different stop/target basis.");
            }

            builder.AppendLine("- **Caveats.** The end is ex-post even in the causal variant: the sample is built from a future-known end, and the causal detector is only the entry rule. The archive is bid-only, has no spread, and the cost is a flat 2 bp. The fresh set is a pair holdout, not a new time period. The grid is wide, so only the tuning+fresh agreement is evidence.");
            builder.AppendLine();
        }

        private static PostEndCell? FindCell(
            IReadOnlyList<PostEndCell> cells,
            string config,
            string set,
            PostEndMode mode,
            PostEndFilter filter,
            double confirm,
            double stop,
            double target)
        {
            return cells.FirstOrDefault(c =>
                c.Config == config && c.Set == set && c.Key.Mode == mode &&
                c.Key.Filter == filter &&
                Math.Abs(c.Key.ConfirmRatio - confirm) < 1e-9 &&
                Math.Abs(c.Key.StopRatio - stop) < 1e-9 &&
                Math.Abs(c.Key.TargetRatio - target) < 1e-9);
        }

        #endregion

        #region CSV

        private static string BuildPostEndCsv(IReadOnlyList<PostEndTrade> trades)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "config,set,file,symbol,signal_time,mode,confirm,stop,target,end_time,end_bar," +
                "end_price,height,entry_time,entry_bar,entry,stop_price,target_price,risk,reward," +
                "planned_rr,outcome,gross_r,cost_r,net_r,resolved,bars_to_exit,mfe_r,mae_r," +
                "cap_overshoot,depth_diag");

            foreach (PostEndTrade t in trades
                         .Where(t => (t.Mode == PostEndMode.EX_POST &&
                                      Math.Abs(t.StopRatio - 0.10d) < 1e-9 &&
                                      Math.Abs(t.TargetRatio - 0.50d) < 1e-9) ||
                                     (t.Mode == PostEndMode.CAUSAL &&
                                      Math.Abs(t.ConfirmRatio - 0.10d) < 1e-9 &&
                                      Math.Abs(t.StopRatio - 0.10d) < 1e-9 &&
                                      Math.Abs(t.TargetRatio - 0.50d) < 1e-9))
                         .OrderBy(t => t.Config, StringComparer.Ordinal)
                         .ThenBy(t => t.Set, StringComparer.Ordinal)
                         .ThenBy(t => t.Mode)
                         .ThenBy(t => t.File, StringComparer.Ordinal)
                         .ThenBy(t => t.EntryTime))
            {
                builder.AppendLine(string.Join(",",
                    Csv(t.Config),
                    Csv(t.Set),
                    Csv(t.File),
                    Csv(t.Symbol),
                    Csv(t.SignalTime.ToString("o", CultureInfo.InvariantCulture)),
                    Csv(t.Mode == PostEndMode.EX_POST ? "ex-post" : "causal"),
                    F(t.ConfirmRatio), F(t.StopRatio), F(t.TargetRatio),
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

        private enum PostEndMode
        {
            EX_POST,
            CAUSAL
        }

        private enum PostEndFilter
        {
            ALL,
            WITHIN_CAP
        }

        private readonly record struct PostEndCellKey(
            string Config,
            string Set,
            PostEndMode Mode,
            PostEndFilter Filter,
            double ConfirmRatio,
            double StopRatio,
            double TargetRatio);

        private sealed record PostEndTrade(
            string Config,
            string Set,
            string File,
            string Symbol,
            DateTime SignalTime,
            PostEndMode Mode,
            double ConfirmRatio,
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
            int ExitBar,
            int BarsToExit,
            double MfeR,
            double MaeR,
            bool CapOvershoot,
            double DepthDiag,
            bool Resolved);

        private sealed record PostEndCell(
            PostEndCellKey Key,
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
            public PostEndFilter Filter => Key.Filter;

            public static PostEndCell Create(
                PostEndCellKey key,
                IReadOnlyList<PostEndTrade> trades,
                int sampleCount)
            {
                int tp = trades.Count(t => t.Outcome == "TP");
                int sl = trades.Count(t => t.Outcome == "SL");
                int timeout = trades.Count(t => t.Outcome == "TIMEOUT");
                int eligible = trades.Count;
                List<PostEndTrade> tpTrades = trades
                    .Where(t => t.Outcome == "TP")
                    .ToList();
                List<PostEndTrade> slTrades = trades
                    .Where(t => t.Outcome == "SL")
                    .ToList();
                return new PostEndCell(
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

        private sealed record PostEndBest(PostEndCell Tuning, PostEndCell Fresh);

        #endregion
    }
}
