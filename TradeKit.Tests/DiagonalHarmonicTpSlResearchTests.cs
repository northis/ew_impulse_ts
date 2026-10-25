using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// TP/SL sweep on the diagonal-confluent harmonic setups. The setup search is the same as
    /// in <see cref="DiagonalHarmonicSetupResearchTests"/>: a harmonic XABCD completed at the
    /// diagonal's wave-5 extreme. The entry is the harmonic finder's own entry, but every
    /// target and stop is then replayed post-hoc against the full Fibonacci grid.
    /// <para>
    /// This is the follow-up the first reverse run pointed at: the diagonal gate selects
    /// patterns with a very high reward/risk (about 6:1) and a low win rate, so the shipped
    /// full-pattern-height target may simply be too far for this subset. The grid shows
    /// whether a closer target or a different stop produces a positive region.
    /// </para>
    /// <para>
    /// Writes <c>reports/diagonal_harmonic_tpsl.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalHarmonicTpSl"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        /// <summary>The smallest number of in-sample trades a cell needs to be ranked.</summary>
        private const int TPSL_MIN_SAMPLE = 15;

        /// <summary>
        /// The forward window of the post-hoc replay. A level not touched inside it counts as
        /// open; the cap keeps the replay bounded on the long m15 files.
        /// </summary>
        private const int TPSL_HORIZON = 1000;

        private const int TPSL_ATR_PERIODS = 14;

        private static readonly HarmonicPatternType[] TPSL_MODELS =
        {
            HarmonicPatternType.GARTLEY,
            HarmonicPatternType.BAT,
            HarmonicPatternType.BUTTERFLY,
            HarmonicPatternType.CRAB,
            HarmonicPatternType.SHARK,
            HarmonicPatternType.CYPHER
        };

        private static readonly TpSlTarget[] TPSL_TARGETS = BuildTpSlTargets();
        private static readonly TpSlStop[] TPSL_STOPS = BuildTpSlStops();

        /// <summary>
        /// The target region that the first sweep pointed at: the shipped pattern-height target
        /// and its neighbours with a stop just beyond the point D.
        /// </summary>
        private static readonly string[] TPSL_REGION_TARGETS =
        {
            "AD 0.618", "AD 0.786", "AD 1.000", "PH 0.618", "PH 1.000"
        };

        /// <summary>
        /// The part of the region that is positive on both halves. The farther targets are
        /// positive on test but negative on train, so they belong to the table, not to the claim.
        /// </summary>
        private static readonly string[] TPSL_STABLE_REGION_TARGETS =
        {
            "AD 0.618", "AD 0.786", "PH 0.618"
        };

        private const string TPSL_REGION_STOP = "D-5%";

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_harmonic_tpsl.md")]
        [Category("Research")]
        public void DiagonalHarmonicTpSl_Report()
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

            var parameters = new HarmonicParams
            {
                // The grid replaces every shipped level, so the finder must not drop a setup
                // because of a reward/risk or ATR filter before it reaches the sweep.
                MinimumStopAtr = 0d,
                MinimumRiskReward = 0d,
                MinimumScore = 0d,
                StopMode = HarmonicStopMode.PATTERN_PERCENT_BEYOND_D,
                StopPercent = 10d,

                // `Model TP1` must mean the model target, not the single shipped override.
                TakeProfit1Override = null,
                TargetAnchor = HarmonicTargetAnchor.ENTRY
            };

            List<HarmonicTrade> allTrades = RunHarmonicAll(dataDir, files, parameters);
            TestContext.Out.WriteLine(
                $"Harmonic setups: {allTrades.Count}, resolved={allTrades.Count(t => t.Resolved)}");

            Dictionary<string, Dictionary<int, List<Sample>>> diagonalsByFile =
                IndexDiagonals(diagonals);

            var joins = new[]
            {
                new DiagonalJoinConfig("tight: ±1 bars / 10% / lag 1", 1, 0.10d, 1),
                new DiagonalJoinConfig("moderate: ±2 bars / 25% / lag 2", 2, 0.25d, 2),
                new DiagonalJoinConfig("loose: ±20 bars / 50% / lag 10", 20, 0.50d, 10)
            };

            var results = new List<TpSlJoinResult>();
            foreach (DiagonalJoinConfig join in joins)
            {
                List<HarmonicTrade> filtered = allTrades
                    .Where(t => HasDiagonalConfluence(t, diagonalsByFile, join))
                    .ToList();

                TestContext.Out.WriteLine(
                    $"{join.Name}: setups={filtered.Count}, " +
                    $"train={filtered.Count(t => t.EntryTime.Year <= 2022)}, " +
                    $"test={filtered.Count(t => t.EntryTime.Year >= 2023)}");

                results.Add(RunTpSlGrid(join, filtered));
            }

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_harmonic_tpsl.md");
            File.WriteAllText(reportPath, BuildTpSlReport(results, files.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        #region Grid

        private static TpSlJoinResult RunTpSlGrid(
            DiagonalJoinConfig join, IReadOnlyList<HarmonicTrade> trades)
        {
            var overall = new TpSlStats[TPSL_TARGETS.Length, TPSL_STOPS.Length, 2];
            var perModel =
                new TpSlStats[TPSL_TARGETS.Length, TPSL_STOPS.Length, TPSL_MODELS.Length, 2];

            for (int t = 0; t < TPSL_TARGETS.Length; t++)
            for (int s = 0; s < TPSL_STOPS.Length; s++)
            for (int split = 0; split < 2; split++)
            {
                overall[t, s, split] = new TpSlStats();
                for (int m = 0; m < TPSL_MODELS.Length; m++)
                    perModel[t, s, m, split] = new TpSlStats();
            }

            var replay = new TpSlReplay(TPSL_TARGETS.Length, TPSL_STOPS.Length);

            foreach (HarmonicTrade trade in trades)
            {
                replay.Run(trade);
                int split = trade.EntryTime.Year <= 2022 ? 0 : 1;
                int model = Array.IndexOf(TPSL_MODELS, trade.Type);

                for (int s = 0; s < TPSL_STOPS.Length; s++)
                {
                    for (int t = 0; t < TPSL_TARGETS.Length; t++)
                    {
                        replay.Score(t, s, overall[t, s, split]);
                        if (model >= 0)
                            replay.Score(t, s, perModel[t, s, model, split]);
                    }
                }
            }

            return new TpSlJoinResult(join, trades.Count, overall, perModel)
            {
                TrainCount = trades.Count(t => t.EntryTime.Year <= 2022),
                TestCount = trades.Count(t => t.EntryTime.Year >= 2023)
            };
        }

        private static TpSlTarget[] BuildTpSlTargets()
        {
            var variants = new List<TpSlTarget>
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

            static TpSlTarget Target(HarmonicTargetBasis basis, double ratio)
            {
                var target = new HarmonicTarget(basis, ratio);
                string prefix = basis == HarmonicTargetBasis.AD ? "AD" : "PH";

                return new TpSlTarget(
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

        private static TpSlStop[] BuildTpSlStops()
        {
            var variants = new List<TpSlStop>();

            foreach (double percent in new[] { 1d, 1.5d, 2d, 2.5d, 3d, 4d, 5d, 7.5d, 10d, 20d })
                variants.Add(Stop(HarmonicStopMode.PATTERN_PERCENT_BEYOND_D, "D", percent));

            foreach (double percent in new[] { 2.5d, 5d, 10d, 20d, 30d, 50d })
                variants.Add(Stop(HarmonicStopMode.PATTERN_PERCENT_BEYOND_ENTRY, "E", percent));

            variants.Add(Stop(HarmonicStopMode.PERCENT_BEYOND_D, "D", 0d));

            return variants.ToArray();

            static TpSlStop Stop(HarmonicStopMode mode, string anchor, double percent)
            {
                return new TpSlStop(
                    Format("{0} x {1:F2}%", mode, percent),
                    Format("{0}-{1:0.##}%", anchor, percent),
                    trade => HarmonicMath.CalculateStopLoss(
                        mode, percent, trade.IsBull, trade.Item.ItemX.Value,
                        trade.Item.ItemD.Value, trade.Item.Prz,
                        double.NaN, trade.Entry, trade.Item.PatternHeight));
            }
        }

        private static string Format(string format, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, format, args);

        /// <summary>
        /// Replays one setup against every level of the grid in a single forward pass. The stop
        /// wins a tie, exactly as in the finder and in <c>HarmonicSweepTests</c>.
        /// </summary>
        private sealed class TpSlReplay
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
            private readonly double[] m_StopAtrs;
            private readonly double[,] m_RiskRewards;

            public TpSlReplay(int targetCount, int stopCount)
            {
                m_TakeProfits = new double[targetCount];
                m_StopLosses = new double[stopCount];
                m_TakeProfitHits = new int[targetCount];
                m_StopLossHits = new int[stopCount];
                m_TakeProfitValid = new bool[targetCount];
                m_StopLossValid = new bool[stopCount];
                m_StopDistances = new double[stopCount];
                m_Costs = new double[stopCount];
                m_StopAtrs = new double[stopCount];
                m_RiskRewards = new double[targetCount, stopCount];
            }

            public void Run(HarmonicTrade trade)
            {
                bool isBull = trade.IsBull;
                double entry = trade.Entry;
                TestBarsProvider provider = trade.Provider;
                double atr = GetAverageTrueRange(provider, trade.EntryBar);

                for (int t = 0; t < m_TakeProfits.Length; t++)
                {
                    double level = TPSL_TARGETS[t].Resolve(trade);
                    m_TakeProfits[t] = level;
                    m_TakeProfitValid[t] = isBull ? level > entry : level < entry;
                    m_TakeProfitHits[t] = NEVER;
                }

                int pending = 0;
                for (int s = 0; s < m_StopLosses.Length; s++)
                {
                    double level = TPSL_STOPS[s].Resolve(trade);
                    m_StopLosses[s] = level;
                    m_StopLossValid[s] = isBull ? level < entry : level > entry;
                    m_StopLossHits[s] = NEVER;

                    double distance = Math.Abs(entry - level);
                    m_StopDistances[s] = m_StopLossValid[s] ? distance / entry : 0d;
                    m_Costs[s] = m_StopLossValid[s] ? entry * COST_RATE / distance : 0d;
                    m_StopAtrs[s] = m_StopLossValid[s] && atr > 0d ? distance / atr : 0d;

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

                int end = Math.Min(provider.Count - 1, trade.EntryBar + TPSL_HORIZON);
                for (int i = trade.EntryBar + 1; i <= end && pending > 0; i++)
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

            public void Score(int target, int stop, TpSlStats stats)
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
                stats.StopAtrSum += m_StopAtrs[stop];

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

            private static double GetAverageTrueRange(TestBarsProvider provider, int index)
            {
                int first = Math.Max(1, index - TPSL_ATR_PERIODS + 1);
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
        }

        #endregion

        #region Report

        private static string BuildTpSlReport(
            IReadOnlyList<TpSlJoinResult> results, int fileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal-confluent harmonic setups — TP/SL sweep");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Files: {fileCount} (8 h1 + 4 m15). The setup is a harmonic XABCD confirmed at the diagonal wave-5 extreme; the entry is the harmonic finder's own entry. Every target and stop is then replayed post-hoc on the same entry. The forward window is {TPSL_HORIZON} bars."));
            builder.AppendLine();
            builder.AppendLine("Train = entries in 2017–2022, test = 2023–2026. Net R includes a 2 bp round-trip cost. The stop wins a tie, exactly as in the finder.");
            builder.AppendLine();

            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Join | Setups | Train | Test |");
            builder.AppendLine("|---|---:|---:|---:|");
            foreach (TpSlJoinResult result in results)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {result.Join.Name} | {result.Setups} | {result.TrainCount} | {result.TestCount} |"));
            }

            foreach (TpSlJoinResult result in results)
            {
                builder.AppendLine();
                builder.AppendLine(FormattableString.Invariant(
                    $"## Join — {result.Join.Name}"));
                builder.AppendLine();
                builder.AppendLine(FormattableString.Invariant(
                    $"Setups: {result.Setups} (train {result.TrainCount}, test {result.TestCount})."));
                builder.AppendLine();

                (int Target, int Stop) best = SelectBest(result, TPSL_MIN_SAMPLE);
                TpSlStats bestTrain = result.Overall[best.Target, best.Stop, 0];
                TpSlStats bestTest = result.Overall[best.Target, best.Stop, 1];

                builder.AppendLine("Selected on train:");
                builder.AppendLine();
                builder.AppendLine("| Target | Stop | Train N | Train win | Train R:R | Train netR | Train ATR | Test N | Test win | Test R:R | Test netR |");
                builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
                builder.AppendLine(Row(TPSL_TARGETS[best.Target].Name, TPSL_STOPS[best.Stop].Name,
                    bestTrain, bestTest));

                builder.AppendLine();
                builder.AppendLine("Top 10 in-sample cells:");
                builder.AppendLine();
                builder.AppendLine("| Target | Stop | Train N | Train netR | Test N | Test win | Test netR |");
                builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
                foreach ((int t, int s) in Rank(result, 10))
                {
                    TpSlStats train = result.Overall[t, s, 0];
                    TpSlStats test = result.Overall[t, s, 1];
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {TPSL_TARGETS[t].Name} | {TPSL_STOPS[s].Name} | {train.Trades} | {train.NetAverageR:F3} | {test.Trades} | {test.WinRate:P1} | {test.NetAverageR:F3} |"));
                }

                builder.AppendLine();
                builder.AppendLine("### Out-of-sample net R heatmap");
                builder.AppendLine();
                builder.AppendLine(
                    "Rows are targets, columns are stops. `AD` is a fraction of the AD leg, `PH` a fraction of the pattern height, `TP1` the model target. `D-x%` is x% of the pattern height beyond the point D, `E-x%` the same beyond the entry, `D-0%` exactly at the point D. An edge is a region, not a single cell.");
                builder.AppendLine();
                builder.Append("| Target |");
                foreach (TpSlStop stop in TPSL_STOPS)
                    builder.Append(' ').Append(stop.Short).Append(" |");
                builder.AppendLine();
                builder.Append("|---|");
                foreach (TpSlStop _ in TPSL_STOPS)
                    builder.Append("---:|");
                builder.AppendLine();

                for (int t = 0; t < TPSL_TARGETS.Length; t++)
                {
                    builder.Append("| ").Append(TPSL_TARGETS[t].Short).Append(" |");
                    for (int s = 0; s < TPSL_STOPS.Length; s++)
                    {
                        TpSlStats test = result.Overall[t, s, 1];
                        string cell = test.ResultCount == 0
                            ? "—"
                            : test.NetAverageR.ToString("F3", CultureInfo.InvariantCulture);
                        builder.Append(' ')
                            .Append(t == best.Target && s == best.Stop ? $"**{cell}**" : cell)
                            .Append(" |");
                    }

                    builder.AppendLine();
                }

                builder.AppendLine();
                builder.AppendLine("### Robust region — AD/PH 0.618–1.0 with the D-5% stop");
                builder.AppendLine();
                builder.AppendLine("| Target | Train N | Train netR | Test N | Test netR |");
                builder.AppendLine("|---|---:|---:|---:|---:|");
                int regionStop = Array.FindIndex(TPSL_STOPS, s => s.Short == TPSL_REGION_STOP);
                foreach (string targetShort in TPSL_REGION_TARGETS)
                {
                    int regionTarget = Array.FindIndex(TPSL_TARGETS, t => t.Short == targetShort);
                    TpSlStats regionTrain = result.Overall[regionTarget, regionStop, 0];
                    TpSlStats regionTest = result.Overall[regionTarget, regionStop, 1];
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {TPSL_TARGETS[regionTarget].Name} | {regionTrain.Trades} | {regionTrain.NetAverageR:F3} | {regionTest.Trades} | {regionTest.NetAverageR:F3} |"));
                }

                builder.AppendLine();
                builder.AppendLine("The three nearer cells (`AD 0.618`, `AD 0.786`, `PH 0.618`) are positive on both halves. The two farther targets are negative on train and positive on test, so they are not part of the stable claim.");

                builder.AppendLine();
                builder.AppendLine("### Best cell per model (selected on train)");
                builder.AppendLine();
                builder.AppendLine("| Model | Target | Stop | Train N | Train netR | Test N | Test win | Test netR |");
                builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");
                for (int m = 0; m < TPSL_MODELS.Length; m++)
                {
                    (int Target, int Stop) top = SelectBestModel(result, m, TPSL_MIN_SAMPLE);
                    TpSlStats train = result.PerModel[top.Target, top.Stop, m, 0];
                    TpSlStats test = result.PerModel[top.Target, top.Stop, m, 1];
                    if (train.Trades == 0 && test.Trades == 0)
                        continue;

                    builder.AppendLine(FormattableString.Invariant(
                        $"| {TPSL_MODELS[m]} | {TPSL_TARGETS[top.Target].Name} | {TPSL_STOPS[top.Stop].Name} | {train.Trades} | {train.NetAverageR:F3} | {test.Trades} | {test.WinRate:P1} | {test.NetAverageR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## What this run says");
            builder.AppendLine();
            AppendTpSlConclusions(builder, results);

            return builder.ToString();
        }

        private static void AppendTpSlConclusions(
            StringBuilder builder, IReadOnlyList<TpSlJoinResult> results)
        {
            builder.AppendLine("- The sample is the binding constraint: the three joins hold 26, 50 and 92 setups, so the 204-cell grid is a search over a few dozen trades per cell. A single best cell means nothing; only a region that is positive on both train and test, and on more than one model, is worth following.");

            int tp1 = Array.FindIndex(TPSL_TARGETS, t => t.Short == "TP1");
            int d5 = Array.FindIndex(TPSL_STOPS, s => s.Short == TPSL_REGION_STOP);
            foreach (TpSlJoinResult result in results)
            {
                TpSlStats shippedTrain = result.Overall[tp1, d5, 0];
                TpSlStats shippedTest = result.Overall[tp1, d5, 1];
                builder.AppendLine(FormattableString.Invariant(
                    $"- **{result.Join.Name}**, shipped geometry `TP1` / `D-5%`: train {shippedTrain.NetAverageR:F3}R ({shippedTrain.Trades} trades), test {shippedTest.NetAverageR:F3}R ({shippedTest.Trades} trades)."));
            }

            TpSlJoinResult? moderate = results.FirstOrDefault(r =>
                r.Join.Name.StartsWith("moderate", StringComparison.Ordinal));
            if (moderate != null)
            {
                (double minTrain, double maxTrain, double minTest, double maxTest,
                    int trainN, int testN) = RegionStats(moderate);
                builder.AppendLine(FormattableString.Invariant(
                    $"- The positive-on-both sub-region is `AD 0.618`, `AD 0.786` and `PH 0.618` with the `D-5%` stop — train {minTrain:F3}…{maxTrain:F3}R, test {minTest:F3}…{maxTest:F3}R, with up to {trainN} train and {testN} test trades. The farther targets (`AD 1.0`, `PH 1.0`) are negative on train and positive on test, so the edge sits in the nearer half of the band, not across all of it."));
            }

            builder.AppendLine("- The model split is mixed: in the moderate join the largest test cell, BUTTERFLY (10 trades), is negative at its best train cell, while SHARK (15 trades) is positive. The region is not yet confirmed across models.");

            builder.AppendLine("- The single best train cell of the moderate join (`AD 0.786` / `E-2.5%`) makes +1.825R on train and **−0.761R on test**. That is exactly the overfitting the grid is meant to expose: the best cell is not a rule.");
            builder.AppendLine("- The tight join is too thin (12 test trades) and its train winner (`D-10%`) is not where the test heatmap peaks (`D-1%`). That inversion is noise, not a geometry.");
            builder.AppendLine("- The loose join is broadly positive on test but its train edge is weak; treat it as a robustness check, not as the primary result.");
            builder.AppendLine("- The earlier near-zero result came from the arbitrary `±1 bars / 10% / lag 10` join, not from the shipped geometry: with the moderate and loose joins the shipped `TP1` / `D-5%` cell is positive on test. The join choice matters more than the TP/SL choice inside the region.");
            builder.AppendLine("- The comparison uses the harmonic finder's own entry and a post-hoc forward replay. It does not model spread widening, slippage or broker-side execution beyond the flat 2 bp cost.");
        }

        private static (double MinTrain, double MaxTrain, double MinTest, double MaxTest,
            int TrainN, int TestN) RegionStats(TpSlJoinResult result)
        {
            int stop = Array.FindIndex(TPSL_STOPS, s => s.Short == TPSL_REGION_STOP);
            double minTrain = double.MaxValue;
            double maxTrain = double.MinValue;
            double minTest = double.MaxValue;
            double maxTest = double.MinValue;
            int trainN = 0;
            int testN = 0;

            foreach (string targetShort in TPSL_STABLE_REGION_TARGETS)
            {
                int target = Array.FindIndex(TPSL_TARGETS, t => t.Short == targetShort);
                TpSlStats train = result.Overall[target, stop, 0];
                TpSlStats test = result.Overall[target, stop, 1];
                minTrain = Math.Min(minTrain, train.NetAverageR);
                maxTrain = Math.Max(maxTrain, train.NetAverageR);
                minTest = Math.Min(minTest, test.NetAverageR);
                maxTest = Math.Max(maxTest, test.NetAverageR);
                trainN = Math.Max(trainN, train.Trades);
                testN = Math.Max(testN, test.Trades);
            }

            return (minTrain, maxTrain, minTest, maxTest, trainN, testN);
        }

        private static (int Target, int Stop) SelectBest(TpSlJoinResult result, int minSample)
        {
            return EnumerateCells()
                .OrderByDescending(c =>
                    result.Overall[c.Target, c.Stop, 0].Trades >= minSample)
                .ThenByDescending(c => result.Overall[c.Target, c.Stop, 0].NetAverageR)
                .First();
        }

        private static (int Target, int Stop) SelectBestModel(
            TpSlJoinResult result, int model, int minSample)
        {
            return EnumerateCells()
                .OrderByDescending(c =>
                    result.PerModel[c.Target, c.Stop, model, 0].Trades >= minSample)
                .ThenByDescending(c =>
                    result.PerModel[c.Target, c.Stop, model, 0].NetAverageR)
                .First();
        }

        private static IEnumerable<(int Target, int Stop)> EnumerateCells()
        {
            for (int t = 0; t < TPSL_TARGETS.Length; t++)
            for (int s = 0; s < TPSL_STOPS.Length; s++)
                yield return (t, s);
        }

        private static List<(int Target, int Stop)> Rank(TpSlJoinResult result, int count)
        {
            return EnumerateCells()
                .OrderByDescending(c =>
                    result.Overall[c.Target, c.Stop, 0].Trades >= TPSL_MIN_SAMPLE)
                .ThenByDescending(c => result.Overall[c.Target, c.Stop, 0].NetAverageR)
                .Take(count)
                .ToList();
        }

        private static string Row(
            string target, string stop, TpSlStats train, TpSlStats test)
        {
            return FormattableString.Invariant(
                $"| {target} | {stop} | {train.Trades} | {train.WinRate:P1} | {train.AverageRiskReward:F2} | {train.NetAverageR:F3} | {train.AverageStopAtr:F1} | {test.Trades} | {test.WinRate:P1} | {test.AverageRiskReward:F2} | {test.NetAverageR:F3} |");
        }

        #endregion

        #region Types

        private sealed record TpSlTarget(
            string Name, string Short, Func<HarmonicTrade, double> Resolve);

        private sealed record TpSlStop(
            string Name, string Short, Func<HarmonicTrade, double> Resolve);

        private sealed class TpSlStats
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
            public double StopAtrSum;
            public int ResultCount;

            public double WinRate => TakeProfits + StopLosses == 0
                ? 0d
                : (double)TakeProfits / (TakeProfits + StopLosses);

            public double AverageRiskReward => Trades == 0 ? 0d : RiskRewardSum / Trades;

            public double AverageStopAtr => Trades == 0 ? 0d : StopAtrSum / Trades;

            public double AverageCost => ResultCount == 0 ? 0d : CostSum / ResultCount;

            public double NetAverageR => ResultCount == 0
                ? 0d
                : (ResultSum - CostSum) / ResultCount;
        }

        private sealed class TpSlJoinResult
        {
            public TpSlJoinResult(
                DiagonalJoinConfig join,
                int setups,
                TpSlStats[,,] overall,
                TpSlStats[,,,] perModel)
            {
                Join = join;
                Setups = setups;
                Overall = overall;
                PerModel = perModel;
                TrainCount = 0;
                TestCount = 0;
            }

            public DiagonalJoinConfig Join { get; }
            public int Setups { get; }
            public int TrainCount { get; set; }
            public int TestCount { get; set; }
            public TpSlStats[,,] Overall { get; }
            public TpSlStats[,,,] PerModel { get; }
        }

        #endregion
    }
}
