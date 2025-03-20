using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for individual components of the MovementStatistic class.
    /// </summary>
    internal class MovementStatisticComponentTests
    {
        private TestBarsProvider m_BarsProvider;
        private readonly ITimeFrame m_TimeFrame = new TimeFrameBase("Minute5", "m5");

        [SetUp]
        public void Setup()
        {
            m_BarsProvider = new TestBarsProvider(m_TimeFrame);
        }

        [Test]
        public void GetHeterogeneity_PerfectlyUniform_ReturnsMinimalHeterogeneity()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create perfectly linear price movement
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                
                // Create candles with exact linear progression
                // Ensure high > low to avoid null candles
                Candle candle = new Candle(basePrice, basePrice + 1, basePrice - 1, basePrice + 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);

            // Act
            (double heterogeneity, double heterogeneityMax) = MovementStatistic.GetHeterogeneity(start, end, m_BarsProvider);

            // Assert
            Assert.That(heterogeneity, Is.LessThan(0.05), "Heterogeneity should be minimal for perfectly uniform movement");
            Assert.That(heterogeneityMax, Is.LessThan(0.1), "Max heterogeneity should be minimal for perfectly uniform movement");
        }

        [Test]
        public void GetHeterogeneity_HighlyNonUniform_ReturnsHighHeterogeneity()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create highly non-uniform movement with large deviations
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                
                // Add significant deviations from the linear progression
                double deviation = i % 2 == 0 ? 30 : -20;
                // Ensure high > low to avoid null candles
                double high = basePrice + Math.Abs(deviation) + 5;
                double low = basePrice - 5;
                
                Candle candle = new Candle(basePrice, high, low, basePrice + deviation);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);

            // Act
            (double heterogeneity, double heterogeneityMax) = MovementStatistic.GetHeterogeneity(start, end, m_BarsProvider);
            //ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);

            // Assert
            Assert.That(heterogeneity, Is.GreaterThan(0.3), "Heterogeneity should be high for non-uniform movement");
            Assert.That(heterogeneityMax, Is.GreaterThan(0.5), "Max heterogeneity should be high for non-uniform movement");
        }

        [Test]
        public void GetHeterogeneity_ZeroLength_ReturnsMaximumHeterogeneity()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create flat movement
            for (int i = 0; i < 5; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                // Ensure high > low to avoid null candles
                Candle candle = new Candle(100, 101, 99, 100);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(100, 4, m_BarsProvider);

            // Act
            (double heterogeneity, double heterogeneityMax) = MovementStatistic.GetHeterogeneity(start, end, m_BarsProvider);

            // Assert
            Assert.That(heterogeneity, Is.EqualTo(1).Within(0.01), "Heterogeneity should be 1 for zero length movement");
            Assert.That(heterogeneityMax, Is.EqualTo(1).Within(0.01), "Max heterogeneity should be 1 for zero length movement");
        }

        [Test]
        public void GetMaxOverlapseScore_NoOverlaps_ReturnsZeroOverlapse()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create movement with no overlapping candles
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                
                // Each candle's low is higher than previous candle's high
                // Ensure high > low to avoid null candles
                Candle candle = new Candle(basePrice, basePrice + 5, basePrice - 1, basePrice + 2);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);

            // Act
            (double overlapseMaxDepth, double overlapseMaxDistance) = MovementStatistic.GetMaxOverlapseScore(start, end, m_BarsProvider);

            // Assert
            Assert.That(overlapseMaxDepth, Is.EqualTo(0).Within(0.01), "Overlapse depth should be 0 for non-overlapping candles");
            Assert.That(overlapseMaxDistance, Is.EqualTo(0).Within(0.01), "Overlapse distance should be 0 for non-overlapping candles");
        }

        [Test]
        public void GetMaxOverlapseScore_SignificantOverlaps_ReturnsHighOverlapse()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);

            // Create movement with significant overlapping candles
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 5; // Slower increase

                // Create overlapping pattern - candles have large ranges that overlap
                // Ensure high > low to avoid null candles
                double high = basePrice + 20;
                double low = Math.Max(90, basePrice - 15);

                Candle candle = new Candle(basePrice, high, low, basePrice + (i % 2 == 0 ? 3 : -3));
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(145, 9, m_BarsProvider);

            // Act
            (double overlapseMaxDepth, double overlapseMaxDistance) = MovementStatistic.GetMaxOverlapseScore(start, end, m_BarsProvider);
            
            // Assert
            Assert.That(overlapseMaxDepth, Is.GreaterThan(0.3), "Overlapse depth should be significant for overlapping candles");
            Assert.That(overlapseMaxDistance, Is.GreaterThan(0.1), "Overlapse distance should be significant for overlapping candles");
        }

        [Test]
        public void GetOverlapseStatistic_MostlySingleCandle_ReturnsHighSingleCandleDegree()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create a series where one candle dominates the movement
            for (int i = 0; i < 5; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 2;
                
                if (i == 2)
                {
                    // One very large candle
                    // Ensure high > low to avoid null candles
                    Candle candle = new Candle(basePrice, basePrice + 42, basePrice - 2, basePrice + 40);
                    m_BarsProvider.AddCandle(candle, openTime);
                }
                else
                {
                    // Other small candles
                    // Ensure high > low to avoid null candles
                    Candle candle = new Candle(basePrice, basePrice + 2, basePrice - 1, basePrice + 1);
                    m_BarsProvider.AddCandle(candle, openTime);
                }
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(148, 4, m_BarsProvider);

            // Act
            (var profile, double overlapseDegree, double singleCandle) = MovementStatistic.GetOverlapseStatistic(start, end, m_BarsProvider);

            // Assert
            Assert.That(singleCandle, Is.GreaterThan(0.7), "Single candle degree should be high when one candle dominates");
            Assert.That(profile.Count, Is.GreaterThan(0), "Profile should contain price levels");
        }

        [Test]
        public void GetOverlapseStatistic_EvenDistribution_ReturnsLowSingleCandleDegree()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create a series with evenly distributed candles
            for (int i = 0; i < 5; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                
                // All candles are similar size
                // Ensure high > low to avoid null candles
                Candle candle = new Candle(basePrice, basePrice + 7, basePrice - 2, basePrice + 5);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(145, 4, m_BarsProvider);
            //string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);
            //TestContext.WriteLine($"Chart saved to: {chartPath}");

            // Act
            (var profile, double overlapseDegree, double singleCandle) = MovementStatistic.GetOverlapseStatistic(start, end, m_BarsProvider);

            // Assert
            Assert.That(singleCandle, Is.LessThan(0.3), "Single candle degree should be low for evenly distributed candles");
        }
    }
}
