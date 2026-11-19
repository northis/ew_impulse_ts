using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness: what diagonal form and what impulse-A quality separate the
    /// reference USDCHF m15 2018-08-03 setup (DIAGONAL.md §9.44) from the rest?
    /// The diagonal finder is replayed once (<c>ALL</c> mode); every shape/impulse filter is
    /// applied post-hoc, which is causal (the diagonal shape is known at the signal bar and
    /// the A leg is complete before the entry at <c>EnterRatio</c> of A). The impulse filters
    /// are also tested as real simulator arguments (<c>MinABars</c>, <c>MinARatio</c>),
    /// where a rejected A lets the simulator pick the next candidate.
    /// Run manually: <c>dotnet test --filter "FullyQualifiedName~DiagonalIdealFilter"</c>.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string IDEAL_FILE =
            "USDCHF_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv";

        private static readonly DateTime IDEAL_SIGNAL = new(2018, 8, 3, 15, 45, 0);

        private static readonly IdealTradeSpec[] IDEAL_SPECS =
        {
            new("trigger/0.50/L1.0-1.272", DiagonalAbcExecution.TRIGGER, 0.5d, true, 1.0d, 1.272d),
            new("trigger/0.50/TP1.0", DiagonalAbcExecution.TRIGGER, 0.5d, false, 1.0d, 1.0d),
            new("trigger/0.50/TP1.272", DiagonalAbcExecution.TRIGGER, 0.5d, false, 1.272d, 1.272d),
            new("trigger/0.382/L1.0-1.272", DiagonalAbcExecution.TRIGGER, 0.382d, true, 1.0d, 1.272d),
            new("trigger/0.618/L1.0-1.272", DiagonalAbcExecution.TRIGGER, 0.618d, true, 1.0d, 1.272d),
            new("nextopen/0.50/L1.0-1.272", DiagonalAbcExecution.NEXT_OPEN, 0.5d, true, 1.0d, 1.272d),
            new("nextopen/0.50/TP1.0", DiagonalAbcExecution.NEXT_OPEN, 0.5d, false, 1.0d, 1.0d),
            new("nextopen/0.50/TP1.272", DiagonalAbcExecution.NEXT_OPEN, 0.5d, false, 1.272d, 1.272d),
            new("nextopen/0.382/TP1.0", DiagonalAbcExecution.NEXT_OPEN, 0.382d, false, 1.0d, 1.0d)
        };

        private static readonly IdealSimSpec[] IDEAL_SIMS =
        {
            new(3, 0.236d),
            new(10, 0.236d),
            new(10, 0.236d, MaxABars: 40),
            new(10, 0.236d, RequireSmoothA: true),
            new(10, 0.236d, MaxABars: 40, RequireSmoothA: true),
            new(3, 0.236d, MaxABars: 40),
            new(3, 0.236d, RequireSmoothA: true),
            new(6, 0.236d, MaxABars: 40)
        };

        [Test]
        [Explicit("Research harness - diagonal form + impulse-A filters on the reference setup")]
        [Category("Research")]
        public void DiagonalIdealFilter_Sweep()
        {
            string dataDir = FindDataDir();
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(dataDir, IDEAL_FILE));

            int impulsePeriod = AutoPeriodEstimator.EstimateImpulsePeriod(provider);
            DiagonalAbcPivotTimeline timeline =
                DiagonalAbcPivotTimeline.Build(provider, impulsePeriod);

            List<Sample> samples = RunDiagonalSamples(
                provider, IDEAL_FILE, EXACT_CORRIDOR);
            var seen = new HashSet<(int, int, bool)>();
            samples = samples.Where(s => seen.Add((s.P0Bar, s.SignalBar, s.IsUpDiagonal)))
                .ToList();

            var anchors = new List<IdealAnchor>(samples.Count);
            foreach (Sample sample in samples)
            {
                var setup = new DiagonalAbcSetup(
                    sample.IsUpDiagonal, sample.SignalBar, sample.EntryTime,
                    sample.P0Bar, sample.V0, sample.P4Bar, sample.V4,
                    sample.P5Bar, sample.V5, sample.W3);

                var sims = new List<IdealSim>(IDEAL_SIMS.Length);
                foreach (IdealSimSpec sim in IDEAL_SIMS)
                {
                    var attempts = new List<IdealAttempt>(IDEAL_SPECS.Length);
                    ALegMetrics? aLeg = null;

                    foreach (IdealTradeSpec spec in IDEAL_SPECS)
                    {
                        DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                            provider, setup,
                            BuildAbcParams(sim, spec),
                            timeline);

                        if (aLeg == null && trade.AStartBar >= 0)
                            aLeg = MeasureALeg(provider, trade);

                        attempts.Add(new IdealAttempt(spec, trade));
                    }

                    sims.Add(new IdealSim(sim, aLeg, attempts));
                }

                anchors.Add(new IdealAnchor(sample, sims));
            }

            Assert.That(anchors, Is.Not.Empty, "No diagonal anchors were found.");
            TestContext.Out.WriteLine($"anchors={anchors.Count}");

            IdealAnchor? ideal = anchors.FirstOrDefault(a => a.Sample.EntryTime == IDEAL_SIGNAL);
            TestContext.Out.WriteLine(ideal == null
                ? "REFERENCE SETUP NOT FOUND"
                : "REFERENCE FOUND " + DescribeIdeal(ideal));

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);
            string reportPath = Path.Combine(reportDir, "diagonal_ideal_filter_sweep.md");
            File.WriteAllText(reportPath, BuildIdealSweepReport(anchors, ideal));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            foreach (IdealSimSpec sim in IDEAL_SIMS)
            {
                IdealStats stats = IdealStat(Slice(anchors, filter: null, sim, IDEAL_SPECS[0]));
                TestContext.Out.WriteLine(
                    $"{sim.Name,-18} N={stats.N,4} res={stats.Resolved,4} " +
                    $"win={stats.Win,6:P1} netR={stats.NetR,7:F3} 5bp={stats.StressNetR,7:F3}");
            }
        }

        private static DiagonalAbcParams BuildAbcParams(
            IdealSimSpec sim,
            IdealTradeSpec spec)
        {
            return new DiagonalAbcParams(
                EnterRatio: spec.EnterRatio,
                MinARatio: sim.MinARatio,
                MinABars: sim.MinABars,
                MaxABars: sim.MaxABars,
                RequireSmoothA: sim.RequireSmoothA,
                AStartToleranceW3: 0.10d,
                AStartBarTolerance: 2,
                StopBuffer: 0d,
                Target1Ratio: spec.Target1,
                Ladder: spec.Ladder,
                Target2Ratio: spec.Target2,
                CostRate: 0.0002d,
                StressCostRate: 0.0005d,
                Execution: spec.Execution,
                MaxWaitBars: 200,
                MaxHoldBars: 400);
        }

        #region Filters

        private static List<IdealFilter> BuildIdealFilters()
        {
            return new List<IdealFilter>
            {
                new("base", (_, _) => true),

                // Diagonal form, known at the signal bar.
                new("A234<=0.005 (inside wedge)", (a, _) => a.Sample.A234 <= 0.005d),
                new("A5Early<=0.005 (no throw-over)", (a, _) => a.Sample.A5Early <= 0.005d),
                new("A5Early>=0.005 (throw-over)", (a, _) => a.Sample.A5Early >= 0.005d),
                new("W4W3<=0.236", (a, _) => a.Sample.W4W3 <= 0.236d),
                new("W4W3<=0.30", (a, _) => a.Sample.W4W3 <= 0.30d),
                new("W2Retr>=0.618", (a, _) => a.Sample.W2Retr >= 0.618d),
                new("W3W1<=0.786", (a, _) => a.Sample.W3W1 <= 0.786d),
                new("W3W1>=0.854", (a, _) => a.Sample.W3W1 >= 0.854d),
                new("W5W3<=0.5", (a, _) => a.Sample.W5W3 <= 0.5d),
                new("BarsW3<=BarsW1", (a, _) => a.Sample.BarsW3 <= a.Sample.BarsW1),
                new("proper wedge (W4<=0.236 & W2>=0.618)", (a, _) =>
                    a.Sample.W4W3 <= 0.236d && a.Sample.W2Retr >= 0.618d),

                // Impulse A quality, known before the entry.
                new("A smooth", (_, s) => s.ALeg != null && s.ALeg.Smooth),
                new("A bars>=6", (_, s) => s.ALeg != null && s.ALeg.Bars >= 6),
                new("A bars>=10", (_, s) => s.ALeg != null && s.ALeg.Bars >= 10),
                new("A bars 10..40", (_, s) =>
                    s.ALeg != null && s.ALeg.Bars is >= 10 and <= 40),
                new("A h/D>=0.382", (_, s) => s.ALeg != null && s.ALeg.ToDiagonal >= 0.382d),
                new("A h/D>=0.500", (_, s) => s.ALeg != null && s.ALeg.ToDiagonal >= 0.5d),
                new("A smooth & h/D>=0.382", (_, s) =>
                    s.ALeg != null && s.ALeg.Smooth && s.ALeg.ToDiagonal >= 0.382d),
                new("A smooth & h/D>=0.500", (_, s) =>
                    s.ALeg != null && s.ALeg.Smooth && s.ALeg.ToDiagonal >= 0.5d),
                new("A bars>=10 & h/D>=0.382", (_, s) =>
                    s.ALeg != null && s.ALeg.Bars >= 10 && s.ALeg.ToDiagonal >= 0.382d),
                new("A bars>=10 & h/D>=0.500", (_, s) =>
                    s.ALeg != null && s.ALeg.Bars >= 10 && s.ALeg.ToDiagonal >= 0.5d),
                new("A bars>=10 & smooth", (_, s) =>
                    s.ALeg != null && s.ALeg.Bars >= 10 && s.ALeg.Smooth),
                new("A bars>=10 & W4W3<=0.30", (a, s) =>
                    s.ALeg != null && s.ALeg.Bars >= 10 && a.Sample.W4W3 <= 0.30d),
                new("A bars>=10 & W2Retr>=0.618", (a, s) =>
                    s.ALeg != null && s.ALeg.Bars >= 10 && a.Sample.W2Retr >= 0.618d),
                new("A bars>=10 & W5W3<=0.5", (a, s) =>
                    s.ALeg != null && s.ALeg.Bars >= 10 && a.Sample.W5W3 <= 0.5d)
            };
        }

        #endregion

        #region A-leg metrics

        private static ALegMetrics MeasureALeg(TestBarsProvider provider, DiagonalAbcTrade trade)
        {
            int from = trade.AStartBar;
            int to = trade.AEndBar;
            bool isUp = trade.AEnd > trade.AStart;
            double total = Math.Abs(trade.AEnd - trade.AStart);

            double running = trade.AStart;
            double maxDrawdown = 0;
            double path = 0;
            double prev = trade.AStart;
            int counter = 0;
            int steps = 0;

            for (int i = from; i <= to; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);

                if (isUp)
                {
                    running = Math.Max(running, high);
                    maxDrawdown = Math.Max(maxDrawdown, running - low);
                }
                else
                {
                    running = Math.Min(running, low);
                    maxDrawdown = Math.Max(maxDrawdown, high - running);
                }

                if (i > from)
                {
                    double close = i == to ? trade.AEnd : provider.GetClosePrice(i);
                    double move = close - prev;
                    if (isUp ? move < 0 : move > 0)
                        counter++;
                    steps++;
                    path += Math.Abs(move);
                    prev = close;
                }
            }

            bool smooth = SmoothImpulseClassifier.IsSmoothImpulse(
                new BarPoint(trade.AStart, from, provider),
                new BarPoint(trade.AEnd, to, provider),
                provider);

            return new ALegMetrics(
                Bars: to - from,
                MaxCorrection: total > 1e-12 ? maxDrawdown / total : 1,
                PathEfficiency: total > 1e-12 ? path / total : double.MaxValue,
                CounterShare: steps > 0 ? counter / (double)steps : 1,
                ToDiagonal: trade.DiagonalHeight > 1e-12
                    ? trade.AHeight / trade.DiagonalHeight
                    : 0,
                Smooth: smooth);
        }

        #endregion

        #region Report

        private static List<IdealAttempt> Slice(
            IReadOnlyList<IdealAnchor> anchors,
            IdealFilter? filter,
            IdealSimSpec sim,
            IdealTradeSpec spec)
        {
            return anchors
                .Where(a => filter == null ||
                            filter.Predicate(a, a.Sims.First(s => s.Spec == sim)))
                .SelectMany(a => a.Sims.First(s => s.Spec == sim).Attempts)
                .Where(x => x.Spec.Name == spec.Name)
                .ToList();
        }

        private static IdealStats IdealStat(IReadOnlyList<IdealAttempt> attempts)
        {
            return IdealStatOfTrades(attempts.Select(x => x.Trade).ToList());
        }

        private static IdealStats IdealStatOfTrades(IReadOnlyList<DiagonalAbcTrade> trades)
        {
            List<DiagonalAbcTrade> filled = trades
                .Where(x => x.EntryBar >= 0)
                .ToList();
            List<DiagonalAbcTrade> resolved = filled
                .Where(x => x.Resolved)
                .ToList();

            return new IdealStats(
                trades.Count,
                filled.Count,
                resolved.Count,
                trades.Count(x => x.Outcome == "SKIP"),
                resolved.Count == 0 ? 0 : resolved.Count(x => x.NetR > 0) / (double)resolved.Count,
                resolved.Count == 0 ? 0 : resolved.Average(x => x.NetR),
                resolved.Count == 0 ? 0 : resolved.Average(x => x.StressNetR),
                filled.Count == 0 ? 0 : filled.Average(x => x.MfeR),
                filled.Count == 0 ? 0 : filled.Average(x => x.MaeR));
        }

        private static string DescribeIdeal(IdealAnchor anchor)
        {
            Sample s = anchor.Sample;
            IdealSim sim = anchor.Sims[0];
            string a = sim.ALeg == null
                ? "no A"
                : string.Format(CultureInfo.InvariantCulture,
                    "A bars={0} maxCorr={1:F3} pathEff={2:F3} counterShare={3:P1} h/D={4:F3} smooth={5}",
                    sim.ALeg.Bars, sim.ALeg.MaxCorrection,
                    sim.ALeg.PathEfficiency, sim.ALeg.CounterShare,
                    sim.ALeg.ToDiagonal, sim.ALeg.Smooth);
            DiagonalAbcTrade primary = sim.Attempts[0].Trade;
            return string.Format(CultureInfo.InvariantCulture,
                "signal={0:yyyy-MM-dd HH:mm} up={1} C={2:F3} A234={3:F4} A5Early={4:F4} " +
                "W2Retr={5:F3} W4W3={6:F3} W3W1={7:F3} W5W3={8:F3} " +
                "bars={9}/{10}/{11}/{12}/{13} | {14} | primary outcome={15} netR={16:F3}",
                s.EntryTime, s.IsUpDiagonal, s.C, s.A234, s.A5Early,
                s.W2Retr, s.W4W3, s.W3W1, s.W5W3,
                s.BarsW1, s.BarsW2, s.BarsW3, s.BarsW4, s.BarsW5,
                a, primary.Outcome, primary.NetR);
        }

        private static string BuildIdealSweepReport(
            IReadOnlyList<IdealAnchor> anchors,
            IdealAnchor? ideal)
        {
            List<IdealFilter> filters = BuildIdealFilters();
            IdealTradeSpec primary = IDEAL_SPECS[0];
            var builder = new StringBuilder();

            builder.AppendLine("# Diagonal form + impulse-A quality — reference USDCHF m15 2018-08-03");
            builder.AppendLine();
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Generated: {0:yyyy-MM-dd HH:mm} UTC. File `{1}`.",
                DateTime.UtcNow, IDEAL_FILE));
            builder.AppendLine();
            builder.AppendLine(
                "The reference setup: down diagonal 07:00–15:45, impulse A against it " +
                "15:45–20:45, B = 50 %, long C. All filters are causal: the diagonal shape " +
                "is known at the signal bar, the A leg before the entry at `EnterRatio` of A. " +
                "Costs 2 bp (5 bp stress).");
            builder.AppendLine();
            builder.AppendLine("## Reference setup");
            builder.AppendLine();
            builder.AppendLine(ideal == null
                ? "**Not found.**"
                : "```\n" + DescribeIdeal(ideal) + "\n```");

            builder.AppendLine();
            builder.AppendLine("## Simulator impulse filters — primary cell, no post-hoc filter");
            builder.AppendLine();
            builder.AppendLine("`MinABars` / `MaxABars` / `RequireSmoothA` are real simulator " +
                               "arguments: a rejected A lets the next candidate be tried.");
            builder.AppendLine();
            builder.AppendLine("| Sim | N | Filled | Resolved | win | netR | netR 5bp |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (IdealSimSpec sim in IDEAL_SIMS)
            {
                IdealStats stats = IdealStat(Slice(anchors, null, sim, primary));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2} | {3} | {4:P1} | {5:F3} | {6:F3} |",
                    sim.Name, stats.N, stats.Filled, stats.Resolved,
                    stats.Win, stats.NetR, stats.StressNetR));
            }

            builder.AppendLine();
            builder.AppendLine("## Post-hoc filters — primary cell");
            builder.AppendLine();
            builder.AppendLine("| Sim | Filter | N | Resolved | win | netR | netR 5bp |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (IdealSimSpec sim in new[] { IDEAL_SIMS[0], new IdealSimSpec(10, 0.236d) })
            {
                foreach (IdealFilter filter in filters)
                {
                    IdealStats stats = IdealStat(Slice(anchors, filter, sim, primary));
                    if (stats.Resolved == 0 && filter.Name != "base")
                        continue;
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "| {0} | {1} | {2} | {3} | {4:P1} | {5:F3} | {6:F3} |",
                        sim.Name, filter.Name, stats.N, stats.Resolved,
                        stats.Win, stats.NetR, stats.StressNetR));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Best cells — at least 25 resolved");
            builder.AppendLine();
            builder.AppendLine("| Sim | Filter | Cell | N | Resolved | win | netR | netR 5bp |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");
            var cells = new List<(IdealSimSpec Sim, IdealFilter Filter, IdealTradeSpec Spec, IdealStats Stats)>();
            foreach (IdealSimSpec sim in IDEAL_SIMS)
            {
                foreach (IdealFilter filter in filters)
                {
                    foreach (IdealTradeSpec spec in IDEAL_SPECS)
                    {
                        IdealStats stats = IdealStat(Slice(anchors, filter, sim, spec));
                        if (stats.Resolved >= 25)
                            cells.Add((sim, filter, spec, stats));
                    }
                }
            }

            foreach ((IdealSimSpec sim, IdealFilter filter, IdealTradeSpec spec, IdealStats stats)
                     in cells.OrderByDescending(c => c.Stats.NetR).Take(40))
            {
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2} | {3} | {4} | {5:P1} | {6:F3} | {7:F3} |",
                    sim.Name, filter.Name, spec.Name, stats.N, stats.Resolved,
                    stats.Win, stats.NetR, stats.StressNetR));
            }

            builder.AppendLine();
            builder.AppendLine("## Time split — primary cell, `MinABars=10 / MinARatio=0.236`");
            builder.AppendLine();
            builder.AppendLine("| Filter | Period | N | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            var focusSim = new IdealSimSpec(10, 0.236d);
            foreach (IdealFilter filter in filters)
            {
                foreach ((string name, int from, int to) in new[]
                         {
                             ("2018", 2018, 2018),
                             ("2019–2022", 2019, 2022),
                             ("2023–2026", 2023, 9999)
                         })
                {
                    List<IdealAttempt> slice = Slice(anchors, filter, focusSim, primary)
                        .Where(x => IdealYearOf(x.Trade) >= from && IdealYearOf(x.Trade) <= to)
                        .ToList();
                    if (slice.Count == 0)
                        continue;
                    IdealStats stats = IdealStat(slice);
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "| {0} | {1} | {2} | {3:P1} | {4:F3} |",
                        filter.Name, name, stats.N, stats.Win, stats.NetR));
                }
            }

            return builder.ToString();
        }

        private static int IdealYearOf(DiagonalAbcTrade trade)
        {
            return trade.EntryBar >= 0 ? trade.EntryTime.Year : trade.SignalTime.Year;
        }

        #endregion

        #region Types

        private sealed record IdealTradeSpec(
            string Name,
            DiagonalAbcExecution Execution,
            double EnterRatio,
            bool Ladder,
            double Target1,
            double Target2);

        private sealed record IdealSimSpec(
            int MinABars,
            double MinARatio,
            int MaxABars = 0,
            bool RequireSmoothA = false)
        {
            public string Name =>
                $"{MinABars}/{MinARatio:F3}" +
                (MaxABars > 0 ? $"/max{MaxABars}" : string.Empty) +
                (RequireSmoothA ? "/smooth" : string.Empty);
        }

        private sealed record ALegMetrics(
            int Bars,
            double MaxCorrection,
            double PathEfficiency,
            double CounterShare,
            double ToDiagonal,
            bool Smooth);

        private sealed record IdealAttempt(IdealTradeSpec Spec, DiagonalAbcTrade Trade);

        private sealed record IdealSim(
            IdealSimSpec Spec,
            ALegMetrics? ALeg,
            IReadOnlyList<IdealAttempt> Attempts);

        private sealed record IdealAnchor(Sample Sample, IReadOnlyList<IdealSim> Sims);

        private sealed record IdealFilter(
            string Name,
            Func<IdealAnchor, IdealSim, bool> Predicate);

        private sealed record IdealStats(
            int N,
            int Filled,
            int Resolved,
            int Skipped,
            double Win,
            double NetR,
            double StressNetR,
            double Mfe,
            double Mae);

        #endregion
    }
}
