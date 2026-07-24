using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Stage 6 checks: structural invariants of <see cref="HarmonicSetupFinder"/> on the real
    /// price archive under <c>data/</c>. Exact pattern counts are deliberately not asserted -
    /// they belong to a separate golden set.
    /// </summary>
    [TestFixture]
    public class HarmonicCsvTests
    {
        private const int CSV_MAX_BARS = 10_000;
        private const double PRICE_TOLERANCE = 1e-9;

        private sealed class RunResult
        {
            public List<HarmonicSignalEventArgs> Enters { get; } = new();
            public List<(string Kind, LevelEventArgs Args)> Closes { get; } = new();
            public int MaxCandidates { get; set; }
        }

        private static RunResult Run(TestBarsProvider provider, int bars, HarmonicParams parameters)
        {
            var finder = new HarmonicSetupFinder(provider, provider.BarSymbol, parameters);
            var result = new RunResult();

            finder.OnEnter += (_, e) => result.Enters.Add(e);
            finder.OnTakeProfit += (_, e) => result.Closes.Add(("TP", e));
            finder.OnStopLoss += (_, e) => result.Closes.Add(("SL", e));

            for (int i = 0; i < bars; i++)
            {
                finder.CheckBar(provider.GetOpenTime(i));
                result.MaxCandidates = Math.Max(result.MaxCandidates, finder.CandidateCount);
            }

            return result;
        }

        private static string Describe(RunResult run)
        {
            IEnumerable<string> enters = run.Enters.Select(a =>
                $"E {a.HarmonicItem.PatternType} {a.HarmonicItem.IsBull} " +
                $"{a.HarmonicItem.ItemX.BarIndex} {a.HarmonicItem.ItemA.BarIndex} " +
                $"{a.HarmonicItem.ItemB.BarIndex} {a.HarmonicItem.ItemC.BarIndex} " +
                $"{a.HarmonicItem.ItemD.BarIndex} {a.Level.Value:R} " +
                $"{a.TakeProfit.Value:R} {a.StopLoss.Value:R} {a.HarmonicItem.Score.Total:R}");

            IEnumerable<string> closes = run.Closes.Select(a =>
                $"{a.Kind} {a.Args.Level.BarIndex} {a.Args.Level.Value:R} " +
                $"{a.Args.FromLevel.BarIndex}");

            return string.Join("\n", enters.Concat(closes));
        }

        [TestCaseSource(typeof(HarmonicCsvData), nameof(HarmonicCsvData.CiFiles))]
        public void RealData_SatisfiesTheStructuralInvariants(string fileName)
        {
            TestBarsProvider provider = HarmonicCsvData.Load(fileName);
            int bars = Math.Min(provider.Count, CSV_MAX_BARS);

            Assert.That(bars, Is.GreaterThan(1000), $"{fileName} is too short.");
            for (int i = 1; i < bars; i++)
            {
                Assert.That(provider.GetOpenTime(i), Is.GreaterThan(provider.GetOpenTime(i - 1)),
                    $"{fileName}: the series is not chronologically ordered at bar {i}.");
            }

            var parameters = new HarmonicParams { MinimumScore = 0.5d, MinimumRiskReward = 0.3d };
            RunResult run = Run(provider, bars, parameters);

            var enterKeys = new HashSet<string>();
            foreach (HarmonicSignalEventArgs args in run.Enters)
            {
                HarmonicItem item = args.HarmonicItem;

                Assert.Multiple(() =>
                {
                    Assert.That(item.ItemX.BarIndex, Is.LessThan(item.ItemA.BarIndex));
                    Assert.That(item.ItemA.BarIndex, Is.LessThan(item.ItemB.BarIndex));
                    Assert.That(item.ItemB.BarIndex, Is.LessThan(item.ItemC.BarIndex));
                    Assert.That(item.ItemC.BarIndex, Is.LessThan(item.ItemD.BarIndex));
                });

                // In a bullish figure X, B and D are lows and A, C are highs; a bearish one
                // is mirrored.
                AssertPointOnCandle(provider, item.ItemX, item.IsBull, fileName);
                AssertPointOnCandle(provider, item.ItemA, !item.IsBull, fileName);
                AssertPointOnCandle(provider, item.ItemB, item.IsBull, fileName);
                AssertPointOnCandle(provider, item.ItemC, !item.IsBull, fileName);
                AssertPointOnCandle(provider, item.ItemD, item.IsBull, fileName);

                AssertRatios(item, parameters, fileName);

                Assert.Multiple(() =>
                {
                    Assert.That(args.Level.BarIndex,
                        Is.EqualTo(item.ItemD.BarIndex + parameters.DConfirmationBars),
                        $"{fileName}: the entry bar must be the point D confirmation bar.");
                    Assert.That(args.Level.Value,
                        Is.EqualTo(provider.GetClosePrice(args.Level.BarIndex)).Within(PRICE_TOLERANCE),
                        $"{fileName}: the entry must be the close of the confirmation bar.");

                    Assert.That(IsFinite(args.Level.Value), Is.True);
                    Assert.That(IsFinite(args.TakeProfit.Value), Is.True);
                    Assert.That(IsFinite(args.StopLoss.Value), Is.True);
                    Assert.That(IsFinite(item.Score.Total), Is.True);

                    if (item.IsBull)
                    {
                        Assert.That(args.TakeProfit.Value, Is.GreaterThan(args.Level.Value));
                        Assert.That(args.StopLoss.Value, Is.LessThan(args.Level.Value));
                    }
                    else
                    {
                        Assert.That(args.TakeProfit.Value, Is.LessThan(args.Level.Value));
                        Assert.That(args.StopLoss.Value, Is.GreaterThan(args.Level.Value));
                    }

                    Assert.That(item.Score.Total, Is.GreaterThanOrEqualTo(parameters.MinimumScore));
                    Assert.That(args.RiskReward,
                        Is.GreaterThanOrEqualTo(parameters.MinimumRiskReward));
                });

                string key = $"{item.PatternType}|{item.IsBull}|{item.ItemX.BarIndex}|" +
                             $"{item.ItemA.BarIndex}|{item.ItemB.BarIndex}|{item.ItemC.BarIndex}";
                Assert.That(enterKeys.Add(key), Is.True,
                    $"{fileName}: the model/XABC key {key} produced more than one entry.");
            }

            // Several patterns can be confirmed on the same bar, so the entries are told
            // apart by the identity of their level object rather than by the bar index.
            var closedEntries = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach ((string _, LevelEventArgs args) in run.Closes)
            {
                Assert.That(closedEntries.Add(args.FromLevel), Is.True,
                    $"{fileName}: the setup entered at bar {args.FromLevel.BarIndex} " +
                    "received more than one terminal event.");
                Assert.That(args.Level.BarIndex, Is.GreaterThan(args.FromLevel.BarIndex),
                    $"{fileName}: a setup must not be closed on the bar it was created on.");
            }

            Assert.That(run.Closes, Has.Count.LessThanOrEqualTo(run.Enters.Count));
            TestContext.WriteLine(
                $"{fileName}: bars={bars} enters={run.Enters.Count} closes={run.Closes.Count} " +
                $"maxCandidates={run.MaxCandidates}");
        }

        [TestCaseSource(typeof(HarmonicCsvData), nameof(HarmonicCsvData.CiFiles))]
        public void RealData_RepeatedRunIsIdentical(string fileName)
        {
            TestBarsProvider provider = HarmonicCsvData.Load(fileName);
            int bars = Math.Min(provider.Count, CSV_MAX_BARS);
            var parameters = new HarmonicParams();

            string first = Describe(Run(provider, bars, parameters));
            string second = Describe(Run(provider, bars, parameters));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void RealData_SequentialFeedMatchesLoadedHistory()
        {
            const string fileName = "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv";
            const int bars = 4000;

            TestBarsProvider loaded = HarmonicCsvData.Load(fileName);
            Assert.That(loaded.Count, Is.GreaterThan(bars));

            var parameters = new HarmonicParams();
            string batch = Describe(Run(loaded, bars, parameters));

            // The same range, but fed bar by bar so the finder never sees future candles.
            var incremental = new TestBarsProvider(loaded.TimeFrame, loaded.BarSymbol);
            var finder = new HarmonicSetupFinder(incremental, incremental.BarSymbol, parameters);
            var run = new RunResult();
            finder.OnEnter += (_, e) => run.Enters.Add(e);
            finder.OnTakeProfit += (_, e) => run.Closes.Add(("TP", e));
            finder.OnStopLoss += (_, e) => run.Closes.Add(("SL", e));

            for (int i = 0; i < bars; i++)
            {
                incremental.AddCandle(
                    new Candle(loaded.GetOpenPrice(i), loaded.GetHighPrice(i),
                        loaded.GetLowPrice(i), loaded.GetClosePrice(i), null, i),
                    loaded.GetOpenTime(i));
                finder.CheckBar(loaded.GetOpenTime(i));
            }

            Assert.That(Describe(run), Is.EqualTo(batch),
                "A sequential feed must produce the same sequence as a loaded history.");
        }

        private static void AssertPointOnCandle(
            TestBarsProvider provider, BarPoint point, bool isLow, string fileName)
        {
            double expected = isLow
                ? provider.GetLowPrice(point.BarIndex)
                : provider.GetHighPrice(point.BarIndex);

            Assert.That(point.Value, Is.EqualTo(expected).Within(PRICE_TOLERANCE),
                $"{fileName}: the point at bar {point.BarIndex} does not match the candle.");
        }

        private static void AssertRatios(HarmonicItem item, HarmonicParams parameters, string fileName)
        {
            HarmonicPatternDefinition definition = HarmonicPatternDefinition.Get(item.PatternType);
            double x = item.ItemX.Value;
            double a = item.ItemA.Value;
            double b = item.ItemB.Value;
            double c = item.ItemC.Value;
            double d = item.ItemD.Value;

            double xa = Math.Abs(a - x);
            double ab = Math.Abs(a - b);
            double bc = Math.Abs(b - c);

            Assert.Multiple(() =>
            {
                Assert.That(definition.TestAb(ab, xa, parameters.FibErrorPercent), Is.True,
                    $"{fileName}: AB/XA of the reported {item.PatternType} does not validate.");
                Assert.That(definition.TestBc(bc, ab, parameters.FibErrorPercent), Is.True,
                    $"{fileName}: BC/AB of the reported {item.PatternType} does not validate.");
                Assert.That(definition.TestCd(Math.Abs(c - d), bc, xa, Math.Abs(c - x),
                    Math.Abs(a - d), parameters.FibErrorPercent), Is.True,
                    $"{fileName}: the CD leg of the reported {item.PatternType} does not validate.");
            });
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
