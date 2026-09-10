using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests of <see cref="ThinMarketStopGuard"/> (DIAGONAL.md §4.7): how far the stop of an
    /// open setup is pulled away from the entry across a thin window, and when it returns.
    /// </summary>
    internal class ThinMarketStopGuardTests
    {
        private const double SPREAD = 0.00040;
        private const double RISK = 0.00200;

        private static (ThinMarketStopGuard Guard, TestTradeViewManager View) BuildGuard(
            double spreadBufferRatio = ThinMarketStopGuard.DEFAULT_SPREAD_BUFFER_RATIO,
            double riskBufferRatio = ThinMarketStopGuard.DEFAULT_RISK_BUFFER_RATIO,
            double spread = SPREAD)
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
            var view = new TestTradeViewManager(provider) { Bid = 1.20000, Ask = 1.20000 + spread };
            var calendar = new ThinMarketCalendar(
                Array.Empty<ITradingHours>(),
                TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15),
                new TimeSpan(21, 0, 0), new TimeSpan(22, 0, 0));

            return (new ThinMarketStopGuard(view, provider.BarSymbol, calendar,
                spreadBufferRatio, riskBufferRatio), view);
        }

        private static readonly DateTime THICK = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime THIN = new(2026, 1, 5, 21, 30, 0, DateTimeKind.Utc);

        [Test]
        public void OutsideTheWindow_NoBuffer()
        {
            (ThinMarketStopGuard guard, _) = BuildGuard();

            Assert.That(guard.Update(THICK, RISK), Is.False);
            Assert.That(guard.IsActive, Is.False);
            Assert.That(guard.Buffer, Is.EqualTo(0));
        }

        [Test]
        public void InsideTheWindow_BufferIsTheWidestOfTheSpreadAndTheRiskShare()
        {
            // 2 × 0.0004 = 0.0008 against 0.25 × 0.002 = 0.0005 — the spread wins.
            (ThinMarketStopGuard guard, _) = BuildGuard();

            Assert.That(guard.Update(THIN, RISK), Is.True);
            Assert.That(guard.IsActive, Is.True);
            Assert.That(guard.Buffer, Is.EqualTo(2 * SPREAD).Within(1e-12));

            // A tiny spread leaves the scale-free share of the risk in charge.
            (ThinMarketStopGuard narrow, _) = BuildGuard(spread: 0.00005);
            Assert.That(narrow.Update(THIN, RISK), Is.True);
            Assert.That(narrow.Buffer, Is.EqualTo(0.25 * RISK).Within(1e-12));
        }

        [Test]
        public void InsideTheWindow_TheBufferOnlyGrows()
        {
            (ThinMarketStopGuard guard, TestTradeViewManager view) = BuildGuard();

            Assert.That(guard.Update(THIN, RISK), Is.True);
            double first = guard.Buffer;

            // The spread narrows again — the protection stays at the widest it has been.
            view.Bid = 1.20000;
            view.Ask = 1.20005;
            Assert.That(guard.Update(THIN.AddMinutes(10), RISK), Is.False);
            Assert.That(guard.Buffer, Is.EqualTo(first));

            // A wider spread pushes it further out.
            view.Ask = 1.20000 + 4 * SPREAD;
            Assert.That(guard.Update(THIN.AddMinutes(20), RISK), Is.True);
            Assert.That(guard.Buffer, Is.EqualTo(2 * 4 * SPREAD).Within(1e-12));
        }

        [Test]
        public void LeavingTheWindow_RestoresTheStop()
        {
            (ThinMarketStopGuard guard, _) = BuildGuard();

            Assert.That(guard.Update(THIN, RISK), Is.True);
            Assert.That(guard.IsActive, Is.True);

            Assert.That(guard.Update(THIN.AddHours(2), RISK), Is.True, "the restore is a change");
            Assert.That(guard.IsActive, Is.False);
            Assert.That(guard.Buffer, Is.EqualTo(0));

            Assert.That(guard.Update(THIN.AddHours(3), RISK), Is.False, "nothing to do anymore");
        }

        [Test]
        public void TickSpread_IsUsedWhenTheViewManagerReportsNone()
        {
            (ThinMarketStopGuard guard, TestTradeViewManager view) = BuildGuard(spread: 0);

            guard.RecordSpread(3 * SPREAD);
            Assert.That(guard.Update(THIN, RISK), Is.True);
            Assert.That(guard.Buffer, Is.EqualTo(2 * 3 * SPREAD).Within(1e-12));
            Assert.That(view.Ask - view.Bid, Is.EqualTo(0));
        }

        [Test]
        public void Reset_DropsTheState()
        {
            (ThinMarketStopGuard guard, _) = BuildGuard();

            Assert.That(guard.Update(THIN, RISK), Is.True);
            guard.Reset();

            Assert.That(guard.IsActive, Is.False);
            Assert.That(guard.Buffer, Is.EqualTo(0));
        }

        [Test]
        public void NoSchedule_Disabled()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
            var calendar = new ThinMarketCalendar(
                Array.Empty<ITradingHours>(), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
            var guard = new ThinMarketStopGuard(null, provider.BarSymbol, calendar);

            Assert.That(guard.IsEnabled, Is.False);
            Assert.That(guard.Update(THIN, RISK), Is.False);
            Assert.That(guard.Buffer, Is.EqualTo(0));
        }
    }
}
