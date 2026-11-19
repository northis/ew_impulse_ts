using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Deterministic checks of <see cref="RelativeStrengthIndexFinder"/>: the standard
    /// close-based Wilder RSI, so a strictly rising close series reads 100 and a falling
    /// one reads 0 regardless of the high/low.
    /// </summary>
    [TestFixture]
    public class RelativeStrengthIndexFinderTests
    {
        private static readonly DateTime S_START =
            new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// High/low are fixed at 1.1/0.9 (a constant median), only the close moves. A
        /// median-based RSI would stay at 50; a close-based one must reach the endpoints.
        /// </summary>
        private static TestBarsProvider BuildProvider(Func<int, double> closeAt, int bars = 60)
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
            for (int i = 0; i < bars; i++)
            {
                double close = closeAt(i);
                provider.AddCandle(
                    new Candle(close, 1.1, 0.9, close, Index: i),
                    S_START.AddHours(i));
            }

            return provider;
        }

        private static RelativeStrengthIndexFinder BuildFinder(
            TestBarsProvider provider, int period = 14)
        {
            var rsi = new RelativeStrengthIndexFinder(
                provider, period, useAutoCalculateEvent: false);
            for (int i = 1; i < provider.Count; i++)
                rsi.OnCalculate(provider.GetOpenTime(i));

            return rsi;
        }

        [Test]
        public void Rsi_UsesTheClosePrice_RisingSeriesIs100()
        {
            TestBarsProvider provider = BuildProvider(i => 0.8 + i * 0.002);
            RelativeStrengthIndexFinder rsi = BuildFinder(provider);

            Assert.That(rsi.GetResultValue(provider.Count - 1), Is.EqualTo(100),
                "A strictly rising close has no losses, so the RSI is 100.");
        }

        [Test]
        public void Rsi_FallingSeriesIsZero()
        {
            TestBarsProvider provider = BuildProvider(i => 1.2 - i * 0.002);
            RelativeStrengthIndexFinder rsi = BuildFinder(provider);

            Assert.That(rsi.GetResultValue(provider.Count - 1), Is.EqualTo(0),
                "A strictly falling close has no gains, so the RSI is 0.");
        }

        [Test]
        public void Rsi_FlatSeriesIsFifty()
        {
            TestBarsProvider provider = BuildProvider(_ => 1.0);
            RelativeStrengthIndexFinder rsi = BuildFinder(provider);

            Assert.That(rsi.GetResultValue(provider.Count - 1), Is.EqualTo(50));
        }

        [Test]
        public void Rsi_IsInRange()
        {
            TestBarsProvider provider = BuildProvider(
                i => 1.0 + Math.Sin(i * 0.3) * 0.05);
            RelativeStrengthIndexFinder rsi = BuildFinder(provider);

            for (int i = 20; i < provider.Count; i++)
            {
                int value = rsi.GetResultValue(i);
                Assert.That(value, Is.InRange(0, 100));
            }
        }
    }
}
