using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    internal class MovementStatisticTests
    {
        private TestBarsProvider m_BarsProvider;
        private readonly ITimeFrame m_TimeFrame = new TimeFrameBase("Minute5", "m5");

        [SetUp]
        public void Setup()
        {
            m_BarsProvider = new TestBarsProvider(m_TimeFrame);
        }

        [Test]
        public void GetMovementStatistic_UniformUpwardMovement_ReturnsLowHeterogeneity()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create perfectly uniform upward movement (linear price increase)
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                
                // Create candles with minimal body and no wicks for uniform movement
                Candle candle = new Candle(basePrice, basePrice + 1, basePrice, basePrice + 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);

            // Act
            ImpulseResult result = MovementStatistic.GetMovementStatistic(start, end, m_BarsProvider);

            // Assert
            Assert.That(result.HeterogeneityDegree, Is.LessThan(0.1), "Heterogeneity should be low for uniform movement");
            Assert.That(result.HeterogeneityMax, Is.LessThan(0.1), "Max heterogeneity should be low for uniform movement");
            Assert.That(result.OverlapseDegree, Is.LessThan(0.1), "Overlapse degree should be low for uniform movement");
        }

        [Test]
        public void GetMovementStatistic_NonUniformUpwardMovement_ReturnsHighHeterogeneity()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create non-uniform upward movement with spikes
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                
                // Every third candle has a large spike
                double high = i % 3 == 0 ? basePrice + 20 : basePrice + 2;
                double low = i % 3 == 0 ? basePrice - 5 : basePrice - 1;
                
                Candle candle = new Candle(basePrice, basePrice + 1, low, high);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);

            // Act
            ImpulseResult result = MovementStatistic.GetMovementStatistic(start, end, m_BarsProvider);

            // Assert
            Assert.That(result.HeterogeneityDegree, Is.GreaterThan(0.3), "Heterogeneity should be high for non-uniform movement");
            Assert.That(result.HeterogeneityMax, Is.GreaterThan(0.5), "Max heterogeneity should be high for non-uniform movement");
        }

        [Test]
        public void GetMovementStatistic_OverlappingCandles_ReturnsHighOverlapseDegree()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create upward movement with significant overlapping candles
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 5; // Slower base price increase
                
                // Create overlapping candles - each candle's low is below previous candle's high
                double high = basePrice + 15; // Large range
                double low = Math.Max(95, basePrice - 15); // Ensure we don't go below 95
                
                Candle candle = new Candle(basePrice, basePrice + (i % 2 == 0 ? 2 : -2), low, high);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(145, 9, m_BarsProvider);

            // Act
            ImpulseResult result = MovementStatistic.GetMovementStatistic(start, end, m_BarsProvider);

            // Assert
            Assert.That(result.OverlapseDegree, Is.GreaterThan(0.5), "Overlapse degree should be high for overlapping candles");
            Assert.That(result.OverlapseMaxDepth, Is.GreaterThan(0.3), "Max overlapse depth should be significant");
        }

        [Test]
        public void GetMovementStatistic_SingleLargeCandle_ReturnsHighSingleCandleDegree()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create a series of candles where one candle is much larger than others
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 5;
                
                // Make the 5th candle very large
                if (i == 4)
                {
                    Candle candle = new Candle(basePrice, basePrice + 30, basePrice - 2, basePrice + 32);
                    m_BarsProvider.AddCandle(candle, openTime);
                }
                else
                {
                    Candle candle = new Candle(basePrice, basePrice + 1, basePrice - 1, basePrice + 2);
                    m_BarsProvider.AddCandle(candle, openTime);
                }
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(145, 9, m_BarsProvider);

            // Act
            ImpulseResult result = MovementStatistic.GetMovementStatistic(start, end, m_BarsProvider);

            // Assert
            Assert.That(result.SingleCandleDegree, Is.GreaterThan(0.5), "Single candle degree should be high when one candle dominates");
        }

        [Test]
        public void GetMovementStatistic_DownwardMovement_CorrectlyCalculatesStatistics()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create downward movement
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 200 - i * 10;
                
                Candle candle = new Candle(basePrice, basePrice - 1, basePrice - 2, basePrice + 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(200, 0, m_BarsProvider);
            BarPoint end = new BarPoint(110, 9, m_BarsProvider);

            // Act
            ImpulseResult result = MovementStatistic.GetMovementStatistic(start, end, m_BarsProvider);

            // Assert
            Assert.That(result.Size, Is.EqualTo(0.45).Within(0.01), "Size should be calculated correctly for downward movement");
            Assert.That(result.CandlesCount, Is.EqualTo(9), "Candles count should be correct");
        }

        [Test]
        public void GetMovementStatistic_ZeroMovement_ReturnsExpectedValues()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create flat movement (no change)
            for (int i = 0; i < 5; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100;
                
                Candle candle = new Candle(basePrice, basePrice, basePrice - 1, basePrice + 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(100, 4, m_BarsProvider);

            // Act
            ImpulseResult result = MovementStatistic.GetMovementStatistic(start, end, m_BarsProvider);

            // Assert
            Assert.That(result.Size, Is.EqualTo(0).Within(0.01), "Size should be zero for flat movement");
            Assert.That(result.HeterogeneityDegree, Is.EqualTo(1).Within(0.01), "Heterogeneity should be 1 for zero movement");
        }

        [Test]
        public void GetMovementStatistic_EdgeCase_SingleCandle_ReturnsExpectedValues()
        {
            // Arrange
            DateTime openTime = new DateTime(2023, 1, 1, 10, 0, 0);
            Candle candle = new Candle(100, 110, 95, 115);
            m_BarsProvider.AddCandle(candle, openTime);

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(110, 0, m_BarsProvider);

            // Act
            ImpulseResult result = MovementStatistic.GetMovementStatistic(start, end, m_BarsProvider);

            // Assert
            Assert.That(result.CandlesCount, Is.EqualTo(0), "Candles count should be 0 for single candle");
            Assert.That(result.SingleCandleDegree, Is.EqualTo(1).Within(0.01), "Single candle degree should be 1");
        }
    }
}
