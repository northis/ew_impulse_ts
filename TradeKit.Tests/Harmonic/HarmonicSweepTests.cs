using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Research harness that sweeps the whole take profit x stop loss grid on the local price
    /// archive. Excluded from the normal test run; writes <c>reports/harmonic_sweep.md</c>.
    /// <para>
    /// The finder runs once per archive file and every recorded entry is then replayed against
    /// the whole grid, so the setups are identical everywhere and only the levels change. All
    /// the distances are measured from the real entry price - the close of the bar that
    /// confirmed the point D.
    /// </para>
    /// <para>
    /// A separate target and stop sweep cannot find the best pair: a tighter stop raises the
    /// reward/risk of every target, which moves the optimum towards the farther ones. Only the
    /// full grid shows the combination.
    /// </para>
    /// <para>
    /// Run: <c>dotnet test --filter "FullyQualifiedName~HarmonicSweep"</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    [Explicit("Research harness - run manually to (re)generate reports/harmonic_sweep.md")]
    [Category("Research")]
    public class HarmonicSweepTests
    {
        /// <summary>
        /// The round trip cost - spread plus commission - as a fraction of the price. Without
        /// it the grid always picks the tightest stop of the sweep: a stop a few pips away
        /// turns any target into a 40:1 lottery ticket that no broker would ever fill.
        /// </summary>
        private const double COST_RATE = 0.0002d;

        /// <summary>
        /// The minimum stop distance filters, in basis points of the price. A setup whose stop
        /// sits closer than the threshold is dropped: the cost is a fixed share of the price,
        /// so a stop of a few basis points spends the whole edge on the spread. The thresholds
        /// are cumulative - a setup is counted in every filter it passes.
        /// </summary>
        private static readonly double[] FILTERS = { 0d, 25d, 50d, 75d, 100d, 150d };

        /// <summary>
        /// The models to research. The Crab is excluded: it loses on every cell of the grid.
        /// </summary>
        private static readonly HarmonicPatternType[] MODELS =
        {
            HarmonicPatternType.GARTLEY,
            HarmonicPatternType.BAT,
            HarmonicPatternType.BUTTERFLY,
            HarmonicPatternType.SHARK,
            HarmonicPatternType.CYPHER
        };

        private sealed record Trade(HarmonicItem Item, int EntryIndex, double Entry)
        {
            public bool IsBull => Item.IsBull;
        }

        /// <summary>A take profit candidate. <see cref="Short"/> is the matrix header.</summary>
        private sealed record TargetVariant(string Name, string Short, Func<Trade, double> Resolve);

        /// <summary>
        /// A stop loss candidate. Only the modes that do not depend on the target are used:
        /// a target-relative stop would fix the reward/risk and hide the grid.
        /// </summary>
        private sealed record StopVariant(string Name, string Short, Func<Trade, double> Resolve);

        private sealed class Stats
        {
            public int Trades;
            public int TakeProfits;
            public int StopLosses;
            public int Skipped;
            public int Open;
            public double RiskRewardSum;
            public double ResultSum;
            public double CostSum;
            public double StopDistanceSum;
            public int ResultCount;

            public double WinRate => TakeProfits + StopLosses == 0
                ? 0d
                : (double)TakeProfits / (TakeProfits + StopLosses);

            public double AverageRiskReward => Trades == 0 ? 0d : RiskRewardSum / Trades;

            /// <summary>The average stop distance in basis points of the entry price.</summary>
            public double AverageStopDistance =>
                Trades == 0 ? 0d : 1e4 * StopDistanceSum / Trades;

            /// <summary>The average cost of a closed trade, in R.</summary>
            public double AverageCost => ResultCount == 0 ? 0d : CostSum / ResultCount;

            public double TotalR => ResultSum;
            public double NetTotalR => ResultSum - CostSum;
            public double NetAverageR => ResultCount == 0 ? 0d : NetTotalR / ResultCount;
        }

        private static readonly TargetVariant[] TARGETS = BuildTargets();
        private static readonly StopVariant[] STOPS = BuildStops();

        private static TargetVariant[] BuildTargets()
        {
            var variants = new List<TargetVariant>
            {
                new("Model TP1", "TP1", trade => trade.Item.TakeProfit1)
            };

            foreach (double ratio in new[]
                     {
                         0.236d, HarmonicFib.F382, 0.5d, HarmonicFib.F618, 0.786d, 1d,
                         HarmonicFib.F1272
                     })
            {
                variants.Add(Target(HarmonicTargetBasis.AD, ratio));
            }

            foreach (double ratio in new[] { HarmonicFib.F382, 0.5d, HarmonicFib.F618, 1d })
                variants.Add(Target(HarmonicTargetBasis.PATTERN_HEIGHT, ratio));

            return variants.ToArray();

            static TargetVariant Target(HarmonicTargetBasis basis, double ratio)
            {
                var target = new HarmonicTarget(basis, ratio);
                string prefix = basis == HarmonicTargetBasis.AD ? "AD" : "PH";

                return new TargetVariant(
                    Format("{0} x {1:F3}", basis, ratio),
                    Format("{0} {1:F3}", prefix, ratio),
                    trade =>
                    {
                        HarmonicItem item = trade.Item;
                        return target.Resolve(item.ItemX.Value, item.ItemA.Value,
                            item.ItemB.Value, item.ItemC.Value, item.ItemD.Value, trade.Entry);
                    });
            }
        }

        private static StopVariant[] BuildStops()
        {
            var variants = new List<StopVariant>();

            // The pattern-relative grid, refined around the optimum of the first sweep.
            foreach (double percent in new[] { 1d, 1.5d, 2d, 2.5d, 3d, 4d, 5d, 7.5d, 10d, 20d })
                variants.Add(Stop(HarmonicStopMode.PATTERN_PERCENT_BEYOND_D, "D", percent));

            foreach (double percent in new[] { 2.5d, 5d, 10d, 20d, 30d, 50d })
                variants.Add(Stop(HarmonicStopMode.PATTERN_PERCENT_BEYOND_ENTRY, "E", percent));

            // Exactly at the point D: the tightest structural stop that still makes sense.
            variants.Add(Stop(HarmonicStopMode.PERCENT_BEYOND_D, "D", 0d));

            return variants.ToArray();

            static StopVariant Stop(HarmonicStopMode mode, string anchor, double percent)
            {
                return new StopVariant(
                    Format("{0} x {1:F2}%", mode, percent),
                    Format("{0}-{1:0.##}%", anchor, percent),
                    trade => HarmonicMath.CalculateStopLoss(
                        mode, percent, trade.IsBull, trade.Item.ItemX.Value,
                        trade.Item.ItemD.Value, trade.Item.Prz,

                        // No mode of the grid is target-relative; a NaN would turn into an
                        // invalid level and be reported as skipped instead of silently scoring.
                        double.NaN, trade.Entry, trade.Item.PatternHeight));
            }
        }

        private static string Format(string format, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, format, args);

        [Test]
        public void Sweep_WriteReport()
        {
            string? repoRoot = HarmonicCsvData.FindRepoRoot();
            if (repoRoot == null)
            {
                Assert.Inconclusive("The local price archive was not found.");
                return;
            }

            var parameters = new HarmonicParams
            {
                Patterns = new SortedSet<HarmonicPatternType>(MODELS),

                // The finder levels are not used by the grid, but a setup must not be dropped
                // by a reward/risk filter before it reaches the sweep.
                StopMode = HarmonicStopMode.PATTERN_PERCENT_BEYOND_D,
                StopPercent = 10d,
                MinimumRiskReward = 0d,

                // The research measures what the trade really has to travel.
                TargetAnchor = HarmonicTargetAnchor.ENTRY
            };

            var overall = new Stats[TARGETS.Length, STOPS.Length, FILTERS.Length];
            var perModel = new Stats[TARGETS.Length, STOPS.Length, MODELS.Length, FILTERS.Length];
            for (int t = 0; t < TARGETS.Length; t++)
            for (int s = 0; s < STOPS.Length; s++)
            for (int f = 0; f < FILTERS.Length; f++)
            {
                overall[t, s, f] = new Stats();
                for (int m = 0; m < MODELS.Length; m++)
                    perModel[t, s, m, f] = new Stats();
            }

            int totalTrades = 0;
            IReadOnlyList<string> files = HarmonicCsvData.GetAllFiles();
            Assert.That(files, Is.Not.Empty, "The data folder holds no archive file.");

            var replay = new Replay(TARGETS.Length, STOPS.Length);

            foreach (string file in files)
            {
                // The whole archive does not fit in memory at once, so the providers are not
                // cached: every file is loaded, replayed and dropped.
                TestBarsProvider provider = HarmonicCsvData.Load(file, cache: false);
                var finder = new HarmonicSetupFinder(provider, provider.BarSymbol, parameters);
                var trades = new List<Trade>();

                finder.OnEnter += (_, e) => trades.Add(
                    new Trade(e.HarmonicItem, e.Level.BarIndex, e.Level.Value));

                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));

                totalTrades += trades.Count;
                TestContext.WriteLine($"{file}: bars={provider.Count} setups={trades.Count}");

                foreach (Trade trade in trades)
                {
                    replay.Run(provider, trade);
                    int model = Array.IndexOf(MODELS, trade.Item.PatternType);

                    for (int s = 0; s < STOPS.Length; s++)
                    {
                        int last = replay.GetLastFilter(s);
                        for (int t = 0; t < TARGETS.Length; t++)
                        for (int f = 0; f <= last; f++)
                        {
                            replay.Score(t, s, overall[t, s, f]);
                            replay.Score(t, s, perModel[t, s, model, f]);
                        }
                    }
                }
            }

            Assert.That(totalTrades, Is.GreaterThan(0), "No setup was produced by the archive.");

            string reportsDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportsDir);
            string path = Path.Combine(reportsDir, "harmonic_sweep.md");
            File.WriteAllText(path, BuildReport(overall, perModel, totalTrades, files.Count));
            TestContext.WriteLine($"Wrote {path}");
        }

        /// <summary>
        /// Replays one setup against every level of the grid in a single forward pass.
        /// <para>
        /// Each level is resolved to the index of the bar that touched it first, and a cell of
        /// the grid is then decided by comparing the two indices. The stop loss wins a tie, so
        /// a bar touching both levels counts as a loss - exactly as the finder does.
        /// </para>
        /// </summary>
        private sealed class Replay
        {
            private const int NEVER = int.MaxValue;

            private readonly double[] m_TakeProfits;
            private readonly double[] m_StopLosses;
            private readonly int[] m_TakeProfitHits;
            private readonly int[] m_StopLossHits;
            private readonly bool[] m_TakeProfitValid;
            private readonly bool[] m_StopLossValid;
            private readonly double[] m_StopDistances;
            private readonly double[] m_Costs;
            private readonly double[,] m_RiskRewards;

            public Replay(int targetCount, int stopCount)
            {
                m_TakeProfits = new double[targetCount];
                m_StopLosses = new double[stopCount];
                m_TakeProfitHits = new int[targetCount];
                m_StopLossHits = new int[stopCount];
                m_TakeProfitValid = new bool[targetCount];
                m_StopLossValid = new bool[stopCount];
                m_StopDistances = new double[stopCount];
                m_Costs = new double[stopCount];
                m_RiskRewards = new double[targetCount, stopCount];
            }

            public void Run(TestBarsProvider provider, Trade trade)
            {
                bool isBull = trade.IsBull;
                double entry = trade.Entry;

                for (int t = 0; t < m_TakeProfits.Length; t++)
                {
                    double level = TARGETS[t].Resolve(trade);
                    m_TakeProfits[t] = level;
                    m_TakeProfitValid[t] = isBull ? level > entry : level < entry;
                    m_TakeProfitHits[t] = NEVER;
                }

                int pending = 0;
                for (int s = 0; s < m_StopLosses.Length; s++)
                {
                    double level = STOPS[s].Resolve(trade);
                    m_StopLosses[s] = level;
                    m_StopLossValid[s] = isBull ? level < entry : level > entry;
                    m_StopLossHits[s] = NEVER;

                    // The spread is paid in price units, so the tighter the stop the larger
                    // the share of the risk it eats.
                    double distance = Math.Abs(entry - level);
                    m_StopDistances[s] = m_StopLossValid[s] ? distance / entry : 0d;
                    m_Costs[s] = m_StopLossValid[s] ? entry * COST_RATE / distance : 0d;

                    if (m_StopLossValid[s])
                        pending++;
                }

                for (int t = 0; t < m_TakeProfits.Length; t++)
                for (int s = 0; s < m_StopLosses.Length; s++)
                {
                    m_RiskRewards[t, s] = m_TakeProfitValid[t] && m_StopLossValid[s]
                        ? Math.Abs(m_TakeProfits[t] - entry) / Math.Abs(entry - m_StopLosses[s])
                        : 0d;
                }

                // Once every stop is resolved the pass can end: a target that is still open
                // would be hit later than any stop, which is a loss in every cell anyway.
                for (int i = trade.EntryIndex + 1; i < provider.Count && pending > 0; i++)
                {
                    double high = provider.GetHighPrice(i);
                    double low = provider.GetLowPrice(i);

                    for (int t = 0; t < m_TakeProfits.Length; t++)
                    {
                        if (!m_TakeProfitValid[t] || m_TakeProfitHits[t] != NEVER)
                            continue;

                        if (isBull ? high >= m_TakeProfits[t] : low <= m_TakeProfits[t])
                            m_TakeProfitHits[t] = i;
                    }

                    for (int s = 0; s < m_StopLosses.Length; s++)
                    {
                        if (!m_StopLossValid[s] || m_StopLossHits[s] != NEVER)
                            continue;

                        if (isBull ? low <= m_StopLosses[s] : high >= m_StopLosses[s])
                        {
                            m_StopLossHits[s] = i;
                            pending--;
                        }
                    }
                }
            }

            public void Score(int target, int stop, Stats stats)
            {
                if (!m_TakeProfitValid[target] || !m_StopLossValid[stop])
                {
                    stats.Skipped++;
                    return;
                }

                double riskReward = m_RiskRewards[target, stop];
                stats.Trades++;
                stats.RiskRewardSum += riskReward;
                stats.StopDistanceSum += m_StopDistances[stop];

                int takeProfitHit = m_TakeProfitHits[target];
                int stopLossHit = m_StopLossHits[stop];

                if (takeProfitHit == NEVER && stopLossHit == NEVER)
                {
                    stats.Open++;
                    return;
                }

                stats.CostSum += m_Costs[stop];
                stats.ResultCount++;

                if (stopLossHit <= takeProfitHit)
                {
                    stats.StopLosses++;
                    stats.ResultSum -= 1d;
                }
                else
                {
                    stats.TakeProfits++;
                    stats.ResultSum += riskReward;
                }
            }

            /// <summary>
            /// The index of the widest minimum stop distance filter the stop passes. The
            /// filters are ascending, so the stop is counted by every filter up to it.
            /// </summary>
            public int GetLastFilter(int stop)
            {
                double basisPoints = 1e4 * m_StopDistances[stop];
                int last = 0;
                for (int f = 1; f < FILTERS.Length && basisPoints >= FILTERS[f]; f++)
                    last = f;

                return last;
            }
        }

        private static string BuildReport(
            Stats[,,] overall, Stats[,,,] perModel, int totalTrades, int fileCount)
        {
            var cells = new List<(int Target, int Stop)>();
            for (int t = 0; t < TARGETS.Length; t++)
            for (int s = 0; s < STOPS.Length; s++)
                cells.Add((t, s));

            (int Target, int Stop) Best(int filter) => cells
                .OrderByDescending(c => overall[c.Target, c.Stop, filter].NetTotalR)
                .First();

            (int Target, int Stop) best = Best(FILTERS.Length - 1);
            int bestFilter = FILTERS.Length - 1;
            for (int f = 0; f < FILTERS.Length; f++)
            {
                (int Target, int Stop) candidate = Best(f);
                if (overall[candidate.Target, candidate.Stop, f].NetAverageR >
                    overall[best.Target, best.Stop, bestFilter].NetAverageR)
                {
                    best = candidate;
                    bestFilter = f;
                }
            }

            var builder = new StringBuilder();
            builder.AppendLine("# Harmonic take profit x stop loss sweep");
            builder.AppendLine();
            builder.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            builder.AppendLine();
            builder.AppendLine(
                $"Archive files: {fileCount}. Setups: {totalTrades}. Models: " +
                $"{string.Join(", ", MODELS)}. Every level is measured from the real entry " +
                "price. R is the realised reward/risk: the planned R:R on a win, -1 on a loss.");
            builder.AppendLine();
            builder.AppendLine(Format(
                "A round trip costs {0:F2} bp of the price and is charged to every closed " +
                "trade, so a stop of `SL bp` basis points loses {0:F2}/`SL bp` of an R before " +
                "the market even moves. Net R is what is left after that.", 1e4 * COST_RATE));
            builder.AppendLine();
            builder.AppendLine(
                "`Min SL` is the minimum stop distance filter: a setup whose stop sits closer " +
                "than that is not traded at all. It is the only way to keep the cost from " +
                "eating the edge, and it is what the whole report is built around.");
            builder.AppendLine();
            builder.AppendLine(Format(
                "Best cell: **{0}** with **{1}**, minimum stop {2:F0} bp - net {3:F3} R over " +
                "{4} trades.", TARGETS[best.Target].Name, STOPS[best.Stop].Name,
                FILTERS[bestFilter], overall[best.Target, best.Stop, bestFilter].NetAverageR,
                overall[best.Target, best.Stop, bestFilter].Trades));
            builder.AppendLine();

            builder.AppendLine("## Net R per trade by minimum stop distance");
            builder.AppendLine();
            builder.AppendLine(
                "The best cell of every filter, and what the filter leaves of the archive. " +
                "The whole point is whether the surviving setups pay for themselves, so the " +
                "cells are ranked by net R per trade, not by the total.");
            builder.AppendLine();
            builder.AppendLine(
                "| Min SL | Target | Stop | Trades | Kept | Win rate | R:R | SL bp | Cost R | Gross avg R | Net avg R | Net R |");
            builder.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int f = 0; f < FILTERS.Length; f++)
            {
                (int Target, int Stop) top = cells
                    .OrderByDescending(c => overall[c.Target, c.Stop, f].NetAverageR)
                    .First();

                Stats stats = overall[top.Target, top.Stop, f];
                double kept = totalTrades == 0 ? 0d : (double)stats.Trades / totalTrades;
                builder.AppendLine(Format(
                    "| {0:F0} | {1} | {2} | {3} | {4:P1} | {5:P1} | {6:F2} | {7:F1} | {8:F3} | {9:F3} | {10:F3} | {11:F1} |",
                    FILTERS[f], TARGETS[top.Target].Name, STOPS[top.Stop].Name, stats.Trades,
                    kept, stats.WinRate, stats.AverageRiskReward, stats.AverageStopDistance,
                    stats.AverageCost, stats.AverageCost + stats.NetAverageR, stats.NetAverageR,
                    stats.NetTotalR));
            }

            builder.AppendLine();
            builder.AppendLine(Format(
                "## Net R per trade at a {0:F0} bp minimum stop", FILTERS[bestFilter]));
            builder.AppendLine();
            builder.AppendLine(
                "Rows are the targets, columns are the stops. `AD` is a fraction of the AD leg, " +
                "`PH` a fraction of the pattern height, `TP1` the model target. `D-x%` is x% of " +
                "the pattern height beyond the point D, `E-x%` the same beyond the entry, `D-0%` " +
                "exactly at the point D.");
            builder.AppendLine();

            builder.Append("| Target |");
            foreach (StopVariant stop in STOPS)
                builder.Append(' ').Append(stop.Short).Append(" |");

            builder.AppendLine();
            builder.Append("|---|");
            foreach (StopVariant _ in STOPS)
                builder.Append("---:|");

            builder.AppendLine();

            for (int t = 0; t < TARGETS.Length; t++)
            {
                builder.Append("| ").Append(TARGETS[t].Short).Append(" |");
                for (int s = 0; s < STOPS.Length; s++)
                {
                    string cell = Format("{0:F3}", overall[t, s, bestFilter].NetAverageR);
                    builder.Append(' ')
                        .Append(t == best.Target && s == best.Stop ? $"**{cell}**" : cell)
                        .Append(" |");
                }

                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine(Format(
                "## Top cells at a {0:F0} bp minimum stop", FILTERS[bestFilter]));
            builder.AppendLine();
            builder.AppendLine(
                "| Target | Stop | Trades | TP | SL | Open | Skipped | Win rate | R:R | SL bp | Cost R | Net avg R | Gross R | Net R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach ((int t, int s) in cells
                         .OrderByDescending(c => overall[c.Target, c.Stop, bestFilter].NetAverageR)
                         .Take(25))
            {
                builder.AppendLine(Row(
                    $"{TARGETS[t].Name} | {STOPS[s].Name}", overall[t, s, bestFilter]));
            }

            builder.AppendLine();
            builder.AppendLine("## Best cell per model");
            builder.AppendLine();
            builder.AppendLine(Format("{0} with {1}, minimum stop {2:F0} bp.",
                TARGETS[best.Target].Name, STOPS[best.Stop].Name, FILTERS[bestFilter]));
            builder.AppendLine();
            builder.AppendLine(
                "| Model | Trades | TP | SL | Open | Skipped | Win rate | R:R | SL bp | Cost R | Net avg R | Gross R | Net R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int m = 0; m < MODELS.Length; m++)
            {
                builder.AppendLine(Row(MODELS[m].ToString(),
                    perModel[best.Target, best.Stop, m, bestFilter]));
            }

            builder.AppendLine();
            builder.AppendLine("## Best cell of every model");
            builder.AppendLine();
            builder.AppendLine(
                "| Model | Target | Stop | Min SL | Trades | Win rate | R:R | Net avg R | Net R |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");
            for (int m = 0; m < MODELS.Length; m++)
            {
                int model = m;
                (int Target, int Stop, int Filter) top = cells
                    .SelectMany(c => Enumerable.Range(0, FILTERS.Length)
                        .Select(f => (c.Target, c.Stop, Filter: f)))
                    .OrderByDescending(c =>
                        perModel[c.Target, c.Stop, model, c.Filter].NetAverageR)
                    .First();

                Stats stats = perModel[top.Target, top.Stop, model, top.Filter];
                builder.AppendLine(Format(
                    "| {0} | {1} | {2} | {3:F0} | {4} | {5:P1} | {6:F2} | {7:F3} | {8:F1} |",
                    MODELS[model], TARGETS[top.Target].Name, STOPS[top.Stop].Name,
                    FILTERS[top.Filter], stats.Trades, stats.WinRate, stats.AverageRiskReward,
                    stats.NetAverageR, stats.NetTotalR));
            }

            return builder.ToString();

            static string Row(string name, Stats stats) => Format(
                "| {0} | {1} | {2} | {3} | {4} | {5} | {6:P1} | {7:F2} | {8:F1} | {9:F3} | {10:F3} | {11:F1} | {12:F1} |",
                name, stats.Trades, stats.TakeProfits, stats.StopLosses, stats.Open,
                stats.Skipped, stats.WinRate, stats.AverageRiskReward, stats.AverageStopDistance,
                stats.AverageCost, stats.NetAverageR, stats.TotalR, stats.NetTotalR);
        }
    }
}

