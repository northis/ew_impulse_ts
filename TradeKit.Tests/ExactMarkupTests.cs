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
            public ISymbol BarSymbol => null!;

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

        /// <summary>
        /// Builds the list of BarPoints from a generated ModelPattern for use with ElliottWaveExactMarkup.
        /// Extracts main-level key points (level == model.Level) plus the pattern start point.
        /// </summary>
        private static List<BarPoint> BuildPointsFromModel(ModelPattern model, IBarsProvider barsProvider)
        {
            List<PatternKeyPoint> allKeyPoints = model.PatternKeyPoints
                .SelectMany(x => x.Value)
                .OrderBy(x => x.Index)
                .ToList();

            List<PatternKeyPoint> mainWaves = allKeyPoints
                .Where(x => x.Notation.Level == model.Level)
                .OrderBy(x => x.Index)
                .ToList();

            if (mainWaves.Count == 0)
                return new List<BarPoint>();

            PatternKeyPoint endNode = mainWaves.Last();
            bool isUp = endNode.Value > model.Candles[0].O;

            double startVal = isUp
                ? model.Candles.Take(mainWaves[0].Index + 1).Min(x => x.L)
                : model.Candles.Take(mainWaves[0].Index + 1).Max(x => x.H);

            int startIndex = model.Candles.FindIndex(
                x => Math.Abs(x.L - startVal) < double.Epsilon
                  || Math.Abs(x.H - startVal) < double.Epsilon);
            if (startIndex < 0)
                startIndex = 0;

            var points = new List<BarPoint>
            {
                new BarPoint(startVal, model.Candles[startIndex].OpenDate, barsProvider)
            };

            List<PatternKeyPoint> distinctPoints = allKeyPoints
                .Where(x => x.Index > startIndex && x.Index <= endNode.Index)
                .GroupBy(x => x.Index)
                .Select(g => g.OrderByDescending(x => x.Notation.Level).First())
                .OrderBy(x => x.Index)
                .ToList();

            foreach (PatternKeyPoint kp in distinctPoints)
                points.Add(new BarPoint(kp.Value, model.Candles[kp.Index].OpenDate, barsProvider));

            return points;
        }

        /// <summary>
        /// Generates <paramref name="totalTests"/> patterns of the given model type, runs
        /// ElliottWaveExactMarkup.Parse on the ideal key-points, and asserts that at least
        /// <paramref name="threshold"/> fraction of runs correctly identify the model.
        /// </summary>
        private void RunMarkupTest(
            ElliottModelType modelType,
            int totalTests = 20,
            int minBars = 200,
            double threshold = 0.6)
        {
            int matchedTests = 0;

            for (int i = 0; i < totalTests; i++)
            {
                (DateTime start, DateTime end) = Helper.GetDateRange(minBars, TIME_FRAME);
                PatternArgsItem paramArgs = new PatternArgsItem(40, 60, start, end, TIME_FRAME);
                ModelPattern model = m_PatternGenerator.GetPattern(paramArgs, modelType);

                if (model.Candles.Count < 2)
                    continue;

                var barsProvider = new TestBarsProvider(model.Candles);
                List<BarPoint> points = BuildPointsFromModel(model, barsProvider);

                if (points.Count < 2)
                    continue;

                var markup = new ElliottWaveExactMarkup();
                List<ExactParsedNode> results = markup.Parse(points);
                ExactParsedNode? bestResult = results.FirstOrDefault();

                bool matched = bestResult != null
                    && bestResult.ModelType == modelType
                    && bestResult.WaveCount == bestResult.ExpectedWaves;

                if (matched)
                {
                    matchedTests++;
                }
                else
                {
                    Console.WriteLine(
                        $"[{modelType}] run {i}: best={bestResult?.ModelType} " +
                        $"score={bestResult?.Score:F3} waves={bestResult?.WaveCount}/{bestResult?.ExpectedWaves}");
                }
            }

            int minRequired = (int)Math.Ceiling(totalTests * threshold);
            Assert.IsTrue(
                matchedTests >= minRequired,
                $"[{modelType}] only {matchedTests}/{totalTests} runs matched " +
                $"(required ≥ {minRequired}, threshold {threshold:P0}).");
        }

        [Test]
        public void ImpulseExactMarkupTest() =>
            RunMarkupTest(ElliottModelType.IMPULSE, minBars: 300);

        [Test]
        public void DiagonalInitialExactMarkupTest() =>
            RunMarkupTest(ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, minBars: 300);

        [Test]
        public void DiagonalEndingExactMarkupTest() =>
            RunMarkupTest(ElliottModelType.DIAGONAL_CONTRACTING_ENDING, minBars: 300);

        [Test]
        public void ZigzagExactMarkupTest() =>
            RunMarkupTest(ElliottModelType.ZIGZAG, minBars: 200);

        [Test]
        public void DoubleZigzagExactMarkupTest() =>
            RunMarkupTest(ElliottModelType.DOUBLE_ZIGZAG, minBars: 200);

        [Test]
        public void FlatExtendedExactMarkupTest() =>
            RunMarkupTest(ElliottModelType.FLAT_EXTENDED, minBars: 200);

        [Test]
        public void FlatRunningExactMarkupTest() =>
            RunMarkupTest(ElliottModelType.FLAT_RUNNING, minBars: 200);

        [Test]
        public void TriangleContractingExactMarkupTest() =>
            RunMarkupTest(ElliottModelType.TRIANGLE_CONTRACTING, minBars: 300);

        [Test]
        public void TriangleRunningExactMarkupTest() =>
            RunMarkupTest(ElliottModelType.TRIANGLE_RUNNING, minBars: 300);
    }
}
