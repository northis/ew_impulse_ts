using NUnit.Framework;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Stage 3 checks: the setup lifecycle of <see cref="HarmonicSetupFinder"/>.
    /// </summary>
    [TestFixture]
    public class HarmonicSetupFinderTests
    {
        private const int X_BAR = 10;
        private const int A_BAR = 20;
        private const int B_BAR = 30;
        private const int C_BAR = 40;
        private const int D_BAR = 50;
        private const int ENTRY_BAR = D_BAR + 1;

        private const double XA_HEIGHT = 100d;
        private const double A_VALUE = 200d;
        private const double X_VALUE = 100d;

        private static readonly double B_VALUE = A_VALUE - HarmonicFib.F618 * XA_HEIGHT;
        private static readonly double C_VALUE = B_VALUE + HarmonicFib.F886 * (A_VALUE - B_VALUE);
        private static readonly double D_VALUE = A_VALUE - HarmonicFib.F786 * XA_HEIGHT;

        private sealed class Recorder
        {
            public List<HarmonicSignalEventArgs> Enters { get; } = new();
            public List<LevelEventArgs> TakeProfits { get; } = new();
            public List<LevelEventArgs> StopLosses { get; } = new();
            public List<LevelEventArgs> Breakevens { get; } = new();
            public List<LevelEventArgs> ManualCloses { get; } = new();

            public void Attach(HarmonicSetupFinder finder)
            {
                finder.OnEnter += (_, e) => Enters.Add(e);
                finder.OnTakeProfit += (_, e) => TakeProfits.Add(e);
                finder.OnStopLoss += (_, e) => StopLosses.Add(e);
                finder.OnBreakeven += (_, e) => Breakevens.Add(e);
                finder.OnManualClose += (_, e) => ManualCloses.Add(e);
            }
        }

        private static HarmonicParams GartleyOnly()
        {
            return new HarmonicParams
            {
                Patterns = new SortedSet<HarmonicPatternType> { HarmonicPatternType.GARTLEY }
            };
        }

        private static List<(int, double)> Points(int lastBar, double lastValue)
        {
            return new List<(int, double)>
            {
                (0, 150d),
                (X_BAR, X_VALUE),
                (A_BAR, A_VALUE),
                (B_BAR, B_VALUE),
                (C_BAR, C_VALUE),
                (D_BAR, D_VALUE),
                (lastBar, lastValue)
            };
        }

        private static (Recorder Recorder, HarmonicSetupFinder Finder) Run(
            TestBarsProvider provider, HarmonicParams parameters)
        {
            var finder = new HarmonicSetupFinder(provider, provider.BarSymbol, parameters);
            var recorder = new Recorder();
            recorder.Attach(finder);

            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            return (recorder, finder);
        }

        [Test]
        public void Setup_EntersAtTheCloseOfTheConfirmationBar()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d));
            (Recorder recorder, _) = Run(provider, GartleyOnly());

            Assert.That(recorder.Enters, Has.Count.EqualTo(1));
            HarmonicSignalEventArgs args = recorder.Enters[0];

            Assert.Multiple(() =>
            {
                Assert.That(args.Level.BarIndex, Is.EqualTo(ENTRY_BAR));
                Assert.That(args.Level.Value,
                    Is.EqualTo(provider.GetClosePrice(ENTRY_BAR)).Within(1e-12));
                Assert.That(args.IsLimit, Is.False);
                Assert.That(args.IsActive, Is.True);
                Assert.That(args.TakeProfit.Value, Is.GreaterThan(args.Level.Value));
                Assert.That(args.StopLoss.Value, Is.LessThan(args.Level.Value));
                Assert.That(args.RiskReward, Is.GreaterThan(0d));
                Assert.That(args.HarmonicItem.ItemD.BarIndex, Is.EqualTo(D_BAR));
            });
        }

        [Test]
        public void Setup_ReachesTheTakeProfit()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d));
            (Recorder recorder, HarmonicSetupFinder finder) = Run(provider, GartleyOnly());

            Assert.Multiple(() =>
            {
                Assert.That(recorder.TakeProfits, Has.Count.EqualTo(1));
                Assert.That(recorder.StopLosses, Is.Empty);
                Assert.That(recorder.TakeProfits[0].Level.Value,
                    Is.EqualTo(recorder.Enters[0].TakeProfit.Value).Within(1e-12));
                Assert.That(finder.ActiveSetupCount, Is.Zero,
                    "A closed setup must be dropped.");
            });
        }

        [Test]
        public void Setup_ReachesTheStopLoss()
        {
            var points = new List<(int, double)>
            {
                (0, 150d), (X_BAR, X_VALUE), (A_BAR, A_VALUE), (B_BAR, B_VALUE),
                (C_BAR, C_VALUE), (D_BAR, D_VALUE), (52, 135d), (70, 80d)
            };

            TestBarsProvider provider = HarmonicTestBars.Build(points);
            (Recorder recorder, HarmonicSetupFinder finder) = Run(provider, GartleyOnly());

            Assert.Multiple(() =>
            {
                Assert.That(recorder.Enters, Has.Count.EqualTo(1));
                Assert.That(recorder.StopLosses, Has.Count.EqualTo(1));
                Assert.That(recorder.TakeProfits, Is.Empty);
                Assert.That(finder.ActiveSetupCount, Is.Zero);
            });
        }

        [Test]
        public void Setup_IsNotClosedOnTheBarItWasCreatedOn()
        {
            // The confirmation bar itself already spans the take profit level.
            var overrides = new Dictionary<int, (double High, double Low)>
            {
                [ENTRY_BAR] = (185d, D_VALUE + 1d)
            };

            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d), overrides);
            (Recorder recorder, _) = Run(provider, GartleyOnly());

            Assert.That(recorder.Enters, Has.Count.EqualTo(1));
            Assert.That(recorder.Enters[0].Level.BarIndex, Is.EqualTo(ENTRY_BAR));
            Assert.That(provider.GetHighPrice(ENTRY_BAR),
                Is.GreaterThanOrEqualTo(recorder.Enters[0].TakeProfit.Value),
                "The test bar must actually span the take profit.");

            IEnumerable<int> closeBars = recorder.TakeProfits.Concat(recorder.StopLosses)
                .Select(a => a.Level.BarIndex);
            Assert.That(closeBars, Has.All.GreaterThan(ENTRY_BAR));
        }

        [Test]
        public void SimultaneousTakeProfitAndStopLoss_CountsAsStopLoss()
        {
            var overrides = new Dictionary<int, (double High, double Low)>
            {
                // A bar right after the entry that touches both levels.
                [ENTRY_BAR + 1] = (300d, 50d)
            };

            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d), overrides);
            (Recorder recorder, _) = Run(provider, GartleyOnly());

            Assert.Multiple(() =>
            {
                Assert.That(recorder.Enters, Has.Count.EqualTo(1));
                Assert.That(recorder.StopLosses, Has.Count.EqualTo(1));
                Assert.That(recorder.TakeProfits, Is.Empty);
                Assert.That(recorder.StopLosses[0].Level.BarIndex, Is.EqualTo(ENTRY_BAR + 1));
            });
        }

        [Test]
        public void Gap_ReportsTheLevelPriceNotTheBarOpen()
        {
            var overrides = new Dictionary<int, (double High, double Low)>
            {
                [ENTRY_BAR + 1] = (300d, 280d)
            };

            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d), overrides);
            (Recorder recorder, _) = Run(provider, GartleyOnly());

            Assert.That(recorder.TakeProfits, Has.Count.EqualTo(1));
            Assert.That(recorder.TakeProfits[0].Level.Value,
                Is.EqualTo(recorder.Enters[0].TakeProfit.Value).Within(1e-12),
                "The event must report the level price, not the gapped bar open.");
        }

        [Test]
        public void MinimumRiskReward_BlocksTheSetup()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d));
            HarmonicParams parameters = GartleyOnly();
            parameters.MinimumRiskReward = 10d;

            (Recorder recorder, _) = Run(provider, parameters);
            Assert.That(recorder.Enters, Is.Empty);
        }

        [Test]
        public void MinimumScore_BlocksTheSetup()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d));
            HarmonicParams parameters = GartleyOnly();
            parameters.MinimumScore = 1.01d;

            (Recorder recorder, _) = Run(provider, parameters);
            Assert.That(recorder.Enters, Is.Empty);
        }

        [Test]
        public void Setup_IsIssuedOnlyOncePerPattern()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d));
            (Recorder recorder, _) = Run(provider, GartleyOnly());

            IEnumerable<string> keys = recorder.Enters.Select(a =>
                $"{a.HarmonicItem.PatternType}|{a.HarmonicItem.ItemX.BarIndex}|" +
                $"{a.HarmonicItem.ItemA.BarIndex}|{a.HarmonicItem.ItemB.BarIndex}|" +
                $"{a.HarmonicItem.ItemC.BarIndex}");

            Assert.That(keys, Is.Unique);
            Assert.That(recorder.Enters, Is.Not.Empty);
        }

        [Test]
        public void Levels_AreImmutableAfterTheEntry()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d));
            var finder = new HarmonicSetupFinder(provider, provider.BarSymbol, GartleyOnly());

            HarmonicSignalEventArgs? args = null;
            double takeProfit = 0d;
            double stopLoss = 0d;
            double entry = 0d;

            finder.OnEnter += (_, e) =>
            {
                args = e;
                entry = e.Level.Value;
                takeProfit = e.TakeProfit.Value;
                stopLoss = e.StopLoss.Value;
            };

            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            Assert.That(args, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(args!.Level.Value, Is.EqualTo(entry));
                Assert.That(args.TakeProfit.Value, Is.EqualTo(takeProfit));
                Assert.That(args.StopLoss.Value, Is.EqualTo(stopLoss));
            });
        }

        [Test]
        public void ManualClose_DropsTheSetup()
        {
            var points = new List<(int, double)>
            {
                (0, 150d), (X_BAR, X_VALUE), (A_BAR, A_VALUE), (B_BAR, B_VALUE),
                (C_BAR, C_VALUE), (D_BAR, D_VALUE), (55, 140d)
            };

            TestBarsProvider provider = HarmonicTestBars.Build(points);
            var finder = new HarmonicSetupFinder(provider, provider.BarSymbol, GartleyOnly());
            var recorder = new Recorder();
            recorder.Attach(finder);

            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            Assert.That(recorder.Enters, Has.Count.EqualTo(1));
            Assert.That(finder.ActiveSetupCount, Is.EqualTo(1),
                "The setup must still be tracked - neither level was reached.");

            finder.NotifyManualClose(recorder.Enters[0], null);

            Assert.Multiple(() =>
            {
                Assert.That(recorder.ManualCloses, Has.Count.EqualTo(1));
                Assert.That(finder.ActiveSetupCount, Is.Zero);
            });
        }

        [Test]
        public void Breakeven_MovesTheStopToTheEntryOnce()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(Points(60, 190d));
            HarmonicParams parameters = GartleyOnly();
            parameters.BreakevenRatio = 0.5d;

            (Recorder recorder, _) = Run(provider, parameters);

            Assert.That(recorder.Enters, Has.Count.EqualTo(1));
            HarmonicSignalEventArgs args = recorder.Enters[0];

            Assert.Multiple(() =>
            {
                Assert.That(recorder.Breakevens, Has.Count.EqualTo(1),
                    "The breakeven must fire exactly once.");
                Assert.That(args.HasBreakeven, Is.True);
                Assert.That(args.StopLoss.Value, Is.EqualTo(args.Level.Value).Within(1e-12));
                Assert.That(recorder.TakeProfits, Has.Count.EqualTo(1));
            });
        }
    }
}
