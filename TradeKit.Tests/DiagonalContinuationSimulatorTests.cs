using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Deterministic checks of the "diagonal as the entry impulse" trade (DIAGONAL.md §9.43):
    /// after an up diagonal that ends at 1.1000, the limit sits <c>k</c> of the height below
    /// the running extreme in the diagonal's direction, the stop is the diagonal start P0,
    /// and the default target is <c>P0 + 1.0*D</c> — the diagonal end.
    /// </summary>
    [TestFixture]
    public class DiagonalContinuationSimulatorTests
    {
        private static readonly DateTime S_START =
            new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static TestBarsProvider BuildProvider(
            params (double O, double H, double L, double C)[] bars)
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
            for (int i = 0; i < bars.Length; i++)
            {
                (double o, double h, double l, double c) = bars[i];
                provider.AddCandle(new Candle(o, h, l, c, Index: i), S_START.AddHours(i));
            }

            return provider;
        }

        /// <summary>
        /// Bars 0-20 are an up diagonal from 1.0000 to 1.1000. The signal is on bar 20, so
        /// the continuation starts on bar 21. <paramref name="afterSignal"/> replaces the
        /// four bars after the diagonal.
        /// </summary>
        private static TestBarsProvider BuildProviderAfterSignal(
            params (double O, double H, double L, double C)[] afterSignal)
        {
            var bars = new List<(double, double, double, double)>();
            for (int i = 0; i <= 20; i++)
            {
                double b = 1.0 + i * 0.005;
                bars.Add((b, i == 20 ? 1.1 : b + 0.0002, b - 0.0002, b + 0.0001));
            }

            bars.AddRange(afterSignal);
            return BuildProvider(bars.ToArray());
        }

        private static DiagonalContinuationSetup UpDiagonalSetup(int signalBar = 20)
        {
            return new DiagonalContinuationSetup(
                IsUp: true,
                SignalBar: signalBar,
                SignalTime: S_START.AddHours(signalBar),
                P0Bar: 0,
                P0: 1.0,
                P5Bar: 20,
                P5: 1.1);
        }

        private static DiagonalWave5State State(double w5, int w5Bar)
        {
            return new DiagonalWave5State(
                new BarPoint(1.0, S_START, TimeFrameHelper.Hour1, 0),
                new BarPoint(1.02, S_START.AddHours(5), TimeFrameHelper.Hour1, 5),
                new BarPoint(1.01, S_START.AddHours(8), TimeFrameHelper.Hour1, 8),
                new BarPoint(1.08, S_START.AddHours(12), TimeFrameHelper.Hour1, 12),
                new BarPoint(1.09, S_START.AddHours(17), TimeFrameHelper.Hour1, 17),
                IsUp: true,
                W5: w5,
                W5Bar: w5Bar);
        }

        [Test]
        public void Simulate_Dynamic_FillsTheRetracementAndTargetsTheDiagonalEnd()
        {
            // Bar 21 makes a new extreme 1.105; the 50% limit becomes 1.0525 and bar 22
            // fills it. TP1 = P0 + 1.0*D = 1.1050 is reached on bar 23.
            TestBarsProvider provider = BuildProviderAfterSignal(
                (1.1000, 1.1050, 1.0900, 1.1000),
                (1.1000, 1.1020, 1.0500, 1.0600),
                (1.0600, 1.1100, 1.0500, 1.1050),
                (1.1050, 1.1060, 1.1000, 1.1020));

            DiagonalContinuationTrade trade = DiagonalContinuationSimulator.Simulate(
                provider,
                UpDiagonalSetup(),
                new DiagonalContinuationParams(EnterRatio: 0.5d, TargetRatio: 1.0d));

            Assert.That(trade.EntryBar, Is.EqualTo(22));
            Assert.That(trade.Entry, Is.EqualTo(1.0525).Within(1e-9));
            Assert.That(trade.Stop, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(trade.Target1, Is.EqualTo(1.105).Within(1e-9));
            Assert.That(trade.D, Is.EqualTo(0.105).Within(1e-9));
            Assert.That(trade.Reanchors, Is.EqualTo(1));
            Assert.That(trade.EntryDepth, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(trade.Outcome, Is.EqualTo("TP"));
            Assert.That(trade.NetR, Is.EqualTo(0.996).Within(0.01));
        }

        [Test]
        public void Simulate_Dynamic_NoFill_WhenTheCorrectionIsShallow()
        {
            // The price never retraces to 50% of the diagonal height.
            TestBarsProvider provider = BuildProviderAfterSignal(
                (1.0999, 1.0999, 1.0700, 1.0750),
                (1.0750, 1.0990, 1.0700, 1.0800),
                (1.0800, 1.0995, 1.0700, 1.0900),
                (1.0900, 1.0999, 1.0700, 1.0950));

            DiagonalContinuationTrade trade = DiagonalContinuationSimulator.Simulate(
                provider,
                UpDiagonalSetup(),
                new DiagonalContinuationParams(EnterRatio: 0.5d));

            Assert.That(trade.EntryBar, Is.EqualTo(-1));
            Assert.That(trade.Outcome, Is.EqualTo("NOFILL"));
            Assert.That(trade.NoFillReason, Is.EqualTo("noFill"));
        }

        [Test]
        public void Simulate_Dynamic_StopIsTheDiagonalStart()
        {
            // Filled at 1.0525 on bar 22, then the price breaks P0 = 1.0 on bar 23.
            TestBarsProvider provider = BuildProviderAfterSignal(
                (1.1000, 1.1050, 1.0900, 1.1000),
                (1.1000, 1.1020, 1.0500, 1.0600),
                (1.0600, 1.0600, 0.9900, 0.9950),
                (0.9950, 1.0000, 0.9900, 0.9950));

            DiagonalContinuationTrade trade = DiagonalContinuationSimulator.Simulate(
                provider,
                UpDiagonalSetup(),
                new DiagonalContinuationParams(EnterRatio: 0.5d));

            Assert.That(trade.EntryBar, Is.EqualTo(22));
            Assert.That(trade.Outcome, Is.EqualTo("SL"));
            Assert.That(trade.ExitPrice, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(trade.GrossR, Is.EqualTo(-1.0).Within(1e-9));
            Assert.That(trade.NetR, Is.LessThan(0));
        }

        [Test]
        public void Simulate_Dynamic_UsesTheValidatedWave5End()
        {
            // The same bars, but the end comes from the live wave-5 tracker: the level at
            // bar 22 is 50% below the validated end 1.105, so the fill is unchanged.
            TestBarsProvider provider = BuildProviderAfterSignal(
                (1.1000, 1.1050, 1.0900, 1.1000),
                (1.1000, 1.1020, 1.0500, 1.0600),
                (1.0600, 1.1100, 1.0500, 1.1050),
                (1.1050, 1.1060, 1.1000, 1.1020));

            var timeline = new Dictionary<int, DiagonalWave5State>
            {
                [21] = State(1.105, 21),
                [22] = State(1.105, 21),
                [23] = State(1.105, 21)
            };

            DiagonalContinuationTrade trade = DiagonalContinuationSimulator.Simulate(
                provider,
                UpDiagonalSetup(),
                new DiagonalContinuationParams(EnterRatio: 0.5d, TargetRatio: 1.0d),
                timeline);

            Assert.That(trade.EntryBar, Is.EqualTo(22));
            Assert.That(trade.Entry, Is.EqualTo(1.0525).Within(1e-9));
            Assert.That(trade.Target1, Is.EqualTo(1.105).Within(1e-9));
            Assert.That(trade.Outcome, Is.EqualTo("TP"));
            Assert.That(trade.DiagonalBrokenBar, Is.EqualTo(-1));
        }

        [Test]
        public void Simulate_Dynamic_BrokenDiagonal_IsNotTraded()
        {
            // The tracker has no state for bar 22: wave 5 reached |W3| there and the
            // diagonal is dead. The raw price would have filled the limit at 1.0525 — it
            // must not be traded.
            TestBarsProvider provider = BuildProviderAfterSignal(
                (1.1000, 1.1050, 1.0900, 1.1000),
                (1.1000, 1.1020, 1.0500, 1.0600),
                (1.0600, 1.0600, 1.0300, 1.0350),
                (1.0350, 1.0400, 1.0300, 1.0350));

            var timeline = new Dictionary<int, DiagonalWave5State>
            {
                [21] = State(1.105, 21)
            };

            DiagonalContinuationTrade trade = DiagonalContinuationSimulator.Simulate(
                provider,
                UpDiagonalSetup(),
                new DiagonalContinuationParams(EnterRatio: 0.5d),
                timeline);

            Assert.That(trade.EntryBar, Is.EqualTo(-1));
            Assert.That(trade.Outcome, Is.EqualTo("NOFILL"));
            Assert.That(trade.NoFillReason, Is.EqualTo("diagonalBroken"));
            Assert.That(trade.DiagonalBrokenBar, Is.EqualTo(22));
        }

        [Test]
        public void Simulate_CorrectionEnd_EntersAtTheConfirmationClose()
        {
            // Bar 21 retraces 20% of the height: V4 enters at its close 1.0850, stops at
            // P0 and targets C + 1.272*D = 1.0800 + 0.1272 = 1.2072.
            TestBarsProvider provider = BuildProviderAfterSignal(
                (1.1000, 1.1000, 1.0800, 1.0850),
                (1.0850, 1.2100, 1.0800, 1.2000),
                (1.2000, 1.2050, 1.1900, 1.1950));

            DiagonalContinuationTrade trade = DiagonalContinuationSimulator.Simulate(
                provider,
                UpDiagonalSetup(),
                new DiagonalContinuationParams(
                    Variant: DiagonalContinuationVariant.CORRECTION_END,
                    ConfirmRatio: 0.1d,
                    TargetKind: DiagonalContinuationTargetKind.CORRECTION_END,
                    TargetRatio: 1.272d));

            Assert.That(trade.EntryBar, Is.EqualTo(21));
            Assert.That(trade.Entry, Is.EqualTo(1.085).Within(1e-9));
            Assert.That(trade.Stop, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(trade.Target1, Is.EqualTo(1.2072).Within(1e-9));
            Assert.That(trade.Outcome, Is.EqualTo("TP"));
        }
    }
}
