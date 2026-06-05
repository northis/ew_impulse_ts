using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Tests
{
    /// <summary>
    /// Step 8 of EW_MARKUP_v2.md §16 — node scoring.
    /// Exercises the public pure-Fibonacci scorer
    /// (<see cref="ElliottWaveExactMarkup.CalculatePureFiboScore"/>) and the end-to-end
    /// score that the v2 search assigns to its roots, asserting the §16.1 contract:
    /// scores are bounded in (0,1], deterministic, ordering-sensitive (a textbook
    /// structure outranks a distorted one) and the roots come back score-descending.
    /// </summary>
    [TestFixture]
    public class ElliottWaveV2ScoringTests
    {
        private static readonly ITimeFrame HOUR1 = TimeFrameHelper.Hour1;
        private static readonly DateTime BASE_TIME = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static BarPoint Pt(int barIndex, double value) =>
            new(value, BASE_TIME.AddHours(barIndex), HOUR1, barIndex);

        /// <summary>Builds segments from strictly-alternating pivot values spaced two bars apart.</summary>
        private static IReadOnlyList<ElliottWaveExactMarkupV2.Segment> Waves(params double[] values)
        {
            var waves = new List<ElliottWaveExactMarkupV2.Segment>(values.Length - 1);
            for (int i = 1; i < values.Length; i++)
                waves.Add(new ElliottWaveExactMarkupV2.Segment(Pt((i - 1) * 2, values[i - 1]), Pt(i * 2, values[i])));
            return waves;
        }

        // Textbook up-impulse: W1 0→10, W2 →5 (0.5 retrace), W3 →21.18 (1.618×W1),
        // W4 →15 (0.382 retrace of W3), W5 →25 (= W1, 1.0×W1).
        private static IReadOnlyList<ElliottWaveExactMarkupV2.Segment> CleanImpulse() =>
            Waves(0, 10, 5, 21.18, 15, 25);

        // Distorted impulse: W3 barely moves (0.3×W1) and W5 is tiny — poor Fibo fits.
        private static IReadOnlyList<ElliottWaveExactMarkupV2.Segment> DistortedImpulse() =>
            Waves(0, 10, 5, 8, 7, 9);

        [Test]
        public void PureFiboScore_IsBoundedInUnitInterval()
        {
            double clean = ElliottWaveExactMarkup.CalculatePureFiboScore(
                ElliottModelType.IMPULSE, CleanImpulse());

            Assert.That(clean, Is.GreaterThan(0.0));
            Assert.That(clean, Is.LessThanOrEqualTo(1.0));
        }

        [Test]
        public void PureFiboScore_IsDeterministic()
        {
            IReadOnlyList<ElliottWaveExactMarkupV2.Segment> waves = CleanImpulse();

            double first = ElliottWaveExactMarkup.CalculatePureFiboScore(ElliottModelType.IMPULSE, waves);
            double second = ElliottWaveExactMarkup.CalculatePureFiboScore(ElliottModelType.IMPULSE, waves);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void PureFiboScore_TextbookImpulse_OutranksDistortedImpulse()
        {
            double clean = ElliottWaveExactMarkup.CalculatePureFiboScore(
                ElliottModelType.IMPULSE, CleanImpulse());
            double distorted = ElliottWaveExactMarkup.CalculatePureFiboScore(
                ElliottModelType.IMPULSE, DistortedImpulse());

            Assert.That(clean, Is.GreaterThan(distorted),
                "A textbook-proportioned impulse must score above a Fibo-distorted one.");
        }

        [Test]
        public void PureFiboScore_RatioFreeModel_ReturnsNeutralOne()
        {
            // SIMPLE_IMPULSE carries no Fibo ratios (§10) → the pure score is neutral.
            IReadOnlyList<ElliottWaveExactMarkupV2.Segment> singleLeg = Waves(0, 10);

            double score = ElliottWaveExactMarkup.CalculatePureFiboScore(
                ElliottModelType.SIMPLE_IMPULSE, singleLeg);

            Assert.That(score, Is.EqualTo(1.0));
        }

        [Test]
        public void Parse_Roots_AreOrderedByScoreDescending()
        {
            var markup = new ElliottWaveExactMarkupV2(null, PivotsOf(0, 10, 5, 21.18, 15, 25));

            MarkupSearchResult result = markup.Parse();

            Assert.That(result.Roots, Is.Not.Empty);
            for (int i = 1; i < result.Roots.Count; i++)
                Assert.That(result.Roots[i - 1].Score, Is.GreaterThanOrEqualTo(result.Roots[i].Score),
                    "Roots must be returned in non-increasing score order.");
        }

        [Test]
        public void Parse_Roots_HaveStrictlyPositiveScores()
        {
            var markup = new ElliottWaveExactMarkupV2(null, PivotsOf(0, 10, 5, 21.18, 15, 25));

            MarkupSearchResult result = markup.Parse();

            Assert.That(result.Roots, Is.Not.Empty);
            foreach (TreeNode root in result.Roots)
                Assert.That(root.Score, Is.GreaterThan(0.0),
                    $"{root.Model} root must carry a strictly-positive score.");
        }

        [Test]
        public void Parse_Score_IsDeterministic()
        {
            IReadOnlyList<BarPoint> pivots = PivotsOf(0, 10, 5, 21.18, 15, 25);

            string first = ScoreSignature(new ElliottWaveExactMarkupV2(null, pivots).Parse().Roots);
            string second = ScoreSignature(new ElliottWaveExactMarkupV2(null, pivots).Parse().Roots);

            Assert.That(second, Is.EqualTo(first),
                "Identical input must yield identical root scores.");
        }

        private static IReadOnlyList<BarPoint> PivotsOf(params double[] values)
        {
            var pts = new List<BarPoint>(values.Length);
            for (int i = 0; i < values.Length; i++)
                pts.Add(Pt(i * 2, values[i]));
            return pts;
        }

        private static string ScoreSignature(IReadOnlyList<TreeNode> roots)
        {
            return string.Join("|", roots.Select(r =>
                $"{r.Model}:{r.RangeStartSegment}-{r.RangeEndSegment}:{r.Score:R}"));
        }
    }
}
