using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    [TestFixture]
    public class CandleSaveLoadTests
    {
        private TestBarsProvider m_SourceProvider;
        private TestBarsProvider m_TargetProvider;
        private readonly ITimeFrame m_TimeFrame = new TimeFrameBase("Minute5", "m5");
        private string m_TempDirectory;
        
        [SetUp]
        public void Setup()
        {
            // Create a temporary directory for our test files
            m_TempDirectory = Path.Combine(Path.GetTempPath(), "TradeKitTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(m_TempDirectory);
            
            // Create test providers
            m_SourceProvider = new TestBarsProvider(m_TimeFrame);
            m_TargetProvider = new TestBarsProvider(m_TimeFrame);
            
            // Populate source provider with sample data
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            for (int i = 0; i < 10; i++)
            {
                DateTime openTime = startTime.AddMinutes(i * 5);
                double basePrice = 100 + i * 5.5;
                
                // Create sample candles with varied OHLC values
                Candle candle = new Candle(
                    basePrice,                 // Open
                    basePrice + 2 + i * 0.2,   // High
                    basePrice - 1 - i * 0.1,   // Low
                    basePrice + 1 - i * 0.05   // Close
                );
                m_SourceProvider.AddCandle(candle, openTime);
            }
        }
        
        [TearDown]
        public void Cleanup()
        {
            // Clean up the temporary directory after the test
            if (Directory.Exists(m_TempDirectory))
            {
                try
                {
                    Directory.Delete(m_TempDirectory, true);
                }
                catch (IOException)
                {
                    // Ignore errors on cleanup
                    TestContext.WriteLine($"Warning: Failed to clean up temporary directory: {m_TempDirectory}");
                }
            }
        }
        
        [Test]
        public void SaveAndLoadCandles_MatchesOriginalData()
        {
            // Arrange
            string filePath = Path.Combine(m_TempDirectory, "testCandles.csv");
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            DateTime endTime = startTime.AddMinutes(9 * 5); // Last candle
            
            // Act
            // 1. Save candles from source provider
            m_SourceProvider.SaveCandles(startTime, endTime, filePath);
            
            // 2. Load candles into target provider
            m_TargetProvider.LoadCandles(filePath);
            
            // Assert
            // Verify file exists and has content
            Assert.That(File.Exists(filePath), Is.True, "Candle file should exist");
            string[] fileContent = File.ReadAllLines(filePath);
            Assert.That(fileContent.Length, Is.EqualTo(m_SourceProvider.Count + 1), "File should contain header + all candles");
            
            // Verify the first line is a header
            Assert.That(fileContent[0], Does.Contain("Time"), "First line should be a header");
            Assert.That(fileContent[0], Does.Contain("Open"), "Header should contain Open column");
            
            // Verify candle data is properly loaded
            Assert.That(m_TargetProvider.Count, Is.EqualTo(m_SourceProvider.Count), "Target provider should have same number of candles");
            
            // Compare candles
            for (int i = 0; i < m_SourceProvider.Count; i++)
            {
                Assert.That(m_TargetProvider.GetOpenTime(i), Is.EqualTo(m_SourceProvider.GetOpenTime(i)), $"OpenTime mismatch at index {i}");
                Assert.That(m_TargetProvider.GetOpenPrice(i), Is.EqualTo(m_SourceProvider.GetOpenPrice(i)).Within(0.0001), $"Open price mismatch at index {i}");
                Assert.That(m_TargetProvider.GetHighPrice(i), Is.EqualTo(m_SourceProvider.GetHighPrice(i)).Within(0.0001), $"High price mismatch at index {i}");
                Assert.That(m_TargetProvider.GetLowPrice(i), Is.EqualTo(m_SourceProvider.GetLowPrice(i)).Within(0.0001), $"Low price mismatch at index {i}");
                Assert.That(m_TargetProvider.GetClosePrice(i), Is.EqualTo(m_SourceProvider.GetClosePrice(i)).Within(0.0001), $"Close price mismatch at index {i}");
            }
            
            // Dump file content to test output for debugging if needed
            TestContext.WriteLine($"CSV file content ({fileContent.Length} lines):");
            foreach (string line in fileContent.Take(5))
            {
                TestContext.WriteLine(line);
            }
            if (fileContent.Length > 5) TestContext.WriteLine("...");
        }
    }
}
