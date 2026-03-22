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
    public class PatternGenMarkupTests
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
        public void ImpulseMarkupTest()
        {
            int totalTests = 10;
            int matchedTests = 0;

            for (int i = 0; i < totalTests; i++)
            {
                (DateTime start, DateTime end) = Helper.GetDateRange(200, TIME_FRAME);
                PatternArgsItem paramArgs = new PatternArgsItem(40, 60, start, end, TIME_FRAME);
                ModelPattern model = m_PatternGenerator.GetPattern(paramArgs, ElliottModelType.IMPULSE, false);

                var barsProvider = new TestBarsProvider(model.Candles);
                var markup = new ElliottWaveMarkup();

                var mainWaves = model.PatternKeyPoints.SelectMany(x => x.Value).Where(x => x.Notation.Level == model.Level).OrderBy(x => x.Index).ToList();
                if (mainWaves.Count < 2) 
                {
                    continue; // Invalid pattern
                }

                var startCandle = model.Candles[0];
                var endNode = mainWaves.Last();

                bool isUp = endNode.Value > startCandle.O;
                double startVal = isUp ? model.Candles.Take(mainWaves[0].Index).Min(x => x.L) : model.Candles.Take(mainWaves[0].Index).Max(x => x.H);
                int startIndex = model.Candles.FindIndex(x => Math.Abs(x.L - startVal) < double.Epsilon || Math.Abs(x.H - startVal) < double.Epsilon);
                if (startIndex == -1) startIndex = 0;

                var startPoint = new BarPoint(startVal, model.Candles[startIndex].OpenDate, barsProvider);
                var endPoint = new BarPoint(endNode.Value, model.Candles[endNode.Index].OpenDate, barsProvider);

                var ranks = new Dictionary<int, (BarPoint Point, int Rank)>();
                foreach (var kpList in model.PatternKeyPoints.Values)
                {
                    foreach (var kp in kpList)
                    {
                        if (kp.Index > startIndex && kp.Index < endNode.Index)
                        {
                            if (!ranks.ContainsKey(kp.Index))
                            {
                                var point = new BarPoint(kp.Value, model.Candles[kp.Index].OpenDate, barsProvider);
                                ranks[kp.Index] = (point, kp.Notation.Level + 1);
                            }
                            else if (kp.Notation.Level + 1 < ranks[kp.Index].Rank)
                            {
                                ranks[kp.Index] = (ranks[kp.Index].Point, kp.Notation.Level + 1);
                            }
                        }
                    }
                }

                var results = markup.ParseSegment(startPoint, endPoint, ranks, 3);
                var bestResult = results.FirstOrDefault();

                if (bestResult is { ModelType: ElliottModelType.IMPULSE })
                {
                    matchedTests++;
                }
                else
                {
                    Console.WriteLine($"Test {i} failed. Best model: {bestResult?.ModelType} Score: {bestResult?.Score}");
                    foreach (var res in results)
                    {
                        Console.WriteLine($"  Alt model: {res.ModelType} Score: {res.Score}");
                        if (res.ModelType == ElliottModelType.IMPULSE)
                        {
                            Console.WriteLine($"    IMPULSE WAS FOUND WITH SCORE: {res.Score}");
                        }
                    }
                }
            }

            Assert.IsTrue(matchedTests >= 6, $"Only {matchedTests} out of {totalTests} matched the IMPULSE model. Requirement is at least 6.");
        }
    }
}
