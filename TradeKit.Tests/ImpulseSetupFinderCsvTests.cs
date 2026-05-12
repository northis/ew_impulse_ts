using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for the ImpulseSetupFinder class using real CSV candle data.
    /// </summary>
    internal class ImpulseSetupFinderCsvTests
    {
        private TestBarsProvider m_BarsProvider;
        private ImpulseSetupFinder m_SetupFinder;
        private readonly ITimeFrame m_TimeFrame = new TimeFrameBase("Minute", "m1");
        private readonly ISymbol m_Symbol = new SymbolBase("XAUUSD", "XAUUSD", 1, 2, 0.01, 0.01, 100);
        private List<ImpulseSignalEventArgs> m_ReceivedSignals;
        private List<LevelEventArgs> m_TakeProfitEvents;
        private List<LevelEventArgs> m_StopLossEvents;
        private List<LevelEventArgs> m_BreakEvenEvents;

        [SetUp]
        public void Setup()
        {
            m_BarsProvider = new TestBarsProvider(m_TimeFrame, m_Symbol);

            ImpulseParams impulseParams = new ImpulseParams(Period: 15,
                EnterRatio: 0.5,
                TakeRatio: 1.0,
                MaxZigzagPercent: 18,
                MaxOverlapseLengthPercent: 24,
                HeterogeneityMax: 64,
                BreakEvenRatio: 0.5,
                MinSizePercent: 0.13,
                AreaPercent: 30,
                BarsCount: 30,
                MaxDistance: 28);

            m_SetupFinder = new ImpulseSetupFinder(m_BarsProvider, new TestTradeViewManager(m_BarsProvider), impulseParams);

            m_ReceivedSignals = new List<ImpulseSignalEventArgs>();
            m_TakeProfitEvents = new List<LevelEventArgs>();
            m_StopLossEvents = new List<LevelEventArgs>();
            m_BreakEvenEvents = new List<LevelEventArgs>();

            m_SetupFinder.OnEnter += (_, args) => m_ReceivedSignals.Add(args);
            m_SetupFinder.OnTakeProfit += (_, args) => m_TakeProfitEvents.Add(args);
            m_SetupFinder.OnStopLoss += (_, args) => m_StopLossEvents.Add(args);
            m_SetupFinder.OnBreakeven += (_, args) => m_BreakEvenEvents.Add(args);
            m_SetupFinder.MarkAsInitialized();
            m_BarsProvider.BarClosed += (_, _) =>
            {
                var dt = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
                if (dt is { Hour: 4, Minute:  6 })
                {
                    
                }
                m_SetupFinder.CheckBar(dt);
            };
        }

        /// <summary>
        /// Runs XAUUSD m1 candles from the CSV through ImpulseSetupFinder for debugging purposes.
        /// </summary>
        [Test]
        public void ImpulseSetupFinder_XauusdM1_RunsWithoutError()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "XAUUSD_m1_2025-08-01T00-30-00_2025-08-01T05-30-00.csv");

            m_BarsProvider.LoadCandles(csvPath);

            TestContext.WriteLine($"Bars loaded: {m_BarsProvider.Count}");
            TestContext.WriteLine($"Signals received: {m_ReceivedSignals.Count}");
            TestContext.WriteLine($"Take profit events: {m_TakeProfitEvents.Count}");
            TestContext.WriteLine($"Stop loss events: {m_StopLossEvents.Count}");
            TestContext.WriteLine($"Break even events: {m_BreakEvenEvents.Count}");

            foreach (ImpulseSignalEventArgs signal in m_ReceivedSignals)
            {
                TestContext.WriteLine(
                    $"Signal: time={signal.Level.OpenTime:O} entry={signal.Level.Value} " +
                    $"tp={signal.TakeProfit.Value} sl={signal.StopLoss.Value}");
            }
        }
    
        
        [Test]
        public void ExactMarkup_XauusdM1_ParsesSuccessfully()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "XAUUSD_m1_2025-08-01T00-30-00_2025-08-01T05-30-00.csv");
            
            m_BarsProvider.LoadCandles(csvPath);

            var extremumFinder = new TradeKit.Core.Indicators.SimpleExtremumFinder(0.01, m_BarsProvider);
            for (int i = 0; i < m_BarsProvider.Count; i++)
            {
                extremumFinder.Calculate(m_BarsProvider.GetOpenTime(i));
            }

            List<BarPoint> allPoints = extremumFinder.Extrema.Values.ToList();
            TestContext.WriteLine($"Extremum points found: {allPoints.Count}");

            if (allPoints.Count < 2) return;

            var markup = new TradeKit.Core.AlgoBase.ElliottWaveExactMarkup();
            
            BarPoint start = allPoints[0];
            BarPoint end = allPoints[^1];
            bool isUp = end.Value > start.Value;
            
            var innerFinder = new TradeKit.Core.Indicators.SimpleExtremumFinder(0.01, m_BarsProvider, !isUp);
            innerFinder.Calculate(start.BarIndex, end.BarIndex);
                
            List<BarPoint> innerPoints = innerFinder.ToExtremaList()
                .Where(p => p.BarIndex >= start.BarIndex && p.BarIndex <= end.BarIndex)
                .ToList();
                
            if (innerPoints.All(p => p.BarIndex != start.BarIndex))
            {
                innerPoints.Insert(0, start);
            }

            if (innerPoints.All(p => p.BarIndex != end.BarIndex))
            {
                innerPoints.Add(end);
            }

            var results = markup.Parse(innerPoints);

            TestContext.WriteLine($"Found {results.Count} possible markups.");

            if (results.Count > 0)
            {
                var best = results[0];
                TestContext.WriteLine($"Best Model: {best.ModelType}, Score: {best.Score}, StartIndex: {best.StartIndex}, EndIndex: {best.EndIndex}");
                Assert.That(best.Score, Is.GreaterThan(0));
            }
        }

        /// <summary>
        /// Reproduces the indicator flow for EURUSD h1 data where the chart showed
        /// a diagonal with wave 3 not clearly exceeding wave 1.
        /// After the MIN_DIAGONAL_PENETRATION fix no diagonal should be found.
        /// </summary>
        [Test]
        public void ExactMarkup_EurusdH1_NoDiagonalWithWeakW3()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "EURUSD_h1_2026-05-08T02-00-00_2026-05-11T22-00-00.csv");

            var tf = new TimeFrameBase("Hour", "h1");
            var sym = new SymbolBase("EURUSD", "EURUSD", 5, 1, 0.00001, 0.1, 100000);
            var provider = new TestBarsProvider(tf, sym);
            provider.LoadCandles(csvPath);

            Assert.That(provider.Count, Is.GreaterThan(0), "CSV must load successfully");

            // Replicate the indicator flow: find overall max/min
            int startBarIndex = 0;
            int endBarIndex = provider.Count - 1;

            double maxValue = double.MinValue;
            double minValue = double.MaxValue;
            int maxBarIndex = startBarIndex;
            int minBarIndex = startBarIndex;

            for (int i = startBarIndex; i <= endBarIndex; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                if (high > maxValue) { maxValue = high; maxBarIndex = i; }
                if (low < minValue) { minValue = low; minBarIndex = i; }
            }

            int fartherBarIndex = Math.Min(maxBarIndex, minBarIndex);
            int closerBarIndex = Math.Max(maxBarIndex, minBarIndex);
            double startValue = fartherBarIndex == maxBarIndex ? maxValue : minValue;
            double endValue = closerBarIndex == maxBarIndex ? maxValue : minValue;

            var startPoint = new BarPoint(startValue, fartherBarIndex, provider);
            var endPoint = new BarPoint(endValue, closerBarIndex, provider);
            bool isUp = endPoint.Value > startPoint.Value;

            // Same deviation as the indicator
            var innerFinder = new TradeKit.Core.Indicators.SimpleExtremumFinder(
                0.1, provider, !isUp);
            innerFinder.Calculate(startPoint.BarIndex, endPoint.BarIndex);

            List<BarPoint> innerPoints = innerFinder.ToExtremaList()
                .Where(p => p.BarIndex >= startPoint.BarIndex && p.BarIndex <= endPoint.BarIndex)
                .ToList();

            if (innerPoints.All(p => p.BarIndex != startPoint.BarIndex))
                innerPoints.Insert(0, startPoint);
            if (innerPoints.All(p => p.BarIndex != endPoint.BarIndex))
                innerPoints.Add(endPoint);

            // Corridor refinement — exactly as the indicator does
            innerPoints = TradeKit.Core.Indicators.ExtremumFinderBase
                .EndFixCorridors(innerPoints, provider);
            innerPoints = TradeKit.Core.Indicators.ExtremumFinderBase
                .RefineToCorridors(innerPoints, provider);

            TestContext.WriteLine($"Inner points: {innerPoints.Count}");
            foreach (var p in innerPoints)
                TestContext.WriteLine($"  bar={p.BarIndex} val={p.Value:F5}");

            // Parse with BarsProvider (same as indicator — enables candle breach checks)
            var markup = new TradeKit.Core.AlgoBase.ElliottWaveExactMarkup(provider);
            var results = markup.Parse(innerPoints);

            TestContext.WriteLine($"Total results: {results.Count}");
            foreach (var r in results.Take(10))
                TestContext.WriteLine($"  {r.ModelType} score={r.Score:F3} waves={r.WaveCount}/{r.ExpectedWaves}");

            var diagonals = results
                .Where(r => TradeKit.Core.AlgoBase.ElliottWavePatternHelper
                    .DiagonalImpulses.Contains(r.ModelType))
                .ToList();

            TestContext.WriteLine($"Diagonals found: {diagonals.Count}");
            foreach (var d in diagonals)
            {
                TestContext.WriteLine(
                    $"  {d.ModelType} score={d.Score:F3} " +
                    $"start={d.StartPoint.Value:F5}(bar{d.StartPoint.BarIndex})");
                for (int wi = 0; wi < d.WaveCount; wi++)
                {
                    var sw = d.SubWaves[wi];
                    TestContext.WriteLine(
                        $"    W{wi+1}: {sw.StartPoint.Value:F5}(bar{sw.StartPoint.BarIndex})" +
                        $" → {sw.EndPoint.Value:F5}(bar{sw.EndPoint.BarIndex})" +
                        $" len={Math.Abs(sw.EndPoint.Value - sw.StartPoint.Value):F5}" +
                        $" {(sw.EndPoint.Value > sw.StartPoint.Value ? "UP" : "DN")}");
                }
            }

            Assert.That(diagonals, Is.Empty,
                "No diagonal should be found — wave 3 does not clearly exceed wave 1");
        }

}
}
