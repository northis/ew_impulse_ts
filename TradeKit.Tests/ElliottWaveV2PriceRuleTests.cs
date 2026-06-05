using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Tests
{
    /// <summary>
    /// Step 3 of EW_MARKUP_v2.md §19 — hard-price death conditions (§7).
    /// Directly exercises <see cref="ElliottWaveExactMarkupV2.CheckPriceRules"/>
    /// with hand-built wave sequences: valid structures must survive
    /// (<see cref="DeathReason.NONE"/>) and each forbidden structure must die
    /// with <see cref="DeathReason.PRICE_BREACH"/>.
    /// <para>
    /// The data-wide T-MK-2 ("no COMPLETE node violates §7") arrives in Step 5
    /// once the beam search actually produces COMPLETE nodes; here we validate
    /// the predicate itself, which that test will rely on.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ElliottWaveV2PriceRuleTests
    {
        private static readonly ITimeFrame HOUR1 = TimeFrameHelper.Hour1;
        private static readonly DateTime BASE_TIME = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Builds a contiguous list of <see cref="ElliottWaveExactMarkupV2.Segment"/>
        /// from price pivots; pivot <c>i</c> sits on bar <c>i</c>.
        /// </summary>
        private static IReadOnlyList<ElliottWaveExactMarkupV2.Segment> Waves(params double[] pivots)
        {
            var segs = new List<ElliottWaveExactMarkupV2.Segment>();
            for (int i = 0; i < pivots.Length - 1; i++)
            {
                BarPoint a = Pt(i, pivots[i]);
                BarPoint b = Pt(i + 1, pivots[i + 1]);
                segs.Add(new ElliottWaveExactMarkupV2.Segment(a, b));
            }

            return segs;
        }

        private static BarPoint Pt(int barIndex, double value) =>
            new(value, BASE_TIME.AddHours(barIndex), HOUR1, barIndex);

        // ----- IMPULSE ---------------------------------------------------------

        [Test]
        public void Impulse_ValidUp_Survives()
        {
            // W1 0→10, W2 →4, W3 →24, W4 →16, W5 →30
            var w = Waves(0, 10, 4, 24, 16, 30);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void Impulse_ValidDown_Survives()
        {
            // Mirror of the up case.
            var w = Waves(0, -10, -4, -24, -16, -30);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void Impulse_Wave2BeyondStart_Dies()
        {
            // W2 retraces to -2, below W1 start (0).
            var w = Waves(0, 10, -2, 24, 16, 30);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void Impulse_Wave3NotBeyondWave1End_Dies()
        {
            // W3 ends at 9, below W1 end (10): no new extreme.
            var w = Waves(0, 10, 4, 9, 6, 12);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void Impulse_Wave4OverlapsWave1_SimpleW4_Dies()
        {
            // W4 ends at 8, inside the W1 zone (W1 end = 10). Simple W4 → death.
            var w = Waves(0, 10, 4, 24, 8, 30);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.IMPULSE, w, wave4Simple: true),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void Impulse_Wave4OverlapsWave1_ComplexW4_Survives()
        {
            // Same overlap, but W4 is a triangle/flat (§6.3 exception) → allowed.
            var w = Waves(0, 10, 4, 24, 8, 30);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.IMPULSE, w, wave4Simple: false),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void Impulse_Wave3Shortest_Dies()
        {
            // |W1|=10, |W3|=6, |W5|=12 → W3 strictly shortest.
            var w = Waves(0, 10, 4, 10, 7, 19);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void Impulse_Wave5NotBeyondWave4End_Dies()
        {
            // W5 ends at 13, below W4 end (16): no new extreme.
            var w = Waves(0, 10, 4, 24, 16, 13);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void Impulse_PartialSequence_NotPrematurelyKilled()
        {
            // Only W1+W2 present and valid → must not die on incomplete data.
            var w = Waves(0, 10, 4);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.NONE));
        }

        // ----- ZIGZAG ----------------------------------------------------------

        [Test]
        public void Zigzag_Valid_Survives()
        {
            // A 0→10, B →4, C →16.
            var w = Waves(0, 10, 4, 16);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.ZIGZAG, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void Zigzag_BBeyondStart_Dies()
        {
            // B retraces to -3, beyond A start (0).
            var w = Waves(0, 10, -3, 16);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.ZIGZAG, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void Zigzag_CNotBeyondAEnd_Dies()
        {
            // C ends at 9, below A end (10): no new extreme.
            var w = Waves(0, 10, 4, 9);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.ZIGZAG, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        // ----- FLAT ------------------------------------------------------------

        [Test]
        public void FlatExtended_BOvershootsOrigin_Survives()
        {
            // A 0→10, B overshoots origin to -2, C →16.
            var w = Waves(0, 10, -2, 16);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.FLAT_EXTENDED, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void FlatExtended_BFailsToReachOrigin_Dies()
        {
            // B only retraces to 3, never reaching origin (0): must die.
            var w = Waves(0, 10, 3, 16);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.FLAT_EXTENDED, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void FlatRegular_BOvershootsOrigin_Dies()
        {
            // Regular flat: B must NOT overshoot origin; here B = -3 → death.
            var w = Waves(0, 10, -3, 16);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.FLAT_REGULAR, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        // ----- TRIANGLE --------------------------------------------------------

        [Test]
        public void TriangleContracting_Valid_Survives()
        {
            // a 0→10, b →3, c →8, d →5, e →6 — converging, b within origin.
            var w = Waves(0, 10, 3, 8, 5, 6);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.TRIANGLE_CONTRACTING, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void TriangleContracting_CExceedsA_Dies()
        {
            // c (8→12) breaks above a's peak (10): not converging → death.
            var w = Waves(0, 10, 3, 12, 5, 6);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.TRIANGLE_CONTRACTING, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void TriangleContracting_BOvershootsOrigin_Dies()
        {
            // b overshoots origin to -2 → that is a running triangle, not contracting.
            var w = Waves(0, 10, -2, 8, 5, 6);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.TRIANGLE_CONTRACTING, w),
                Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void TriangleRunning_BOvershootsOrigin_Survives()
        {
            // Running triangle: b overshoots origin to -2; subsequent waves converge.
            var w = Waves(0, 10, -2, 9, 3, 6);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.TRIANGLE_RUNNING, w),
                Is.EqualTo(DeathReason.NONE));
        }

        // ----- misc ------------------------------------------------------------

        [Test]
        public void SimpleImpulse_AlwaysSurvives()
        {
            var w = Waves(0, 10);
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(ElliottModelType.SIMPLE_IMPULSE, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void EmptyWaves_Survives()
        {
            Assert.That(
                ElliottWaveExactMarkupV2.CheckPriceRules(
                    ElliottModelType.IMPULSE, Array.Empty<ElliottWaveExactMarkupV2.Segment>()),
                Is.EqualTo(DeathReason.NONE));
        }
    }
}
