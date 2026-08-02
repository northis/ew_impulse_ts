using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Research harness that measures how far the price runs after a confirmed point D, in
    /// units of the A-D leg. Excluded from the normal test run; writes
    /// <c>reports/harmonic_excursion.md</c>.
    /// <para>
    /// Run: <c>dotnet test --filter "FullyQualifiedName~HarmonicExcursion"</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    [Explicit("Research harness - run manually to (re)generate reports/harmonic_excursion.md")]
    [Category("Research")]
    public class HarmonicExcursionTests
    {
        /// <summary>The cost of a round trip, as a fraction of the price.</summary>
        private const double COST_RATE = 0.0002d;

        /// <summary>The classic Fibonacci targets, as a fraction of the A-D leg.</summary>
        private static readonly double[] TARGETS =
            { 0.236d, 0.382d, 0.5d, 0.618d, 0.786d, 1d, 1.272d, 1.618d, 2d, 2.618d };

        /// <summary>The histogram edges, as a fraction of the A-D leg.</summary>
        private static readonly double[] EDGES =
        {
            0d, 0.1d, 0.2d, 0.382d, 0.5d, 0.618d, 0.786d, 1d, 1.272d, 1.618d, 2d, 3d
        };

        /// <summary>One setup, walked forward from the bar that confirmed it.</summary>
        private sealed record Excursion(
            HarmonicPatternType PatternType,
            string TimeFrame,
            double FromD,
            double FromDCapped,
            double EntryOffset,
            double Entry,
            double Risk,
            double PointD,
            double LegAd,
            int Bars,
            int Horizon,
            bool Stopped,
            bool StoppedInHorizon)
        {
            /// <summary>The best run measured from the entry instead of the point D.</summary>
            public double FromEntry => FromD - EntryOffset;

            /// <summary>The same, within the pattern's own duration.</summary>
            public double FromEntryCapped => FromDCapped - EntryOffset;
        }

        [Test]
        public void Excursion_WriteReport()
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

            var all = new List<Excursion>();
            long totalBars = 0;
            int fileCount = 0;

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                file =>
                {
                    TestBarsProvider provider = HarmonicCsvData.Load(file, false);
                    if (provider.Count < 1000)
                    {
                        TestContext.WriteLine($"SKIP (too short): {file}");
                        return;
                    }

                    List<Excursion> local = Run(provider, file.Contains("_h1_") ? "h1" : "m15");
                    lock (all)
                    {
                        all.AddRange(local);
                        totalBars += provider.Count;
                        fileCount++;
                    }

                    TestContext.WriteLine(
                        $"{file}: bars={provider.Count} setups={local.Count}");
                });

            Assert.That(fileCount, Is.GreaterThan(0), "No archive file was processed.");
            Assert.That(all, Is.Not.Empty, "The shipped configuration found no setup.");

            string reportsDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportsDir);
            string outPath = Path.Combine(reportsDir, "harmonic_excursion.md");
            File.WriteAllText(outPath, BuildReport(all, fileCount, totalBars));
            TestContext.WriteLine($"Wrote {outPath}");
        }

        /// <summary>
        /// Replays one file with the shipped settings and walks every setup forward until its
        /// stop is hit, recording the best price reached on the way.
        /// </summary>
        private static List<Excursion> Run(TestBarsProvider provider, string timeFrame)
        {
            var result = new List<Excursion>();
            var finder = new HarmonicSetupFinder(provider, provider.BarSymbol, new HarmonicParams());

            finder.OnEnter += (_, e) =>
            {
                HarmonicItem item = e.HarmonicItem;
                double legAd = Math.Abs(item.ItemA.Value - item.ItemD.Value);
                double entry = e.Level.Value;
                double risk = Math.Abs(entry - e.StopLoss.Value);
                if (legAd <= 0d || risk <= 0d)
                    return;

                double pointD = item.ItemD.Value;
                double stop = e.StopLoss.Value;
                double best = entry;
                double bestCapped = entry;
                int entryIndex = e.Level.BarIndex;

                // The pattern took this long to form; a target it only reaches long after is
                // not the target the pattern called.
                int horizon = entryIndex + item.LengthBars;
                int i = entryIndex + 1;
                bool stopped = false;

                for (; i < provider.Count; i++)
                {
                    // A bar that reaches both the stop and a new extreme is counted as the
                    // stop: the walk may not credit a run the trade was no longer in.
                    if (item.IsBull
                            ? provider.GetLowPrice(i) <= stop
                            : provider.GetHighPrice(i) >= stop)
                    {
                        stopped = true;
                        break;
                    }

                    best = item.IsBull
                        ? Math.Max(best, provider.GetHighPrice(i))
                        : Math.Min(best, provider.GetLowPrice(i));

                    if (i <= horizon)
                        bestCapped = best;
                }

                double sign = item.IsBull ? 1d : -1d;
                result.Add(new Excursion(
                    item.PatternType,
                    timeFrame,
                    sign * (best - pointD) / legAd,
                    sign * (bestCapped - pointD) / legAd,
                    sign * (entry - pointD) / legAd,
                    entry,
                    risk,
                    pointD,
                    sign * legAd,
                    i - entryIndex,
                    item.LengthBars,
                    stopped,
                    stopped && i <= horizon));
            };

            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            return result;
        }

        /// <summary>The value below which the given share of the sorted sample lies.</summary>
        private static double Percentile(IReadOnlyList<double> sorted, double share)
        {
            if (sorted.Count == 0)
                return 0d;

            int index = (int)Math.Round(share * (sorted.Count - 1), MidpointRounding.AwayFromZero);
            return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
        }

        private static string BuildReport(
            IReadOnlyList<Excursion> all, int fileCount, long totalBars)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Harmonic excursion in A-D units");
            builder.AppendLine();
            builder.AppendLine(Format("Generated: {0:yyyy-MM-dd HH:mm} UTC", DateTime.UtcNow));
            builder.AppendLine();
            builder.AppendLine(Format(
                "Archive files: {0}, bars: {1}, setups: {2}. Every setup the shipped settings " +
                "produce is walked forward from the bar that confirmed the point D until the " +
                "shipped stop is hit or the file ends, and the best price reached on the way is " +
                "measured in units of the A-D leg. A bar that reaches both the stop and a new " +
                "extreme counts as the stop.",
                fileCount, totalBars, all.Count));
            builder.AppendLine();

            var offsets = all.Select(a => a.EntryOffset).OrderBy(a => a).ToList();
            int unresolved = all.Count(a => !a.Stopped);
            builder.AppendLine(Format(
                "The entry is the close of the confirmation bar, and the shipped minimum stop " +
                "distance of 4 ATR keeps only the setups whose entry is already far from the " +
                "point D: the median entry sits {0:F3} A-D past it (p25 {1:F3}, p75 {2:F3}). " +
                "A target projected from D is that much closer than it looks, so the same run " +
                "is reported from both anchors below. {3} setups ({4:P1}) never hit their stop " +
                "before the end of their file.",
                Percentile(offsets, 0.5d), Percentile(offsets, 0.25d), Percentile(offsets, 0.75d),
                unresolved, (double)unresolved / all.Count));
            builder.AppendLine();
            builder.AppendLine(Format(
                "The walk has no time limit, so the `capped` figures repeat it over the " +
                "pattern's own X-D duration (median {0:F0} bars).",
                Percentile(all.Select(a => (double)a.Horizon).OrderBy(a => a).ToList(), 0.5d)));
            builder.AppendLine();

            Histogram(builder,
                "Distribution of the best run, measured from the point D",
                all, a => a.FromD);
            Histogram(builder,
                "Distribution of the best run, measured from the entry",
                all, a => a.FromEntry);
            Histogram(builder,
                "Distribution of the best run from the entry, within the X-D duration",
                all, a => a.FromEntryCapped);

            builder.AppendLine("## Percentiles");
            builder.AppendLine();
            builder.AppendLine("| Group | Setups | p10 | p25 | median | p75 | p90 | p95 | max |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            builder.AppendLine(Row("**All, from D**", all, a => a.FromD));
            builder.AppendLine(Row("**All, from entry**", all, a => a.FromEntry));
            builder.AppendLine(Row("**All, from entry, capped**", all, a => a.FromEntryCapped));
            foreach (IGrouping<HarmonicPatternType, Excursion> group in all
                         .GroupBy(a => a.PatternType)
                         .OrderBy(a => (int)a.Key))
            {
                builder.AppendLine(
                    Row($"{group.Key}, from entry", group.ToList(), a => a.FromEntry));
            }

            foreach (IGrouping<string, Excursion> group in all
                         .GroupBy(a => a.TimeFrame)
                         .OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                builder.AppendLine(
                    Row($"{group.Key}, from entry", group.ToList(), a => a.FromEntry));
            }

            builder.AppendLine();
            Targets(builder,
                "Classic targets projected from the point D",
                "The target price is `D + ratio * AD`. `Tradable` counts the setups the entry " +
                "has not already carried past the target; the rest of the row is measured on " +
                "those alone. `Net avg R` prices the target as the only exit: a reached target " +
                "pays its distance from the entry over the risk, a stop pays -1, and both are " +
                "charged the round trip.",
                all, a => a.FromD, a => a.FromDCapped, a => a.PointD);
            Targets(builder,
                "The same targets projected from the entry",
                "The target price is `entry + ratio * AD`, so every setup can trade every row.",
                all, a => a.FromEntry, a => a.FromEntryCapped, a => a.Entry);

            builder.AppendLine("## Bars held");
            builder.AppendLine();
            var bars = all.Select(a => (double)a.Bars).OrderBy(a => a).ToList();
            builder.AppendLine(Format(
                "From the confirmation bar to the stop: median {0:F0} bars, p75 {1:F0}, " +
                "p90 {2:F0}, p99 {3:F0}.",
                Percentile(bars, 0.5d), Percentile(bars, 0.75d),
                Percentile(bars, 0.9d), Percentile(bars, 0.99d)));

            return builder.ToString();

            static string Row(
                string name, IReadOnlyCollection<Excursion> group, Func<Excursion, double> value)
            {
                var values = group.Select(value).OrderBy(a => a).ToList();
                return Format(
                    "| {0} | {1} | {2:F3} | {3:F3} | {4:F3} | {5:F3} | {6:F3} | {7:F3} | {8:F2} |",
                    name, group.Count,
                    Percentile(values, 0.1d), Percentile(values, 0.25d),
                    Percentile(values, 0.5d), Percentile(values, 0.75d),
                    Percentile(values, 0.9d), Percentile(values, 0.95d),
                    values[^1]);
            }
        }

        private static void Histogram(
            StringBuilder builder,
            string title,
            IReadOnlyList<Excursion> all,
            Func<Excursion, double> value)
        {
            builder.AppendLine($"## {title}");
            builder.AppendLine();
            builder.AppendLine("| A-D reached | Setups | Share | At least this far |");
            builder.AppendLine("|---|---:|---:|---:|");

            int negative = all.Count(a => value(a) < 0d);
            if (negative > 0)
            {
                builder.AppendLine(Format(
                    "| below 0.000 | {0} | {1:P1} | 100.0 % |",
                    negative, (double)negative / all.Count));
            }

            for (int i = 0; i < EDGES.Length; i++)
            {
                double low = EDGES[i];
                bool last = i == EDGES.Length - 1;
                int count = last
                    ? all.Count(a => value(a) >= low)
                    : all.Count(a => value(a) >= low && value(a) < EDGES[i + 1]);
                int atLeast = all.Count(a => value(a) >= low);
                builder.AppendLine(Format(
                    "| {0} | {1} | {2:P1} | {3:P1} |",
                    last ? Format("{0:F3}+", low) : Format("{0:F3} - {1:F3}", low, EDGES[i + 1]),
                    count, (double)count / all.Count, (double)atLeast / all.Count));
            }

            builder.AppendLine();
        }

        /// <summary>
        /// Prices every classic target as if it were the only exit of the trade.
        /// </summary>
        private static void Targets(
            StringBuilder builder,
            string title,
            string note,
            IReadOnlyList<Excursion> all,
            Func<Excursion, double> reach,
            Func<Excursion, double> reachCapped,
            Func<Excursion, double> anchor)
        {
            builder.AppendLine($"## {title}");
            builder.AppendLine();
            builder.AppendLine(note);
            builder.AppendLine();
            builder.AppendLine(
                "| Target, A-D | Tradable | Reached | Net avg R | Net R | Reached capped | " +
                "Net avg R capped | R:R |");
            builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (double target in TARGETS)
            {
                int tradable = 0;
                int reached = 0;
                int closed = 0;
                int reachedCapped = 0;
                int closedCapped = 0;
                double sum = 0d;
                double sumCapped = 0d;
                double riskRewardSum = 0d;

                foreach (Excursion e in all)
                {
                    double distance = target * e.LegAd + anchor(e) - e.Entry;

                    // A target the entry has already passed cannot be traded at all.
                    if (e.LegAd > 0d ? distance <= 0d : distance >= 0d)
                        continue;

                    tradable++;
                    double riskReward = Math.Abs(distance) / e.Risk;
                    double cost = e.Entry * COST_RATE / e.Risk;
                    riskRewardSum += riskReward;

                    if (reach(e) >= target)
                    {
                        reached++;
                        closed++;
                        sum += riskReward - cost;
                    }
                    else if (e.Stopped)
                    {
                        closed++;
                        sum += -1d - cost;
                    }

                    if (reachCapped(e) >= target)
                    {
                        reachedCapped++;
                        closedCapped++;
                        sumCapped += riskReward - cost;
                    }
                    else if (e.StoppedInHorizon)
                    {
                        closedCapped++;
                        sumCapped += -1d - cost;
                    }
                }

                builder.AppendLine(Format(
                    "| {0:F3} | {1} | {2:P1} | {3:F3} | {4:F1} | {5:P1} | {6:F3} | {7:F2} |",
                    target,
                    tradable,
                    tradable == 0 ? 0d : (double)reached / tradable,
                    closed == 0 ? 0d : sum / closed,
                    sum,
                    tradable == 0 ? 0d : (double)reachedCapped / tradable,
                    closedCapped == 0 ? 0d : sumCapped / closedCapped,
                    tradable == 0 ? 0d : riskRewardSum / tradable));
            }

            builder.AppendLine();
        }

        private static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }
}
