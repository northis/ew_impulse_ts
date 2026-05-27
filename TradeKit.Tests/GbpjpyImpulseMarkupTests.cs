using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests <see cref="ElliottWaveExactMarkup"/> against real GBPJPY M15 data
    /// (2026-04-19T21:00 – 2026-04-23T09:30, 339 bars).
    /// <para>
    /// The price moves from 213.993 (bar 0) to 215.733 (bar 338) — an upward
    /// corrective structure identified as a double zigzag (W-X-Y).
    /// </para>
    /// </summary>
    [TestFixture]
    public class GbpjpyDoubleZigzagMarkupTests
    {
        private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Minute15;
        private static readonly ISymbol SYMBOL =
            new SymbolBase("GBPJPY", "GBP/JPY", 2, 3, 0.01, 0.01, 100000);

        private TestBarsProvider m_BarsProvider;

        [SetUp]
        public void Setup()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "GBPJPY_m15_2026-04-19T21-00-00_2026-04-23T09-30-00.csv");

            m_BarsProvider = new TestBarsProvider(TIME_FRAME, SYMBOL);
            m_BarsProvider.LoadCandles(csvPath);
        }

        /// <summary>
        /// Mimics the indicator logic: finds the overall price range (max High / min Low),
        /// uses <see cref="DeviationOptimizer"/> + <see cref="SimpleExtremumFinder"/> to
        /// discover inner extrema automatically, and asserts that the markup engine
        /// identifies a double zigzag (W-X-Y) pattern.
        /// </summary>
        [Test]
        public void DoubleZigzagMarkup_GbpjpyM15_FindsDoubleZigzag()
        {
            Assert.That(m_BarsProvider.Count, Is.EqualTo(339),
                "CSV must contain exactly 339 M15 bars");

            // ── step 1: find overall range ─────────────────────────────────
            double maxValue = double.MinValue;
            double minValue = double.MaxValue;
            int maxBarIndex = 0;
            int minBarIndex = 0;

            for (int i = 0; i < m_BarsProvider.Count; i++)
            {
                double high = m_BarsProvider.GetHighPrice(i);
                double low  = m_BarsProvider.GetLowPrice(i);
                if (high > maxValue) { maxValue = high; maxBarIndex = i; }
                if (low  < minValue) { minValue = low;  minBarIndex = i; }
            }

            int fartherBarIndex = Math.Min(maxBarIndex, minBarIndex);
            int closerBarIndex  = Math.Max(maxBarIndex, minBarIndex);

            double startValue = fartherBarIndex == maxBarIndex ? maxValue : minValue;
            double endValue   = closerBarIndex  == maxBarIndex ? maxValue : minValue;

            var startPoint = new BarPoint(startValue, fartherBarIndex, m_BarsProvider);
            var endPoint   = new BarPoint(endValue,   closerBarIndex,  m_BarsProvider);

            bool isUp = endPoint.Value > startPoint.Value;

            // ── step 2: collect inner extrema via DeviationOptimizer ────────
            var optimizer = new DeviationOptimizer(m_BarsProvider, startPoint.BarIndex, endPoint.BarIndex, !isUp);
            double optimalDev = optimizer.FindOptimalDeviation();
            TestContext.WriteLine($"Optimal deviation: {optimalDev:F4}%");

            var innerFinder = new SimpleExtremumFinder(optimalDev, m_BarsProvider, !isUp);
            innerFinder.Calculate(startPoint.BarIndex, endPoint.BarIndex);

            List<BarPoint> innerPoints = innerFinder.ToExtremaList()
                .Where(p => p.BarIndex >= startPoint.BarIndex && p.BarIndex <= endPoint.BarIndex)
                .ToList();

            if (innerPoints.All(p => p.BarIndex != startPoint.BarIndex))
                innerPoints.Insert(0, startPoint);
            if (innerPoints.All(p => p.BarIndex != endPoint.BarIndex))
                innerPoints.Add(endPoint);

            innerPoints = ExtremumFinderBase.EndFixCorridors(innerPoints, m_BarsProvider);
            innerPoints = ExtremumFinderBase.RefineToCorridors(innerPoints, m_BarsProvider);
            TestContext.WriteLine($"Inner extrema after refinement: {innerPoints.Count} points");

            // ── step 3: parse ───────────────────────────────────────────────
            var markup = new ElliottWaveExactMarkup(m_BarsProvider);
            List<ExactParsedNode> results = markup.Parse(innerPoints);

            TestContext.WriteLine($"Parse results: {results.Count}");
            foreach (ExactParsedNode r in results.Take(5))
                TestContext.WriteLine($"  {r.ModelType} score={r.Score:F3} waves={r.WaveCount}/{r.ExpectedWaves} " +
                    $"bar{r.StartIndex}→{r.EndIndex}");

            ExactParsedNode? doubleZigzag = results.FirstOrDefault(
                r => r.ModelType == ElliottModelType.DOUBLE_ZIGZAG && r.WaveCount == r.ExpectedWaves);

            results.SaveMarkupResults(m_BarsProvider);

            Assert.IsNotNull(doubleZigzag,
                $"Expected DOUBLE_ZIGZAG in results but top result was: " +
                $"{results.FirstOrDefault()?.ModelType} (score={results.FirstOrDefault()?.Score:F3}) " +
                $"| total results={results.Count}");

            TestContext.WriteLine($"Found DOUBLE_ZIGZAG: score={doubleZigzag.Score:F3} bar{doubleZigzag.StartIndex}→{doubleZigzag.EndIndex}");

            // Dump sub-wave details
            for (int i = 0; i < doubleZigzag.SubWaves.Length; i++)
            {
                var sw = doubleZigzag.SubWaves[i];
                string waveKey = ElliottWaveExactMarkup.GetWaveKey(doubleZigzag.ModelType, i + 1);
                TestContext.WriteLine($"  SubWave {waveKey}: {sw.ModelType} bar{sw.StartIndex}→{sw.EndIndex} " +
                    $"subWaves={sw.SubWaves?.Length ?? 0}");
                if (sw.SubWaves != null)
                    foreach (var ssw in sw.SubWaves)
                        TestContext.WriteLine($"    {ssw.ModelType} bar{ssw.StartIndex}→{ssw.EndIndex}");
            }

            // Diagnostic: how many inner points in the y-wave range?
            int yStart = doubleZigzag.SubWaves[2].StartIndex;
            int yEnd = doubleZigzag.SubWaves[2].EndIndex;
            var yPoints = innerPoints.Where(p => p.BarIndex >= yStart && p.BarIndex <= yEnd).ToList();
            TestContext.WriteLine($"\nPoints in y-wave range [{yStart}..{yEnd}]: {yPoints.Count}");
            foreach (var p in yPoints)
                TestContext.WriteLine($"  bar{p.BarIndex} val={p.Value:F3}");

            // Try parsing the y segment directly with finer deviation
            var yFinder = new SimpleExtremumFinder(0.03, m_BarsProvider, false);
            yFinder.Calculate(yStart, yEnd);
            var yFinerPoints = yFinder.ToExtremaList()
                .Where(p => p.BarIndex >= yStart && p.BarIndex <= yEnd).ToList();
            if (yFinerPoints.All(p => p.BarIndex != yStart))
                yFinerPoints.Insert(0, doubleZigzag.SubWaves[2].StartPoint);
            if (yFinerPoints.All(p => p.BarIndex != yEnd))
                yFinerPoints.Add(doubleZigzag.SubWaves[2].EndPoint);
            yFinerPoints = ExtremumFinderBase.EndFixCorridors(yFinerPoints, m_BarsProvider);

            TestContext.WriteLine($"\nFiner points in y-wave (dev=0.03): {yFinerPoints.Count}");
            foreach (var p in yFinerPoints)
                TestContext.WriteLine($"  bar{p.BarIndex} val={p.Value:F3}");

            // Try to parse just the y-wave points as ZIGZAG with depth=1
            // (simulating what FillSubWaveModels does internally)
            var yMarkup = new ElliottWaveExactMarkup(m_BarsProvider);

            // Test 1: Full Parse() at depth=0 (like the test diagnostic)
            var yResults = yMarkup.Parse(yFinerPoints);
            TestContext.WriteLine($"\nY-wave full Parse() results: {yResults.Count}");
            foreach (var r in yResults.Take(5))
                TestContext.WriteLine($"  {r.ModelType} score={r.Score:F3} waves={r.WaveCount}/{r.ExpectedWaves} " +
                    $"bar{r.StartIndex}→{r.EndIndex}");

            // Test 2: Directly call Parse() but check if ZIGZAG is found at depth 0
            // (this is what we expect FillSubWaveModels fallback to produce)
            Assert.IsTrue(yResults.Any(r => r.ModelType == ElliottModelType.ZIGZAG),
                "Y-wave segment should be parseable as ZIGZAG");

            // ── Diagnostic: verify boundary sync at bar 171 ──────────────
            TestContext.WriteLine($"\nBoundary sync diagnostic:");
            TestContext.WriteLine($"GetHighPrice(171) = {m_BarsProvider.GetHighPrice(171):F5}");
            TestContext.WriteLine($"GetLowPrice(171) = {m_BarsProvider.GetLowPrice(171):F5}");
            
            TestContext.WriteLine($"\nY-wave sub-wave boundaries after Parse():");
            for (int i = 0; i < doubleZigzag.SubWaves[2].SubWaves.Length; i++)
            {
                var leaf = doubleZigzag.SubWaves[2].SubWaves[i];
                TestContext.WriteLine($"  Wave {i} ({leaf.ModelType}): [{leaf.StartPoint.BarIndex}]={leaf.StartPoint.Value:F5} → [{leaf.EndPoint.BarIndex}]={leaf.EndPoint.Value:F5}");
            }
        }
    }
}
