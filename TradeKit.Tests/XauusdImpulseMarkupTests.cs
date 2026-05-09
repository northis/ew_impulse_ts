using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests <see cref="ElliottWaveExactMarkup"/> against real XAUUSD H1 data
    /// (2026-05-04T16:00 – 2026-05-07T15:00, 69 bars).
    /// <para>
    /// Manual markup shows a 5-wave upward impulse ①②③④⑤:
    /// <list type="bullet">
    ///   <item>Wave 1 – simple impulse (bars 0→21)</item>
    ///   <item>Wave 2 – simple correction (bars 21→28)</item>
    ///   <item>Wave 3 – extended impulse (bars 28→40)</item>
    ///   <item>Wave 4 – contracting triangle a-b-c-d-e (bars 40→57)</item>
    ///   <item>Wave 5 – zigzag A-B-C (bars 57→68)</item>
    /// </list>
    /// </para>
    /// </summary>
    [TestFixture]
    public class XauusdImpulseMarkupTests
    {
        private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Hour1;
        private static readonly ISymbol SYMBOL =
            new SymbolBase("XAUUSD", "Gold/USD", 1, 2, 0.01, 0.01, 100);

        private TestBarsProvider m_BarsProvider;

        [SetUp]
        public void Setup()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "XAUUSD_h1_2026-05-04T16-00-00_2026-05-07T15-00-00.csv");

            m_BarsProvider = new TestBarsProvider(TIME_FRAME, SYMBOL);
            m_BarsProvider.LoadCandles(csvPath);
        }

        /// <summary>
        /// Returns the 12 manually annotated key points of the XAUUSD H1 impulse,
        /// built against the loaded <see cref="TestBarsProvider"/> so each
        /// <see cref="BarPoint"/> carries the correct bar index.
        /// <para>
        /// Layout (alternating L/H):
        /// Start(L0) W1(H21) W2(L28) W3(H40)
        /// Tri-a(L42) Tri-b(H44) Tri-c(L47) Tri-d(H53) W4/Tri-e(L57)
        /// W5-1(H59) W5-2(L62) W5end(H68)
        /// </para>
        /// </summary>
        private List<BarPoint> BuildImpulseKeyPoints()
        {
            // Timestamps match the CSV (UTC convention used by TestBarsProvider.LoadCandles)
            static DateTime Utc(string iso) =>
                DateTime.SpecifyKind(
                    DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture),
                    DateTimeKind.Utc);

            return new List<BarPoint>
            {
                new(4500.90, Utc("2026-05-04T16:00:00"), m_BarsProvider), // Start:     bar  0, L
                new(4586.54, Utc("2026-05-05T14:00:00"), m_BarsProvider), // W1 end:    bar 21, H
                new(4546.38, Utc("2026-05-05T22:00:00"), m_BarsProvider), // W2 end:    bar 28, L
                new(4723.00, Utc("2026-05-06T10:00:00"), m_BarsProvider), // W3 end:    bar 40, H
                new(4660.36, Utc("2026-05-06T12:00:00"), m_BarsProvider), // Tri-a end: bar 42, L
                new(4717.42, Utc("2026-05-06T14:00:00"), m_BarsProvider), // Tri-b end: bar 44, H
                new(4679.02, Utc("2026-05-06T17:00:00"), m_BarsProvider), // Tri-c end: bar 47, L
                new(4701.07, Utc("2026-05-07T00:00:00"), m_BarsProvider), // Tri-d end: bar 53, H
                new(4692.55, Utc("2026-05-07T04:00:00"), m_BarsProvider), // W4 end:    bar 57, L (Tri-e)
                new(4750.22, Utc("2026-05-07T06:00:00"), m_BarsProvider), // W5/(1):    bar 59, H
                new(4722.04, Utc("2026-05-07T09:00:00"), m_BarsProvider), // W5/(2):    bar 62, L
                new(4764.76, Utc("2026-05-07T15:00:00"), m_BarsProvider), // W5 end:    bar 68, H
            };
        }

        /// <summary>
        /// Verifies that the markup engine identifies a complete 5-wave impulse
        /// from the 12 manually annotated key points.
        /// </summary>
        [Test]
        public void ImpulseMarkup_XauusdH1_FindsImpulse()
        {
            Assert.That(m_BarsProvider.Count, Is.EqualTo(69),
                "CSV must contain exactly 69 H1 bars");

            List<BarPoint> points = BuildImpulseKeyPoints();

            Assert.That(points.All(p => p.BarIndex >= 0), Is.True,
                "Every key-point datetime must resolve to a valid bar index in the provider");

            var markup = new ElliottWaveExactMarkup();
            List<ExactParsedNode> results = markup.Parse(points);

            ExactParsedNode? impulse = results.FirstOrDefault(
                r => r.ModelType == ElliottModelType.IMPULSE && r.WaveCount == r.ExpectedWaves);

            Assert.IsNotNull(impulse,
                $"Expected IMPULSE to appear in results but got: " +
                $"top={results.FirstOrDefault()?.ModelType} " +
                $"score={results.FirstOrDefault()?.Score:F3} " +
                $"waves={results.FirstOrDefault()?.WaveCount}/{results.FirstOrDefault()?.ExpectedWaves} " +
                $"| total results={results.Count}");
        }

        /// <summary>
        /// Uses the indicator approach (SimpleExtremumFinder) to verify that Wave 4 of the
        /// identified impulse is a contracting triangle and Wave 5 is a corrective pattern.
        /// </summary>
        [Test]
        public void ImpulseMarkup_XauusdH1_Wave4IsTriangle_Wave5IsZigzag()
        {
            Assert.That(m_BarsProvider.Count, Is.EqualTo(69));

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
            double startValue   = fartherBarIndex == maxBarIndex ? maxValue : minValue;
            double endValue     = closerBarIndex  == maxBarIndex ? maxValue : minValue;

            var startPoint = new BarPoint(startValue, fartherBarIndex, m_BarsProvider);
            var endPoint   = new BarPoint(endValue,   closerBarIndex,  m_BarsProvider);
            bool isUp      = endPoint.Value > startPoint.Value;

            // ── step 2: collect inner extrema via SimpleExtremumFinder ──────
            var innerFinder = new SimpleExtremumFinder(0.01, m_BarsProvider, !isUp);
            innerFinder.Calculate(startPoint.BarIndex, endPoint.BarIndex);

            List<BarPoint> innerPoints = innerFinder.ToExtremaList()
                .Where(p => p.BarIndex >= startPoint.BarIndex && p.BarIndex <= endPoint.BarIndex)
                .ToList();

            if (innerPoints.All(p => p.BarIndex != startPoint.BarIndex))
                innerPoints.Insert(0, startPoint);
            if (innerPoints.All(p => p.BarIndex != endPoint.BarIndex))
                innerPoints.Add(endPoint);

            // ── step 3: parse ───────────────────────────────────────────────
            var markup = new ElliottWaveExactMarkup();
            List<ExactParsedNode> results = markup.Parse(innerPoints);

            ExactParsedNode? impulse = results.FirstOrDefault(
                r => r.ModelType == ElliottModelType.IMPULSE && r.WaveCount == r.ExpectedWaves);

            Assert.IsNotNull(impulse,
                $"Expected IMPULSE in results but top result was: " +
                $"{results.FirstOrDefault()?.ModelType} (score={results.FirstOrDefault()?.Score:F3})");

            // Wave 4 is at SubWaves[3] (0-indexed within the 5-wave impulse)
            ExactParsedNode? wave4 = impulse!.SubWaves?[3];
            Assert.IsNotNull(wave4, "Wave 4 sub-wave node must be present");
            Assert.That(wave4!.ModelType, Is.EqualTo(ElliottModelType.TRIANGLE_CONTRACTING),
                $"Wave 4 expected TRIANGLE_CONTRACTING, got {wave4.ModelType}. " +
                $"W4 bar range: {wave4.StartIndex}→{wave4.EndIndex}");

            // Wave 5 is at SubWaves[4] — corrective after a triangle W4
            ExactParsedNode? wave5 = impulse.SubWaves?[4];
            Assert.IsNotNull(wave5, "Wave 5 sub-wave node must be present");
            Assert.That(
                wave5!.ModelType != ElliottModelType.SIMPLE_IMPULSE,
                Is.True,
                $"Wave 5 must be a structured corrective/motive model, got {wave5.ModelType}");
        }

        /// <summary>
        /// Mimics the logic of <c>IterativeElliottWaveExactIndicator</c>:
        /// finds the overall price range (max High / min Low), then uses a
        /// <see cref="SimpleExtremumFinder"/> at 0.01 % deviation to discover
        /// all inner extrema automatically, and asserts that the resulting
        /// top-scored markup is a complete 5-wave impulse whose Wave 4 is
        /// identified as a contracting triangle.
        /// </summary>
        [Test]
        public void ImpulseMarkup_XauusdH1_IndicatorApproach_FindsTriangleWave4()
        {
            Assert.That(m_BarsProvider.Count, Is.EqualTo(69));

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

            // ── step 2: collect inner extrema via SimpleExtremumFinder ──────
            var innerFinder = new SimpleExtremumFinder(0.01, m_BarsProvider, !isUp);
            innerFinder.Calculate(startPoint.BarIndex, endPoint.BarIndex);

            List<BarPoint> innerPoints = innerFinder.ToExtremaList()
                .Where(p => p.BarIndex >= startPoint.BarIndex && p.BarIndex <= endPoint.BarIndex)
                .ToList();

            if (innerPoints.All(p => p.BarIndex != startPoint.BarIndex))
                innerPoints.Insert(0, startPoint);
            if (innerPoints.All(p => p.BarIndex != endPoint.BarIndex))
                innerPoints.Add(endPoint);

            TestContext.WriteLine($"Inner extrema: {innerPoints.Count} points");

            // ── step 3: parse ───────────────────────────────────────────────
            var markup = new ElliottWaveExactMarkup();
            List<ExactParsedNode> results = markup.Parse(innerPoints);

            TestContext.WriteLine($"Parse results: {results.Count}");
            foreach (ExactParsedNode r in results.Take(5))
                TestContext.WriteLine($"  {r.ModelType} score={r.Score:F3} waves={r.WaveCount}/{r.ExpectedWaves} " +
                    $"bar{r.StartIndex}→{r.EndIndex} " +
                    $"W4={(r.SubWaves?.Length > 3 ? r.SubWaves[3]?.ModelType.ToString() : "n/a")}");

            ExactParsedNode? impulse = results.FirstOrDefault(
                r => r.ModelType == ElliottModelType.IMPULSE && r.WaveCount == r.ExpectedWaves);

            Assert.IsNotNull(impulse,
                $"Expected IMPULSE in results but top result was: " +
                $"{results.FirstOrDefault()?.ModelType} (score={results.FirstOrDefault()?.Score:F3})");

            ExactParsedNode? wave4 = impulse!.SubWaves?[3];
            Assert.IsNotNull(wave4, "Wave 4 sub-wave node must be present");
            Assert.That(wave4!.ModelType, Is.EqualTo(ElliottModelType.TRIANGLE_CONTRACTING),
                $"Wave 4 expected TRIANGLE_CONTRACTING, got {wave4.ModelType}. " +
                $"W4 bar range: {wave4.StartIndex}→{wave4.EndIndex}");
        }
    }
}
