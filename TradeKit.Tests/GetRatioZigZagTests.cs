using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for <see cref="MovementStatistic.GetRatioZigZag"/> to verify
    /// the refactored version produces the same results as the third value of <see cref="MovementStatistic.GetMaxOverlapseScore"/>.
    /// </summary>
    internal class GetRatioZigZagTests
    {
        private TestBarsProvider m_BarsProvider;
        private readonly ITimeFrame m_TimeFrame = new TimeFrameBase("Minute5", "m5");

        [SetUp]
        public void Setup()
        {
            m_BarsProvider = new TestBarsProvider(m_TimeFrame);
        }

        [Test]
        public void GetRatioZigZag_UniformUpwardMovement_MatchesGetMaxOverlapseScore()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                Candle candle = new Candle(basePrice, basePrice + 1, basePrice, basePrice + 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);

            // Act
            double ratioZigZag = MovementStatistic.GetRatioZigZag(start, end, m_BarsProvider);
            (_, _, double expected, _) = MovementStatistic.GetMaxOverlapseScore(start, end, m_BarsProvider);

            // Assert
            Assert.That(ratioZigZag, Is.EqualTo(expected).Within(1e-10),
                "GetRatioZigZag should match the third value of GetMaxOverlapseScore for uniform upward movement");
        }

        [Test]
        public void GetRatioZigZag_NonUniformUpwardMovement_MatchesGetMaxOverlapseScore()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                double high = i % 3 == 0 ? basePrice + 20 : basePrice + 2;
                double low = i % 3 == 0 ? basePrice - 5 : basePrice - 1;
                Candle candle = new Candle(basePrice, high, low, basePrice + 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);

            // Act
            double ratioZigZag = MovementStatistic.GetRatioZigZag(start, end, m_BarsProvider);
            (_, _, double expected, _) = MovementStatistic.GetMaxOverlapseScore(start, end, m_BarsProvider);

            // Assert
            Assert.That(ratioZigZag, Is.EqualTo(expected).Within(1e-10),
                "GetRatioZigZag should match the third value of GetMaxOverlapseScore for non-uniform movement");
        }

        [Test]
        public void GetRatioZigZag_OverlappingCandles_MatchesGetMaxOverlapseScore()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 5;
                double high = basePrice + 15;
                double low = Math.Max(95, basePrice - 15);
                Candle candle = new Candle(basePrice, high, low, basePrice + (i % 2 == 0 ? 2 : -2));
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(145, 9, m_BarsProvider);

            // Act
            double ratioZigZag = MovementStatistic.GetRatioZigZag(start, end, m_BarsProvider);
            (_, _, double expected, _) = MovementStatistic.GetMaxOverlapseScore(start, end, m_BarsProvider);

            // Assert
            Assert.That(ratioZigZag, Is.EqualTo(expected).Within(1e-10),
                "GetRatioZigZag should match the third value of GetMaxOverlapseScore for overlapping candles");
        }

        [Test]
        public void GetRatioZigZag_DownwardMovement_MatchesGetMaxOverlapseScore()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 200 - i * 10;
                Candle candle = new Candle(basePrice, basePrice + 1, basePrice - 2, basePrice - 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(200, 0, m_BarsProvider);
            BarPoint end = new BarPoint(110, 9, m_BarsProvider);

            // Act
            double ratioZigZag = MovementStatistic.GetRatioZigZag(start, end, m_BarsProvider);
            (_, _, double expected, _) = MovementStatistic.GetMaxOverlapseScore(start, end, m_BarsProvider);

            // Assert
            Assert.That(ratioZigZag, Is.EqualTo(expected).Within(1e-10),
                "GetRatioZigZag should match the third value of GetMaxOverlapseScore for downward movement");
        }

        [Test]
        public void GetRatioZigZag_ZeroMovement_ReturnsOne()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            for (int i = 0; i < 5; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                Candle candle = new Candle(100, 101, 99, 100);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(100, 4, m_BarsProvider);

            // Act
            double ratioZigZag = MovementStatistic.GetRatioZigZag(start, end, m_BarsProvider);

            // Assert
            Assert.That(ratioZigZag, Is.EqualTo(1).Within(1e-10),
                "GetRatioZigZag should return 1 for zero-length movement");
        }

        [Test]
        public void GetRatioZigZag_SingleCandle_ReturnsZero()
        {
            // Arrange
            DateTime openTime = new DateTime(2023, 1, 1, 10, 0, 0);
            Candle candle = new Candle(100, 115, 95, 110);
            m_BarsProvider.AddCandle(candle, openTime);

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(110, 0, m_BarsProvider);

            // Act
            double ratioZigZag = MovementStatistic.GetRatioZigZag(start, end, m_BarsProvider);

            // Assert
            Assert.That(ratioZigZag, Is.EqualTo(0).Within(1e-10),
                "GetRatioZigZag should return 0 for single candle (duration=0)");
        }

        [Test]
        public void GetRatioZigZag_GShapedMovement_MatchesGetMaxOverlapseScore()
        {
            // Arrange: first candle makes a big move, rest are flat (Г-shaped)
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);

            // First candle: big upward spike
            m_BarsProvider.AddCandle(new Candle(100, 180, 100, 170), startTime);

            // Remaining candles: small increments near the top
            for (int i = 1; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 170 + i * 2;
                Candle candle = new Candle(basePrice, basePrice + 1, basePrice - 1, basePrice + 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(188, 9, m_BarsProvider);

            // Act
            double ratioZigZag = MovementStatistic.GetRatioZigZag(start, end, m_BarsProvider);
            (_, _, double expected, _) = MovementStatistic.GetMaxOverlapseScore(start, end, m_BarsProvider);

            // Assert
            Assert.That(ratioZigZag, Is.EqualTo(expected).Within(1e-10),
                "GetRatioZigZag should match the third value of GetMaxOverlapseScore for Г-shaped movement");
        }

        [Test]
        public void GetRatioZigZag_WithRateZigzagMaxLimit_MatchesGetMaxOverlapseScore()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                double high = i % 3 == 0 ? basePrice + 20 : basePrice + 2;
                double low = i % 3 == 0 ? basePrice - 5 : basePrice - 1;
                Candle candle = new Candle(basePrice, high, low, basePrice + 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);
            double limit = 0.5;

            // Act
            double ratioZigZag = MovementStatistic.GetRatioZigZag(start, end, m_BarsProvider, limit);
            (_, _, double expected, _) = MovementStatistic.GetMaxOverlapseScore(start, end, m_BarsProvider, rateZigzagMaxLimit: limit);

            // Assert
            Assert.That(ratioZigZag, Is.EqualTo(expected).Within(1e-10),
                "GetRatioZigZag should match the third value of GetMaxOverlapseScore when using rateZigzagMaxLimit");
        }
    }
}
