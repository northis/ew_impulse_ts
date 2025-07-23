using NUnit.Framework;
using System;
using System.Collections.Generic;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.Core.Json;
using TradeKit.Core.PatternGeneration;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for the TriangleSetupFinder class.
    /// </summary>
    internal class TriangleSetupFinderTests
    {
        private readonly string m_TestDataPath =
            Path.Combine(Environment.CurrentDirectory, "TestData",
                "TriangleSetupFinderTests_001.csv");
        
        /// <summary>
        /// Generates triangle pattern candles using PatternGenerator and tests IsSetup invocation.
        /// </summary>
        [Test]
        public void TriangleSetupFinder_IsSetup_Invocation()
        {
            // Arrange: Generate triangle pattern candles
            /*var patternGenerator = new PatternGenerator(true);
            (DateTime, DateTime) dates = Helper.GetDateRange(100, TimeFrameHelper.Minute5);
            (DateTime, DateTime) datesImp = Helper.GetDateRange(120, TimeFrameHelper.Minute5);
            ModelPattern? model = patternGenerator.GetPattern(
                new PatternArgsItem(90, 80, dates.Item1, dates.Item2, TimeFrameHelper.Minute5)
                { Min = 60, Max = 90}, ElliottModelType.TRIANGLE_CONTRACTING);

            PatternArgsItem paramArgs = new PatternArgsItem(
                40, 90,  dates.Item1.Subtract(TimeFrameHelper
                                            .TimeFrames[TimeFrameHelper.Minute5.Name].TimeSpan * 200),
               datesImp.Item1,
                TimeFrameHelper.Minute5);
            ModelPattern modelImp = patternGenerator.GetPattern(
                paramArgs, ElliottModelType.ZIGZAG);

            // Convert generated candles to (Candle, DateTime) tuples
            var testCandles = new List<(Candle candle, DateTime openTime)>();
            foreach (JsonCandleExport? candleExport in modelImp.Candles)
            {
                var candle = new Candle(candleExport.O, candleExport.H, candleExport.L, candleExport.C);
                testCandles.Add((candle, candleExport.OpenDate));
            }
            
            foreach (JsonCandleExport? candleExport in model.Candles)
            {
                var candle = new Candle(candleExport.O, candleExport.H, candleExport.L, candleExport.C);
                testCandles.Add((candle, candleExport.OpenDate));
            }*/

            // Use TestBarsProvider to add candles
            var barsProvider = new TestBarsProvider(TimeFrameHelper.Minute5);

            // Prepare EWParams and TriangleSetupFinder
            var ewParams = new EWParams(30, 10, 10);
            var triangleFinder = new TriangleSetupFinder(barsProvider, barsProvider.BarSymbol, ewParams);

            barsProvider.BarClosed += (bp, args) =>
            {
                triangleFinder.CheckBar(
                    barsProvider.GetOpenTime(barsProvider.Count - 1));
            };

            triangleFinder.OnEnter += (_, args) =>
            {

            };
            
            triangleFinder.MarkAsInitialized();
            barsProvider.LoadCandles(m_TestDataPath);
            
            // barsProvider.AddCandles(testCandles);
            // barsProvider.SaveCandles(barsProvider.GetOpenTime(0),
            //     barsProvider.GetOpenTime(barsProvider.Count - 1),
            //     m_TestDataPath);
            // string chartPath = ChartGenerator.GenerateCandlestickChart(
            //     new BarPoint(0, barsProvider),
            //     new BarPoint(barsProvider.Count - 1, barsProvider),
            //     barsProvider);

            // Assert: Just ensure invocation, not result (method is not finished)
            //Assert.Pass("IsSetup was invoked without exceptions. Result: " + (result ?? "null"));
        }
    }
}
