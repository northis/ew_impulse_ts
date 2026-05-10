using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests <see cref="ElliottWaveExactMarkup"/> against real USDJPY H1 data
    /// (2026-04-30T06:00 – 2026-05-06T04:00).
    /// The data shows a descending move from ~160.72 to ~155.04, which should be
    /// identified as a double zigzag (or similar corrective pattern) rather than
    /// an initial diagonal.
    /// </summary>
    [TestFixture]
    public class UsdjpyDoubleZigzagMarkupTests
    {
        private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Hour1;
        private static readonly ISymbol SYMBOL =
            new SymbolBase("USDJPY", "USD/JPY", 1, 3, 0.001, 0.001, 100);

        private TestBarsProvider m_BarsProvider;

        [SetUp]
        public void Setup()
        {
            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "TestData",
                "USDJPY_h1_2026-04-30T06-00-00_2026-05-06T04-00-00.csv");

            m_BarsProvider = new TestBarsProvider(TIME_FRAME, SYMBOL);
            m_BarsProvider.LoadCandles(csvPath);
        }

        /// <summary>
        /// Verifies that without a bars provider (point-only mode), the algorithm
        /// finds a diagonal — because it cannot check OHLC candle breaches.
        /// </summary>
        [Test]
        public void UsdjpyH1_WithoutBarsProvider_FindsDiagonal()
        {
            List<BarPoint> innerPoints = BuildInnerExtrema();

            var markup = new ElliottWaveExactMarkup();
            List<ExactParsedNode> results = markup.Parse(innerPoints);

            bool hasDiagonal = results.Any(r =>
                r.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL
                || r.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_ENDING);

            Assert.IsTrue(hasDiagonal,
                "Without barsProvider the point-only mode should find a diagonal (no OHLC breach check).");
        }

        /// <summary>
        /// Verifies that with a bars provider (OHLC breach checks active),
        /// diagonals are rejected and the top result is a DOUBLE_ZIGZAG.
        /// This is the pattern the user manually identified in the data.
        /// </summary>
        [Test]
        public void UsdjpyH1_WithBarsProvider_FindsDoubleZigzag()
        {
            List<BarPoint> innerPoints = BuildInnerExtrema();

            var markup = new ElliottWaveExactMarkup(m_BarsProvider);
            List<ExactParsedNode> results = markup.Parse(innerPoints);

            Assert.IsNotEmpty(results, "Markup should produce at least one result.");

            ExactParsedNode top = results[0];
            TestContext.WriteLine($"Top result: {top.ModelType} score={top.Score:F3} " +
                $"waves={top.WaveCount}/{top.ExpectedWaves}");

            Assert.That(top.ModelType, Is.EqualTo(ElliottModelType.DOUBLE_ZIGZAG),
                "Top-scored result should be DOUBLE_ZIGZAG when candle breach checks are active.");

            bool hasDiagonal = results.Any(r =>
                r.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL
                || r.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_ENDING);

            Assert.IsFalse(hasDiagonal,
                "No diagonals should survive when OHLC candle breach checks filter them out.");
        }

        /// <summary>
        /// Builds inner extrema from the loaded USDJPY data using the standard
        /// indicator pipeline: SimpleExtremumFinder → EndFixCorridors → RefineToCorridors.
        /// </summary>
        private List<BarPoint> BuildInnerExtrema()
        {
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

            var innerFinder = new SimpleExtremumFinder(0.01, m_BarsProvider, !isUp);
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
            return innerPoints;
        }
    }
}
