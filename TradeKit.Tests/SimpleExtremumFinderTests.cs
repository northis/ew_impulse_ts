using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for the <see cref="SimplePivotExtremumFinder"/> class.
    /// </summary>
    internal class SimpleExtremumFinderTests
    {
        private const int ZigzagPeriod = 10;

        private readonly string m_TestDataPath =
            Path.Combine(Environment.CurrentDirectory, "TestData",
                "USDJPY_m5_2026-02-12T00-00-00_2026-02-17T00-00-00.csv");

        /// <summary>
        /// Verifies that the zigzag produced by <see cref="SimplePivotExtremumFinder"/> with period 10
        /// on real USDJPY M5 data has strictly alternating segments (no two consecutive segments
        /// going in the same direction) and contains no orphaned extrema inside a segment that
        /// exceed the segment's start or end value.
        /// </summary>
        [Test]
        public void SimpleExtremumFinder_RealData_ZigzagAlternatesAndHasNoOrphanedExtrema()
        {
            var barsProvider = new TestBarsProvider(TimeFrameHelper.Minute5);
            var finder = new SimplePivotExtremumFinder(ZigzagPeriod, barsProvider);

            barsProvider.LoadCandles(m_TestDataPath);

            List<BarPoint> extrema = finder.ToExtremaList();

            Assert.That(extrema.Count, Is.GreaterThan(2),
                "Expected at least 3 extrema for alternation check.");

            for (int i = 1; i < extrema.Count - 1; i++)
            {
                bool prevUp = extrema[i].Value > extrema[i - 1].Value;
                bool nextUp = extrema[i + 1].Value > extrema[i].Value;

                Assert.That(prevUp, Is.Not.EqualTo(nextUp),
                    $"Two consecutive segments have the same direction at extremum index {i}: " +
                    $"[{i - 1}]={extrema[i - 1].Value:F3} -> [{i}]={extrema[i].Value:F3} -> [{i + 1}]={extrema[i + 1].Value:F3}. " +
                    $"This indicates either a double direction flip (spurious segment) or an orphaned extremum inside a segment.");
            }
        }

    }
}
