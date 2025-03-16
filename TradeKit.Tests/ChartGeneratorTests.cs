using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for the ChartGenerator class.
    /// </summary>
    internal class ChartGeneratorTests
    {
        private TestBarsProvider m_BarsProvider;
        private readonly ITimeFrame m_TimeFrame = new TimeFrameBase("Minute5", "m5");

        [SetUp]
        public void Setup()
        {
            m_BarsProvider = new TestBarsProvider(m_TimeFrame);
        }

        [Test]
        public void GenerateCandlestickChart_UniformMovement_CreatesChart()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create perfectly linear price movement
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 10;
                
                // Create candles with exact linear progression
                Candle candle = new Candle(basePrice, basePrice + 1, basePrice - 1, basePrice + 1);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);

            // Act
            string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);

            // Assert
            Assert.That(chartPath, Is.Not.Null);
            Assert.That(File.Exists(chartPath), Is.True, "Chart file should be created");
            TestContext.WriteLine($"Chart saved to: {chartPath}");
        }

        [Test]
        public void GenerateCandlestickChart_NonUniformMovement_CreatesChart()
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
                double high = basePrice + Math.Abs(deviation) + 5;
                double low = basePrice - 5;
                
                Candle candle = new Candle(basePrice, high, low, basePrice + deviation);
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(190, 9, m_BarsProvider);

            // Act
            string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);

            // Assert
            Assert.That(chartPath, Is.Not.Null);
            Assert.That(File.Exists(chartPath), Is.True, "Chart file should be created");
            TestContext.WriteLine($"Chart saved to: {chartPath}");
        }

        [Test]
        public void GenerateCandlestickChart_OverlappingCandles_CreatesChart()
        {
            // Arrange
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            
            // Create movement with significant overlapping candles
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 5; // Slower increase
                
                // Create overlapping pattern - candles have large ranges that overlap
                double high = basePrice + 20;
                double low = Math.Max(90, basePrice - 15);
                
                Candle candle = new Candle(basePrice, high, low, basePrice + (i % 2 == 0 ? 3 : -3));
                m_BarsProvider.AddCandle(candle, openTime);
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(145, 9, m_BarsProvider);

            // Act
            string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);

            // Assert
            Assert.That(chartPath, Is.Not.Null);
            Assert.That(File.Exists(chartPath), Is.True, "Chart file should be created");
            TestContext.WriteLine($"Chart saved to: {chartPath}");
        }

        [Test]
        public void GenerateCandlestickChart_SingleDominantCandle_CreatesChart()
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
                    Candle candle = new Candle(basePrice, basePrice + 42, basePrice - 2, basePrice + 40);
                    m_BarsProvider.AddCandle(candle, openTime);
                }
                else
                {
                    // Other small candles
                    Candle candle = new Candle(basePrice, basePrice + 2, basePrice - 1, basePrice + 1);
                    m_BarsProvider.AddCandle(candle, openTime);
                }
            }

            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(148, 4, m_BarsProvider);

            // Act
            string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);

            // Assert
            Assert.That(chartPath, Is.Not.Null);
            Assert.That(File.Exists(chartPath), Is.True, "Chart file should be created");
            TestContext.WriteLine($"Chart saved to: {chartPath}");
        }
    }
}
