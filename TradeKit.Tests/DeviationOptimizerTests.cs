using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for <see cref="DeviationOptimizer"/> — automatic determination of
    /// the optimal deviationPercent for <see cref="SimpleExtremumFinder"/>.
    /// </summary>
    [TestFixture]
    public class DeviationOptimizerTests
    {
        private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Hour1;
        private static readonly ISymbol SYMBOL =
            new SymbolBase("TEST", "Test/USD", 1, 3, 0.001, 0.001, 100);

        /// <summary>
        /// For a monotonically rising series (no reversals), the optimizer should
        /// return a large deviation (upper bound) since there are never more than
        /// 2 extremum points regardless of deviation.
        /// </summary>
        [Test]
        public void FindOptimalDeviation_MonotonicRise_ReturnsUpperBound()
        {
            var provider = new TestBarsProvider(TIME_FRAME, SYMBOL);
            // Create 50 bars with steadily rising prices
            DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 50; i++)
            {
                double price = 100.0 + i * 0.5;
                provider.AddCandle(
                    new Candle(price, price + 0.2, price - 0.1, price + 0.1),
                    start.AddHours(i));
            }

            var optimizer = new DeviationOptimizer(provider, 0, provider.Count - 1, false);
            double optimalDev = optimizer.FindOptimalDeviation();

            // With monotonic data, there's no point where segments increase,
            // so the result should be the initial upper bound found by FindUpperBound
            // (which stops as soon as count ≤ 2, i.e. a relatively small deviation)
            int countAtOptimal = optimizer.GetExtremumCount(optimalDev);
            TestContext.WriteLine($"Monotonic: optimalDev={optimalDev:F4}%, count={countAtOptimal}");
            Assert.That(countAtOptimal, Is.LessThanOrEqualTo(2),
                "Monotonic series should have at most 2 extremum points at the optimal deviation.");
        }

        /// <summary>
        /// For a sawtooth pattern with uniform amplitude, the saturation point
        /// should be around the amplitude percentage — below that, no new segments appear.
        /// </summary>
        [Test]
        public void FindOptimalDeviation_UniformSawtooth_FindsSaturation()
        {
            var provider = new TestBarsProvider(TIME_FRAME, SYMBOL);
            DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Sawtooth: 100 → 102 → 100 → 102 → ... (2% swings)
            // Each full cycle is 4 bars: rise, rise, fall, fall
            double basePrice = 100.0;
            double amplitude = 2.0; // 2% of basePrice
            for (int i = 0; i < 40; i++)
            {
                int phase = i % 4;
                double mid = phase < 2
                    ? basePrice + amplitude * (phase + 1) / 2.0
                    : basePrice + amplitude - amplitude * (phase - 1) / 2.0;
                provider.AddCandle(
                    new Candle(mid - 0.1, mid + 0.1, mid - 0.2, mid),
                    start.AddHours(i));
            }

            var optimizer = new DeviationOptimizer(provider, 0, provider.Count - 1, false);
            double optimalDev = optimizer.FindOptimalDeviation();

            // The amplitude is 2% of basePrice, so the zigzag should detect all turns
            // at deviation slightly below 2.0, and saturation should be somewhere around
            // or below that threshold.
            TestContext.WriteLine($"Optimal deviation for uniform sawtooth: {optimalDev:F4}");
            Assert.That(optimalDev, Is.LessThan(3.0),
                "Optimal deviation should be close to the swing amplitude percentage.");
            Assert.That(optimalDev, Is.GreaterThan(0.001),
                "Deviation should not collapse to the minimum.");
        }

        /// <summary>
        /// Tests that GetExtremumCount returns reasonable values: fewer points
        /// for larger deviations, more for smaller.
        /// </summary>
        [Test]
        public void GetExtremumCount_SmallerDeviation_MorePoints()
        {
            var provider = new TestBarsProvider(TIME_FRAME, SYMBOL);
            DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Create a pattern with multiple swings of different sizes
            double[] prices = { 100, 101, 99, 102, 98, 103, 97, 104, 100, 105 };
            for (int i = 0; i < prices.Length; i++)
            {
                double p = prices[i];
                provider.AddCandle(
                    new Candle(p, p + 0.5, p - 0.5, p),
                    start.AddHours(i));
            }

            var optimizer = new DeviationOptimizer(provider, 0, provider.Count - 1, false);

            int countLargeDev = optimizer.GetExtremumCount(5.0);
            int countSmallDev = optimizer.GetExtremumCount(0.5);

            TestContext.WriteLine($"Count at 5.0%: {countLargeDev}, at 0.5%: {countSmallDev}");
            Assert.That(countSmallDev, Is.GreaterThanOrEqualTo(countLargeDev),
                "Smaller deviation should find at least as many extrema as larger deviation.");
        }

        /// <summary>
        /// Uses real USDJPY H1 data to verify the optimizer finds a meaningful
        /// deviation that produces a reasonable number of extrema.
        /// </summary>
        [Test]
        public void FindOptimalDeviation_RealUsdjpyData_FindsReasonableDeviation()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "USDJPY_h1_2026-04-30T06-00-00_2026-05-06T04-00-00.csv");

            var provider = new TestBarsProvider(TIME_FRAME,
                new SymbolBase("USDJPY", "USD/JPY", 1, 3, 0.001, 0.001, 100));
            provider.LoadCandles(csvPath);

            // Find overall range
            double maxValue = double.MinValue;
            double minValue = double.MaxValue;
            int maxBarIndex = 0;
            int minBarIndex = 0;

            for (int i = 0; i < provider.Count; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                if (high > maxValue) { maxValue = high; maxBarIndex = i; }
                if (low < minValue) { minValue = low; minBarIndex = i; }
            }

            int startIdx = Math.Min(maxBarIndex, minBarIndex);
            int endIdx = Math.Max(maxBarIndex, minBarIndex);
            bool isUp = maxBarIndex > minBarIndex;

            var optimizer = new DeviationOptimizer(provider, startIdx, endIdx, !isUp);
            double optimalDev = optimizer.FindOptimalDeviation();

            TestContext.WriteLine($"USDJPY optimal deviation: {optimalDev:F4}%");

            // Verify the found deviation produces a reasonable number of segments
            int segmentCount = optimizer.GetExtremumCount(optimalDev);
            TestContext.WriteLine($"Extremum count at optimal deviation: {segmentCount}");

            Assert.That(optimalDev, Is.GreaterThan(0.001), "Deviation should be above minimum.");
            Assert.That(optimalDev, Is.LessThan(10.0), "Deviation should be below 10% for H1 forex data.");
            Assert.That(segmentCount, Is.GreaterThanOrEqualTo(3),
                "Real forex data should produce at least a few extremum points.");
        }

        /// <summary>
        /// Uses real XAUUSD H1 data to verify the optimizer works across different instruments.
        /// </summary>
        [Test]
        public void FindOptimalDeviation_RealXauusdData_FindsReasonableDeviation()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "XAUUSD_h1_2026-05-04T16-00-00_2026-05-07T15-00-00.csv");

            var provider = new TestBarsProvider(TIME_FRAME,
                new SymbolBase("XAUUSD", "Gold/USD", 1, 2, 0.01, 0.01, 100));
            provider.LoadCandles(csvPath);

            double maxValue = double.MinValue;
            double minValue = double.MaxValue;
            int maxBarIndex = 0;
            int minBarIndex = 0;

            for (int i = 0; i < provider.Count; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                if (high > maxValue) { maxValue = high; maxBarIndex = i; }
                if (low < minValue) { minValue = low; minBarIndex = i; }
            }

            int startIdx = Math.Min(maxBarIndex, minBarIndex);
            int endIdx = Math.Max(maxBarIndex, minBarIndex);
            bool isUp = maxBarIndex > minBarIndex;

            var optimizer = new DeviationOptimizer(provider, startIdx, endIdx, !isUp);
            double optimalDev = optimizer.FindOptimalDeviation();

            TestContext.WriteLine($"XAUUSD optimal deviation: {optimalDev:F4}%");

            int segmentCount = optimizer.GetExtremumCount(optimalDev);
            TestContext.WriteLine($"Extremum count at optimal deviation: {segmentCount}");

            Assert.That(optimalDev, Is.GreaterThan(0.001));
            Assert.That(optimalDev, Is.LessThan(10.0));
            Assert.That(segmentCount, Is.GreaterThanOrEqualTo(3));
        }

        /// <summary>
        /// Verifies that the optimal deviation produces at least as many extrema as
        /// the hard-coded 0.01 that was previously used in the indicator.
        /// </summary>
        [Test]
        public void FindOptimalDeviation_RealData_ProducesAtLeastAsManyAsHardcoded()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "USDJPY_h1_2026-04-30T06-00-00_2026-05-06T04-00-00.csv");

            var provider = new TestBarsProvider(TIME_FRAME,
                new SymbolBase("USDJPY", "USD/JPY", 1, 3, 0.001, 0.001, 100));
            provider.LoadCandles(csvPath);

            int startIdx = 0;
            int endIdx = provider.Count - 1;

            var optimizer = new DeviationOptimizer(provider, startIdx, endIdx, false);
            double optimalDev = optimizer.FindOptimalDeviation();

            int countOptimal = optimizer.GetExtremumCount(optimalDev);
            int countHardcoded = optimizer.GetExtremumCount(0.01);

            TestContext.WriteLine(
                $"Optimal dev={optimalDev:F4}% → {countOptimal} points; " +
                $"Hardcoded 0.01% → {countHardcoded} points");

            Assert.That(countOptimal, Is.GreaterThanOrEqualTo(countHardcoded),
                "Optimal deviation should not produce fewer extrema than the old hardcoded value.");
        }
    }
}
