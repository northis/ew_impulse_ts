using System.Diagnostics;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Research harness that asks how many signals the search settings buy and what the extra
    /// signals are worth. Every variant trades the shipped levels and differs only in how the
    /// patterns are looked for. Excluded from the normal test run; writes
    /// <c>reports/harmonic_search_sweep.md</c>.
    /// <para>
    /// Run: <c>dotnet test --filter "FullyQualifiedName~HarmonicSearchSweep"</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    [Explicit("Research harness - run manually to (re)generate reports/harmonic_search_sweep.md")]
    [Category("Research")]
    public class HarmonicSearchSweepTests
    {
        /// <summary>The cost of a round trip, as a fraction of the price.</summary>
        private const double COST_RATE = 0.0002d;

        private const int ATR_PERIODS = 14;
        private const int IN_SAMPLE = 0;
        private const int OUT_OF_SAMPLE = 1;
        private const int SPLITS = 2;

        /// <summary>
        /// The minimum stop distance filter, in average true ranges. It is applied to the
        /// finished trades instead of to the search, so a single run of a variant answers for
        /// every threshold at once.
        /// </summary>
        private static readonly double[] THRESHOLDS = { 0d, 3d, 3.5d, 4d, 5d };

        /// <summary>The index in <see cref="THRESHOLDS"/> of the threshold the library ships.</summary>
        private const int SHIPPED = 3;

        /// <summary>
        /// One factor at a time around the shipped search settings, plus two combinations.
        /// The baseline is what the library ships, so it already carries the wider
        /// <see cref="HarmonicParams.FibErrorPercent"/> the first run of this harness earned.
        /// </summary>
        private static readonly (string Name, Action<HarmonicParams> Apply)[] VARIANTS =
        {
            ("baseline", _ => { }),
            ("MinPivotPeriod 2", p => p.MinPivotPeriod = 2),
            ("MaxPivotPeriod 30", p => p.MaxPivotPeriod = 30),
            ("MaxPivotPeriod 40", p => p.MaxPivotPeriod = 40),
            ("MaxPivotPeriod 30, BarsDepth 1000", p =>
            {
                p.MaxPivotPeriod = 30;
                p.BarsDepth = 1000;
            }),
            ("MaxPivotPeriod 30, MinPivotPeriod 2", p =>
            {
                p.MaxPivotPeriod = 30;
                p.MinPivotPeriod = 2;
            }),
            ("FibErrorPercent 25", p => p.FibErrorPercent = 25d),
            ("MaxPivotPeriod 30, FibErrorPercent 25", p =>
            {
                p.MaxPivotPeriod = 30;
                p.FibErrorPercent = 25d;
            })
        };

        private sealed class Stats
        {
            public int Setups;
            public int TakeProfits;
            public int StopLosses;
            public double ResultSum;
            public double CostSum;
            public double RiskRewardSum;

            public int Closed => TakeProfits + StopLosses;
            public double WinRate => Closed == 0 ? 0d : (double)TakeProfits / Closed;
            public double NetR => ResultSum - CostSum;
            public double NetAverageR => Closed == 0 ? 0d : NetR / Closed;
            public double AverageRiskReward => Setups == 0 ? 0d : RiskRewardSum / Setups;

            public void Add(Stats other)
            {
                Setups += other.Setups;
                TakeProfits += other.TakeProfits;
                StopLosses += other.StopLosses;
                ResultSum += other.ResultSum;
                CostSum += other.CostSum;
                RiskRewardSum += other.RiskRewardSum;
            }
        }

        private sealed class Open
        {
            public int Split;
            public double RiskReward;
            public double Cost;
            public double AtrRatio;
        }

        [Test]
        public void SearchSweep_WriteReport()
        {
            string? repoRoot = HarmonicCsvData.FindRepoRoot();
            if (repoRoot == null)
            {
                Assert.Inconclusive("The local price archive was not found.");
                return;
            }

            string[] files = HarmonicCsvData.GetAllFiles()
                .Where(a => a.Contains("_h1_") || a.Contains("_m15_"))
                .ToArray();

            var totals = NewStats();
            var elapsed = new long[VARIANTS.Length];
            long totalBars = 0;
            int fileCount = 0;

            foreach (string file in files)
            {
                TestBarsProvider provider = HarmonicCsvData.Load(file, false);
                if (provider.Count < 1000)
                {
                    TestContext.WriteLine($"SKIP (too short): {file}");
                    continue;
                }

                fileCount++;
                totalBars += provider.Count;
                double[] atr = BuildAverageTrueRange(provider);

                var watch = Stopwatch.StartNew();
                Parallel.For(0, VARIANTS.Length, variant =>
                {
                    var start = Stopwatch.GetTimestamp();
                    Stats[,] local = Run(provider, atr, VARIANTS[variant].Apply);
                    long ms = (Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency;

                    lock (totals)
                    {
                        elapsed[variant] += ms;
                        for (int k = 0; k < THRESHOLDS.Length; k++)
                        for (int s = 0; s < SPLITS; s++)
                            totals[variant, k, s].Add(local[k, s]);
                    }
                });

                watch.Stop();
                TestContext.WriteLine(
                    $"{file}: bars={provider.Count} ms={watch.ElapsedMilliseconds}");
            }

            Assert.That(fileCount, Is.GreaterThan(0), "No archive file was processed.");

            string reportsDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportsDir);
            string outPath = Path.Combine(reportsDir, "harmonic_search_sweep.md");
            File.WriteAllText(outPath, BuildReport(totals, elapsed, fileCount, totalBars));
            TestContext.WriteLine($"Wrote {outPath}");
        }

        /// <summary>
        /// Replays one search variant over one file and buckets every trade by how many
        /// average true ranges its stop is away from the entry.
        /// </summary>
        private static Stats[,] Run(
            TestBarsProvider provider, double[] atr, Action<HarmonicParams> apply)
        {
            // The filter is applied to the results below, so the run itself must keep
            // everything the search finds.
            var parameters = new HarmonicParams { MinimumStopAtr = 0d };
            apply(parameters);

            Stats[,] stats = NewFileStats();
            var finder = new HarmonicSetupFinder(provider, provider.BarSymbol, parameters);
            var openMap = new Dictionary<object, Open>(ReferenceEqualityComparer.Instance);

            finder.OnEnter += (_, e) =>
            {
                double entry = e.Level.Value;
                double distance = Math.Abs(entry - e.StopLoss.Value);
                if (distance <= 0d)
                    return;

                double range = atr[e.Level.BarIndex];
                var open = new Open
                {
                    // Every file is cut in half, so both halves hold every symbol and model.
                    Split = 2 * e.Level.BarIndex < provider.Count ? IN_SAMPLE : OUT_OF_SAMPLE,
                    RiskReward = e.RiskReward,
                    Cost = entry * COST_RATE / distance,

                    // The finder skips the check when it has no range to compare against.
                    AtrRatio = range > 0d ? distance / range : double.MaxValue
                };

                openMap[e.Level] = open;
                for (int k = 0; k < THRESHOLDS.Length; k++)
                {
                    if (open.AtrRatio < THRESHOLDS[k])
                        continue;

                    stats[k, open.Split].Setups++;
                    stats[k, open.Split].RiskRewardSum += open.RiskReward;
                }
            };

            finder.OnTakeProfit += (_, e) => Close(e.FromLevel, true);
            finder.OnStopLoss += (_, e) => Close(e.FromLevel, false);

            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            return stats;

            void Close(object level, bool win)
            {
                if (!openMap.TryGetValue(level, out Open? open))
                    return;

                openMap.Remove(level);
                for (int k = 0; k < THRESHOLDS.Length; k++)
                {
                    if (open.AtrRatio < THRESHOLDS[k])
                        continue;

                    Stats cell = stats[k, open.Split];
                    cell.CostSum += open.Cost;
                    if (win)
                    {
                        cell.TakeProfits++;
                        cell.ResultSum += open.RiskReward;
                    }
                    else
                    {
                        cell.StopLosses++;
                        cell.ResultSum -= 1d;
                    }
                }
            }
        }

        /// <summary>
        /// The average true range of every bar. The first bars of a file have no history to
        /// average, so the range is taken over what there is, exactly as the level sweep does.
        /// </summary>
        private static double[] BuildAverageTrueRange(TestBarsProvider provider)
        {
            var trueRange = new double[provider.Count];
            for (int i = 1; i < provider.Count; i++)
            {
                double close = provider.GetClosePrice(i - 1);
                trueRange[i] = Math.Max(provider.GetHighPrice(i), close) -
                               Math.Min(provider.GetLowPrice(i), close);
            }

            var result = new double[provider.Count];
            double sum = 0d;
            for (int i = 1; i < provider.Count; i++)
            {
                sum += trueRange[i];
                if (i - ATR_PERIODS >= 1)
                    sum -= trueRange[i - ATR_PERIODS];

                int first = Math.Max(1, i - ATR_PERIODS + 1);
                result[i] = sum / (i - first + 1);
            }

            return result;
        }

        private static Stats[,,] NewStats()
        {
            var result = new Stats[VARIANTS.Length, THRESHOLDS.Length, SPLITS];
            for (int v = 0; v < VARIANTS.Length; v++)
            for (int k = 0; k < THRESHOLDS.Length; k++)
            for (int s = 0; s < SPLITS; s++)
                result[v, k, s] = new Stats();

            return result;
        }

        private static Stats[,] NewFileStats()
        {
            var result = new Stats[THRESHOLDS.Length, SPLITS];
            for (int k = 0; k < THRESHOLDS.Length; k++)
            for (int s = 0; s < SPLITS; s++)
                result[k, s] = new Stats();

            return result;
        }

        private static string BuildReport(
            Stats[,,] totals, long[] elapsed, int fileCount, long totalBars)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Harmonic search parameter sweep");
            builder.AppendLine();
            builder.AppendLine(Format("Generated: {0:yyyy-MM-dd HH:mm} UTC", DateTime.UtcNow));
            builder.AppendLine();
            builder.AppendLine(Format(
                "Archive files: {0}, bars: {1}. Every variant trades the levels the library " +
                "ships and differs only in how the patterns are searched for. A round trip " +
                "costs {2:F2} bp of the price and is charged to every closed trade.",
                fileCount, totalBars, COST_RATE * 10000d));
            builder.AppendLine();
            builder.AppendLine(Format(
                "The minimum stop distance filter is applied to the finished trades instead " +
                "of to the search, so one run of a variant answers for every threshold. The " +
                "shipped threshold is {0:F1} ATR.", THRESHOLDS[SHIPPED]));
            builder.AppendLine();
            builder.AppendLine(
                "Every file is cut in half by time. `IS` is the first half, on which the " +
                "shipped configuration was chosen; `OOS` is the second one. The search " +
                "settings below were never fitted on either half, so both columns are honest " +
                "here - they are shown apart only to keep them comparable with the level sweep.");
            builder.AppendLine();

            builder.AppendLine(Format(
                "## Signals at the shipped {0:F1} ATR filter", THRESHOLDS[SHIPPED]));
            builder.AppendLine();
            builder.AppendLine(
                "| Variant | Setups | IS trades | IS net avg R | OOS trades | OOS win rate | " +
                "OOS R:R | OOS net avg R | OOS net R | s |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            for (int v = 0; v < VARIANTS.Length; v++)
            {
                Stats inSample = totals[v, SHIPPED, IN_SAMPLE];
                Stats outSample = totals[v, SHIPPED, OUT_OF_SAMPLE];
                builder.AppendLine(Format(
                    "| {0} | {1} | {2} | {3:F3} | {4} | {5:P1} | {6:F2} | {7:F3} | {8:F1} | {9} |",
                    VARIANTS[v].Name, inSample.Setups + outSample.Setups, inSample.Closed,
                    inSample.NetAverageR, outSample.Closed, outSample.WinRate,
                    outSample.AverageRiskReward, outSample.NetAverageR, outSample.NetR,
                    elapsed[v] / 1000));
            }

            builder.AppendLine();
            builder.AppendLine("## Closed trades by minimum stop distance");
            builder.AppendLine();
            builder.AppendLine(Header("Variant"));
            builder.AppendLine(Divider());
            for (int v = 0; v < VARIANTS.Length; v++)
            {
                builder.Append(Format("| {0} |", VARIANTS[v].Name));
                for (int k = 0; k < THRESHOLDS.Length; k++)
                {
                    builder.Append(Format(" {0} |",
                        totals[v, k, IN_SAMPLE].Closed + totals[v, k, OUT_OF_SAMPLE].Closed));
                }

                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("## Out-of-sample net R per trade by minimum stop distance");
            builder.AppendLine();
            builder.AppendLine(Header("Variant"));
            builder.AppendLine(Divider());
            for (int v = 0; v < VARIANTS.Length; v++)
            {
                builder.Append(Format("| {0} |", VARIANTS[v].Name));
                for (int k = 0; k < THRESHOLDS.Length; k++)
                    builder.Append(Format(" {0:F3} |", totals[v, k, OUT_OF_SAMPLE].NetAverageR));

                builder.AppendLine();
            }

            return builder.ToString();

            static string Header(string first)
            {
                var line = new StringBuilder(Format("| {0} |", first));
                foreach (double threshold in THRESHOLDS)
                    line.Append(Format(" {0:F1} ATR |", threshold));

                return line.ToString();
            }

            static string Divider()
            {
                var line = new StringBuilder("|---|");
                foreach (double _ in THRESHOLDS)
                    line.Append("---:|");

                return line.ToString();
            }
        }

        private static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }
}
