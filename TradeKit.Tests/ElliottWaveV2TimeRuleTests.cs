using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Tests
{
    /// <summary>
    /// Step 4 of EW_MARKUP_v2.md §19 — hard-time death conditions (§8.2/§8.3)
    /// and the cross-level duration-order rule (§9).
    /// Exercises <see cref="ElliottWaveExactMarkupV2.CheckTimeWindow"/> and
    /// <see cref="ElliottWaveExactMarkupV2.CheckDurationOrder"/> with hand-built
    /// wave sequences whose <b>bar spans</b> (durations) are set explicitly.
    /// </summary>
    [TestFixture]
    public class ElliottWaveV2TimeRuleTests
    {
        private static readonly ITimeFrame HOUR1 = TimeFrameHelper.Hour1;
        private static readonly DateTime BASE_TIME = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static BarPoint Pt(int barIndex, double value) =>
            new(value, BASE_TIME.AddHours(barIndex), HOUR1, barIndex);

        /// <summary>
        /// Builds a contiguous segment list from (bar, price) pivots so each wave's
        /// duration (bar span) can be controlled independently.
        /// </summary>
        private static IReadOnlyList<ElliottWaveExactMarkupV2.Segment> Waves(
            params (int bar, double price)[] pivots)
        {
            var segs = new List<ElliottWaveExactMarkupV2.Segment>();
            for (int i = 0; i < pivots.Length - 1; i++)
            {
                BarPoint a = Pt(pivots[i].bar, pivots[i].price);
                BarPoint b = Pt(pivots[i + 1].bar, pivots[i + 1].price);
                segs.Add(new ElliottWaveExactMarkupV2.Segment(a, b));
            }

            return segs;
        }

        // ----- §8.2 time window: IMPULSE (W4 ↔ W2) -----------------------------

        [Test]
        public void Impulse_W4WithinWindowOfW2_Survives()
        {
            // W2 span = 10 bars (10→20). W4 span = 12 bars (within [3, 30]).
            var w = Waves((0, 0), (10, 10), (20, 4), (40, 24), (52, 16));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void Impulse_W4TooLongVsW2_Dies()
        {
            // W2 span = 4 bars. k_max = 3 → window [1.2, 12]. W4 span = 40 > 12.
            var w = Waves((0, 0), (10, 10), (14, 4), (40, 24), (80, 16));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.TIME_WINDOW));
        }

        [Test]
        public void Impulse_W4TooShortVsW2_Dies()
        {
            // W2 span = 30 bars. k_min = 0.3 → window [9, 90]. W4 span = 2 < 9.
            var w = Waves((0, 0), (10, 10), (40, 4), (60, 24), (62, 16));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.TIME_WINDOW));
        }

        [Test]
        public void Impulse_OnlyThreeWaves_NotChecked()
        {
            // W4 not yet present → no time-window check possible.
            var w = Waves((0, 0), (10, 10), (40, 4), (60, 24));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(ElliottModelType.IMPULSE, w),
                Is.EqualTo(DeathReason.NONE));
        }

        // ----- §8.2 time window: ZIGZAG (C ↔ A) --------------------------------

        [Test]
        public void Zigzag_CWithinWindowOfA_Survives()
        {
            // A span = 10. C span = 8 (within [3, 30]).
            var w = Waves((0, 0), (10, 10), (16, 4), (24, 16));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(ElliottModelType.ZIGZAG, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void Zigzag_CTooLongVsA_Dies()
        {
            // A span = 4. window [1.2, 12]. C span = 30 > 12.
            var w = Waves((0, 0), (4, 10), (10, 4), (40, 16));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(ElliottModelType.ZIGZAG, w),
                Is.EqualTo(DeathReason.TIME_WINDOW));
        }

        [Test]
        public void DoubleZigzag_YWithinWindowOfW_Survives()
        {
            var w = Waves((0, 0), (10, 10), (16, 4), (24, 16));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(ElliottModelType.DOUBLE_ZIGZAG, w),
                Is.EqualTo(DeathReason.NONE));
        }

        // ----- soft-timing models are not killed here --------------------------

        [Test]
        public void Triangle_AdjacentTimingIsSoft_NotKilled()
        {
            // Even with wildly uneven spans, triangles get no hard time window.
            var w = Waves((0, 0), (2, 10), (60, 3), (62, 8), (120, 5), (122, 6));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(ElliottModelType.TRIANGLE_CONTRACTING, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void FlatRegular_TimingIsSoft_NotKilled()
        {
            var w = Waves((0, 0), (2, 10), (60, -2), (62, 16));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(ElliottModelType.FLAT_REGULAR, w),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void CustomCoefficients_NarrowWindow_Dies()
        {
            // W2 span = 10. Tight window kMin=0.8,kMax=1.2 → [8, 12]. W4 span = 20 > 12.
            var w = Waves((0, 0), (10, 10), (20, 4), (40, 24), (60, 16));
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(
                    ElliottModelType.IMPULSE, w, kMin: 0.8, kMax: 1.2),
                Is.EqualTo(DeathReason.TIME_WINDOW));
        }

        [Test]
        public void EmptyWaves_Survives()
        {
            Assert.That(
                ElliottWaveExactMarkupV2.CheckTimeWindow(
                    ElliottModelType.IMPULSE, Array.Empty<ElliottWaveExactMarkupV2.Segment>()),
                Is.EqualTo(DeathReason.NONE));
        }

        // ----- §9 duration order (child ≤ parent) ------------------------------

        [Test]
        public void DurationOrder_ParentLongerThanChild_Survives()
        {
            // Parent 100 bars, child 40 bars → parent ≥ 0.9·child.
            Assert.That(
                ElliottWaveExactMarkupV2.CheckDurationOrder(100, 40),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void DurationOrder_ApproximatelyEqual_Survives()
        {
            // Parent 90, child 100 → parent = 0.9·child exactly → allowed.
            Assert.That(
                ElliottWaveExactMarkupV2.CheckDurationOrder(90, 100),
                Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void DurationOrder_ChildLongerThanParent_Dies()
        {
            // Parent 50, child 100 → parent (50) < 0.9·100 = 90 → death.
            Assert.That(
                ElliottWaveExactMarkupV2.CheckDurationOrder(50, 100),
                Is.EqualTo(DeathReason.DURATION_ORDER));
        }

        [Test]
        public void DurationOrder_ChildJustOverThreshold_Dies()
        {
            // Parent 89, child 100 → 89 < 90 → death.
            Assert.That(
                ElliottWaveExactMarkupV2.CheckDurationOrder(89, 100),
                Is.EqualTo(DeathReason.DURATION_ORDER));
        }

        [Test]
        public void DurationOrder_ZeroChild_Survives()
        {
            Assert.That(
                ElliottWaveExactMarkupV2.CheckDurationOrder(0, 0),
                Is.EqualTo(DeathReason.NONE));
        }
    }
}
