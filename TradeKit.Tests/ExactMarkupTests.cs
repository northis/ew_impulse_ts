using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Json;
using TradeKit.Core.PatternGeneration;

namespace TradeKit.Tests
{
    public class ExactMarkupTests
    {
        private PatternGenerator m_PatternGenerator;
        private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Minute5;

        private class TestBarsProvider : IBarsProvider
        {
            private readonly List<JsonCandleExport> m_Candles;
            public TestBarsProvider(List<JsonCandleExport> candles)
            {
                m_Candles = candles;
            }

            public int Count => m_Candles.Count;
            public int StartIndexLimit => 0;
            public ITimeFrame TimeFrame => TIME_FRAME;
            public ISymbol BarSymbol => null;

            public event EventHandler BarClosed { add { } remove { } }
            public double GetClosePrice(int index) => m_Candles[index].C;
            public double GetHighPrice(int index) => m_Candles[index].H;
            public int GetIndexByTime(DateTime dateTime) => m_Candles.FindIndex(c => c.OpenDate == dateTime);
            public double GetLowPrice(int index) => m_Candles[index].L;
            public double GetMedianPrice(int index) => (m_Candles[index].H + m_Candles[index].L) / 2;
            public double GetOpenPrice(int index) => m_Candles[index].O;
            public DateTime GetOpenTime(int index) => m_Candles[index].OpenDate;
            public void LoadBars(DateTime date) { }
            public void Dispose() { }
        }

        [SetUp]
        public void Setup()
        {
            m_PatternGenerator = new PatternGenerator(true);
        }

        [Test]
        public void ImpulseExactMarkupTest()
        {
            int totalTests = 10;
            int matchedTests = 0;

            for (int i = 0; i < totalTests; i++)
            {
                (DateTime start, DateTime end) = Helper.GetDateRange(100, TIME_FRAME);
                PatternArgsItem paramArgs = new PatternArgsItem(40, 60, start, end, TIME_FRAME);
                ModelPattern model = m_PatternGenerator.GetPattern(paramArgs, ElliottModelType.IMPULSE, false);

                var barsProvider = new TestBarsProvider(model.Candles);
                var markup = new ElliottWaveExactMarkup();

                var keyPoints = model.PatternKeyPoints.SelectMany(x => x.Value).OrderBy(x => x.Index).ToList();
                if (keyPoints.Count < 2) continue;

                var points = new List<BarPoint>();
                var mainWaves = model.PatternKeyPoints.SelectMany(x => x.Value).Where(x => x.Notation.Level == model.Level).OrderBy(x => x.Index).ToList();
                var startCandle = model.Candles[0];
                var endNode = mainWaves.Last();
                bool isUp = endNode.Value > startCandle.O;
                double startVal = isUp ? model.Candles.Take(mainWaves[0].Index).Min(x => x.L) : model.Candles.Take(mainWaves[0].Index).Max(x => x.H);
                int startIndex = model.Candles.FindIndex(x => x.L == startVal || x.H == startVal);
                if (startIndex == -1) startIndex = 0;
                points.Add(new BarPoint(startVal, model.Candles[startIndex].OpenDate, barsProvider));

                var distinctPoints = keyPoints.Where(x => x.Index > startIndex && x.Index <= endNode.Index)
                    .GroupBy(x => x.Index).Select(g => g.OrderByDescending(x => x.Notation.Level).First()).OrderBy(x => x.Index).ToList();
                
                foreach (var kp in distinctPoints)
                {
                    points.Add(new BarPoint(kp.Value, model.Candles[kp.Index].OpenDate, barsProvider));
                }

                var results = markup.Parse(points);
                var bestResult = results.FirstOrDefault();

                if (bestResult != null && bestResult.ModelType == ElliottModelType.IMPULSE && bestResult.WaveCount == bestResult.ExpectedWaves)
                {
                    matchedTests++;
                }
                else if (results.Count == 0 || results[0].ModelType != ElliottModelType.IMPULSE || results[0].WaveCount != 5)
                {
                    Console.WriteLine($"Test {i} failed. Best model: {results.FirstOrDefault()?.ModelType} Score: {results.FirstOrDefault()?.Score} WaveCount: {results.FirstOrDefault()?.WaveCount}");
                }
                else
                {
                    Console.WriteLine($"Test {i} failed. Best model: {bestResult?.ModelType} Score: {bestResult?.Score} WaveCount: {bestResult?.WaveCount}");
                }
            }

            Assert.IsTrue(matchedTests >= 6, $"Only {matchedTests} out of {totalTests} matched the IMPULSE model using ExactMarkup.");
        }
    }
}
