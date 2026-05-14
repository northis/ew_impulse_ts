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

        /// <summary>
        /// EURGBP M5: the chart shows a zigzag/double-zigzag going down.
        /// Wave B (or X) contains a running triangle (ABCDE).
        /// The engine must find at least one result whose wave B/X sub-model
        /// is a triangle (TRIANGLE_CONTRACTING or TRIANGLE_RUNNING).
        /// </summary>
        [Test]
        public void ExactMarkup_EurgbpM5_TriangleInWaveB()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "EURGBP_m5_2026-05-12T08-10-00_2026-05-12T21-00-00.csv");

            var tf = new TimeFrameBase("Minute5", "m5");
            var sym = new SymbolBase("EURGBP", "EURGBP", 5, 1, 0.00001, 0.1, 100000);
            var provider = new TestBarsProvider(tf, sym);
            provider.LoadCandles(csvPath);

            Assert.That(provider.Count, Is.GreaterThan(0), "CSV must load successfully");

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

            innerPoints = TradeKit.Core.Indicators.ExtremumFinderBase
                .EndFixCorridors(innerPoints, provider);
            innerPoints = TradeKit.Core.Indicators.ExtremumFinderBase
                .RefineToCorridors(innerPoints, provider);

            TestContext.WriteLine($"Inner points: {innerPoints.Count}");
            foreach (var p in innerPoints)
                TestContext.WriteLine($"  bar={p.BarIndex} val={p.Value:F5}");

            var markup = new TradeKit.Core.AlgoBase.ElliottWaveExactMarkup(provider);
            var results = markup.Parse(innerPoints);

            TestContext.WriteLine($"\nTotal results: {results.Count}");
            foreach (var r in results.Take(20))
            {
                TestContext.WriteLine($"  {r.ModelType} score={r.Score:F3} waves={r.WaveCount}/{r.ExpectedWaves}");
                if (r.SubWaves != null)
                {
                    for (int wi = 0; wi < r.WaveCount && wi < r.SubWaves.Length; wi++)
                    {
                        var sw = r.SubWaves[wi];
                        if (sw == null) continue;
                        string key = TradeKit.Core.AlgoBase.ElliottWaveExactMarkup
                            .GetWaveKey(r.ModelType, wi + 1);
                        string subModel = sw.ModelType.ToString();
                        if (sw.SubWaves != null && sw.ModelType != ElliottModelType.SIMPLE_IMPULSE)
                        {
                            string subWaves = string.Join(", ",
                                sw.SubWaves.Where(s => s != null)
                                    .Select(s => s.ModelType.ToString()));
                            subModel += $" [{subWaves}]";
                        }
                        TestContext.WriteLine(
                            $"    {key}: bar{sw.StartPoint.BarIndex}({sw.StartPoint.Value:F5})" +
                            $" → bar{sw.EndPoint.BarIndex}({sw.EndPoint.Value:F5})" +
                            $" | {subModel}");
                    }
                }
            }

            // Check if any result has a triangle in wave B or X
            // Diagnostic: check finer extrema in B/X ranges
            foreach (var r in results.Take(10))
            {
                if (r.SubWaves == null) continue;
                for (int wi = 0; wi < r.WaveCount && wi < r.SubWaves.Length; wi++)
                {
                    var sw2 = r.SubWaves[wi];
                    if (sw2 == null) continue;
                    string key2 = TradeKit.Core.AlgoBase.ElliottWaveExactMarkup
                        .GetWaveKey(r.ModelType, wi + 1);
                    if (key2 != "b" && key2 != "x") continue;
                    int fb = sw2.StartPoint.BarIndex, tb = sw2.EndPoint.BarIndex;
                    bool swUp = sw2.EndPoint.Value > sw2.StartPoint.Value;
                    var finerFinder = new TradeKit.Core.Indicators.SimpleExtremumFinder(
                        0.03, provider, !swUp);
                    finerFinder.Calculate(fb, tb);
                    var finerPts = finerFinder.ToExtremaList()
                        .Where(p => p.BarIndex >= fb && p.BarIndex <= tb).ToList();
                    if (finerPts.All(p => p.BarIndex != fb))
                        finerPts.Insert(0, sw2.StartPoint);
                    if (finerPts.All(p => p.BarIndex != tb))
                        finerPts.Add(sw2.EndPoint);
                    finerPts = TradeKit.Core.Indicators.ExtremumFinderBase
                        .EndFixCorridors(finerPts, provider);
                    finerPts = TradeKit.Core.Indicators.ExtremumFinderBase
                        .RefineToCorridors(finerPts, provider);
                    TestContext.WriteLine($"\n  Finer extrema in {key2} (bar{fb}→bar{tb}): {finerPts.Count} points");
                    foreach (var fp in finerPts)
                        TestContext.WriteLine($"    bar={fp.BarIndex} val={fp.Value:F5}");
                }
            }

            // Debug: show all sub-waves for B/X positions
            foreach (var r in results)
            {
                if (r.SubWaves == null) continue;
                for (int wi = 0; wi < r.WaveCount && wi < r.SubWaves.Length; wi++)
                {
                    var sw = r.SubWaves[wi];
                    string key = TradeKit.Core.AlgoBase.ElliottWaveExactMarkup
                        .GetWaveKey(r.ModelType, wi + 1);
                    if (key == "b" || key == "x")
                    {
                        TestContext.WriteLine($"Result {r.ModelType}: wave {key} bar{sw?.StartPoint?.BarIndex}-bar{sw?.EndPoint?.BarIndex} subwave={sw?.ModelType.ToString() ?? "NULL"}");
                    }
                }
            }

            bool hasTriangleInBX = results.Any(r =>
            {
                if (r.SubWaves == null) return false;
                for (int wi = 0; wi < r.WaveCount && wi < r.SubWaves.Length; wi++)
                {
                    var sw = r.SubWaves[wi];
                    if (sw == null) continue;
                    string key = TradeKit.Core.AlgoBase.ElliottWaveExactMarkup
                        .GetWaveKey(r.ModelType, wi + 1);
                    if ((key == "b" || key == "x")
                        && (sw.ModelType == ElliottModelType.TRIANGLE_CONTRACTING
                         || sw.ModelType == ElliottModelType.TRIANGLE_RUNNING))
                        return true;
                }
                return false;
            });

            Assert.That(hasTriangleInBX, Is.True,
                "At least one result should have a triangle in wave B or X");
        }

        /// <summary>
        /// EURUSD H1: a zigzag going down with TRIANGLE_CONTRACTING as wave b.
        /// The triangle wave e is very small (~0.04%) — investigate whether
        /// the engine can detect this markup or deviationPercent prevents it.
        /// </summary>
        [Test]
        public void ExactMarkup_EurusdH1_ZigzagTriangleInB()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "EURUSD_h1_2026-05-11T22-00-00_2026-05-14T16-00-00.csv");

            var tf = new TimeFrameBase("Hour", "h1");
            var sym = new SymbolBase("EURUSD", "EURUSD", 5, 1, 0.00001, 0.1, 100000);
            var provider = new TestBarsProvider(tf, sym);
            provider.LoadCandles(csvPath);

            Assert.That(provider.Count, Is.GreaterThan(0), "CSV must load successfully");
            TestContext.WriteLine($"Bars loaded: {provider.Count}");

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

            TestContext.WriteLine($"Start: bar={fartherBarIndex} val={startValue:F5}");
            TestContext.WriteLine($"End:   bar={closerBarIndex} val={endValue:F5}");
            TestContext.WriteLine($"IsUp: {isUp}");

            // Try multiple deviations: standard 0.1, sub-wave 0.03, and smaller
            double[] deviations = { 0.1, 0.03, 0.01, 0.005 };

            foreach (double dev in deviations)
            {
                TestContext.WriteLine($"\n========== Deviation = {dev} ==========");

                var innerFinder = new TradeKit.Core.Indicators.SimpleExtremumFinder(
                    dev, provider, !isUp);
                innerFinder.Calculate(startPoint.BarIndex, endPoint.BarIndex);

                List<BarPoint> innerPoints = innerFinder.ToExtremaList()
                    .Where(p => p.BarIndex >= startPoint.BarIndex && p.BarIndex <= endPoint.BarIndex)
                    .ToList();

                if (innerPoints.All(p => p.BarIndex != startPoint.BarIndex))
                    innerPoints.Insert(0, startPoint);
                if (innerPoints.All(p => p.BarIndex != endPoint.BarIndex))
                    innerPoints.Add(endPoint);

                innerPoints = TradeKit.Core.Indicators.ExtremumFinderBase
                    .EndFixCorridors(innerPoints, provider);
                innerPoints = TradeKit.Core.Indicators.ExtremumFinderBase
                    .RefineToCorridors(innerPoints, provider);

                TestContext.WriteLine($"Inner points: {innerPoints.Count}");
                foreach (var p in innerPoints)
                    TestContext.WriteLine($"  bar={p.BarIndex} val={p.Value:F5}");

                var markup = new TradeKit.Core.AlgoBase.ElliottWaveExactMarkup(provider);
                var results = markup.Parse(innerPoints);

                TestContext.WriteLine($"Total results: {results.Count}");
                foreach (var r in results.Take(20))
                {
                    TestContext.WriteLine(
                        $"  {r.ModelType} score={r.Score:F3} waves={r.WaveCount}/{r.ExpectedWaves}" +
                        $" bar{r.StartPoint.BarIndex}→bar{r.EndPoint.BarIndex}");
                    if (r.SubWaves != null)
                    {
                        for (int wi = 0; wi < r.WaveCount && wi < r.SubWaves.Length; wi++)
                        {
                            var sw = r.SubWaves[wi];
                            if (sw == null) continue;
                            string key = TradeKit.Core.AlgoBase.ElliottWaveExactMarkup
                                .GetWaveKey(r.ModelType, wi + 1);
                            string subModel = sw.ModelType.ToString();
                            if (sw.SubWaves != null && sw.ModelType != ElliottModelType.SIMPLE_IMPULSE)
                            {
                                string subWaves = string.Join(", ",
                                    sw.SubWaves.Where(s => s != null)
                                        .Select(s => $"{s.ModelType}"));
                                subModel += $" [{subWaves}]";
                            }
                            TestContext.WriteLine(
                                $"    {key}: bar{sw.StartPoint.BarIndex}({sw.StartPoint.Value:F5})" +
                                $" → bar{sw.EndPoint.BarIndex}({sw.EndPoint.Value:F5})" +
                                $" | {subModel}");
                        }
                    }
                }

                // Check for triangle in wave b/x
                bool hasTriangle = results.Any(r =>
                {
                    if (r.SubWaves == null) return false;
                    for (int wi = 0; wi < r.WaveCount && wi < r.SubWaves.Length; wi++)
                    {
                        var sw = r.SubWaves[wi];
                        if (sw == null) continue;
                        string key = TradeKit.Core.AlgoBase.ElliottWaveExactMarkup
                            .GetWaveKey(r.ModelType, wi + 1);
                        if ((key == "b" || key == "x")
                            && (sw.ModelType == ElliottModelType.TRIANGLE_CONTRACTING
                             || sw.ModelType == ElliottModelType.TRIANGLE_RUNNING))
                            return true;
                    }
                    return false;
                });
                TestContext.WriteLine($"Has triangle in b/x: {hasTriangle}");
            }

            // Also check: what does the finer finder see in the B-wave range (bar 15→30)?
            // The proposed JSON has B from bar 15 (1.17217) to bar 30 (1.17371)
            // with 5 sub-waves (triangle abcde)
            TestContext.WriteLine("\n========== Manual B-wave range analysis ==========");

            // Find bars corresponding to the proposed B range
            // B start: 2026-05-12T15:00 → low 1.17217
            // B end:   2026-05-13T05:00 → high 1.17371 (approx)
            int bStartBar = -1, bEndBar = -1;
            for (int i = 0; i < provider.Count; i++)
            {
                var dt = provider.GetOpenTime(i);
                if (dt.Year == 2026 && dt.Month == 5 && dt.Day == 12 && dt.Hour == 15)
                    bStartBar = i;
                if (dt.Year == 2026 && dt.Month == 5 && dt.Day == 13 && dt.Hour == 5)
                    bEndBar = i;
            }

            if (bStartBar >= 0 && bEndBar >= 0)
            {
                double bStartVal = provider.GetLowPrice(bStartBar);
                double bEndVal = provider.GetHighPrice(bEndBar);

                // Use the user's proposed B endpoint (bar31) directly — NOT the max high
                var bStart = new BarPoint(bStartVal, bStartBar, provider);
                var bEnd = new BarPoint(bEndVal, bEndBar, provider);

                TestContext.WriteLine($"B range (user's proposed): bar{bStartBar}({bStartVal:F5}) → bar{bEndBar}({bEndVal:F5})");

                double[] bDeviations = { 0.03, 0.01, 0.005, 0.003 };
                foreach (double dev in bDeviations)
                {
                    TestContext.WriteLine($"\n--- B-wave deviation = {dev} ---");
                    var bFinder = new TradeKit.Core.Indicators.SimpleExtremumFinder(
                        dev, provider, true);  // isDown=true since B goes up (we want alternating)
                    bFinder.Calculate(bStartBar, bEndBar);
                    var bPoints = bFinder.ToExtremaList()
                        .Where(p => p.BarIndex >= bStartBar && p.BarIndex <= bEndBar)
                        .ToList();

                    if (bPoints.All(p => p.BarIndex != bStartBar))
                        bPoints.Insert(0, bStart);
                    if (bPoints.All(p => p.BarIndex != bEndBar))
                        bPoints.Add(bEnd);

                    bPoints = TradeKit.Core.Indicators.ExtremumFinderBase
                        .EndFixCorridors(bPoints, provider);
                    bPoints = TradeKit.Core.Indicators.ExtremumFinderBase
                        .RefineToCorridors(bPoints, provider);

                    TestContext.WriteLine($"B-wave inner points: {bPoints.Count}");
                    foreach (var p in bPoints)
                        TestContext.WriteLine($"  bar={p.BarIndex} val={p.Value:F5}");

                    if (bPoints.Count >= 6)
                    {
                        var bMarkup = new TradeKit.Core.AlgoBase.ElliottWaveExactMarkup(provider);
                        var bResults = bMarkup.Parse(bPoints);
                        TestContext.WriteLine($"B-wave results: {bResults.Count}");
                        foreach (var r in bResults.Take(10))
                        {
                            TestContext.WriteLine(
                                $"  {r.ModelType} score={r.Score:F3} waves={r.WaveCount}/{r.ExpectedWaves}");
                            if (r.SubWaves != null)
                            {
                                for (int wi = 0; wi < r.WaveCount && wi < r.SubWaves.Length; wi++)
                                {
                                    var sw = r.SubWaves[wi];
                                    if (sw == null) continue;
                                    string key = TradeKit.Core.AlgoBase.ElliottWaveExactMarkup
                                        .GetWaveKey(r.ModelType, wi + 1);
                                    TestContext.WriteLine(
                                        $"    {key}: bar{sw.StartPoint.BarIndex}({sw.StartPoint.Value:F5})" +
                                        $" → bar{sw.EndPoint.BarIndex}({sw.EndPoint.Value:F5})" +
                                        $" | {sw.ModelType}");
                                }
                            }
                        }
                    }
                    else
                    {
                        TestContext.WriteLine("Not enough points for 5-wave model");
                    }
                }

                // Also check: what Fibonacci score does the user's proposed ZIGZAG split get?
                TestContext.WriteLine("\n========== Fibonacci score analysis ==========");
                double lenA = startValue - provider.GetLowPrice(17); // wave a length
                // User's B: bar17→bar31
                double userBLen = bEndVal - bStartVal;
                // User's C: bar31→bar35
                double userCLen = bEndVal - provider.GetLowPrice(closerBarIndex);
                TestContext.WriteLine($"User's split: A len={lenA:F5}, B len={userBLen:F5}, C len={userCLen:F5}");
                TestContext.WriteLine($"  B/A={userBLen/lenA:F4}, C/A={userCLen/lenA:F4}");

                // Engine's preferred B: bar17→bar21
                double engBEnd = provider.GetHighPrice(21);
                double engBLen = engBEnd - bStartVal;
                double engCLen = engBEnd - provider.GetLowPrice(closerBarIndex);
                TestContext.WriteLine($"Engine's split: A len={lenA:F5}, B len={engBLen:F5}, C len={engCLen:F5}");
                TestContext.WriteLine($"  B/A={engBLen/lenA:F4}, C/A={engCLen/lenA:F4}");
            }
        }

}
}
