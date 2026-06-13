using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Hard-time death conditions (EW_MARKUP_v2 §8.2/§8.3) and the cross-level
    /// duration-order rule (§9).
    /// <para>
    /// These are the genuinely <b>hard</b> temporal prohibitions: a wave whose
    /// duration falls outside the window implied by its same-rank reference dies
    /// with <see cref="DeathReason.TIME_WINDOW"/>, and a child wave that lasts
    /// longer than the parent wave of the same role dies with
    /// <see cref="DeathReason.DURATION_ORDER"/>.
    /// </para>
    /// <para>
    /// The <i>soft</i> temporal expectations — §8.4 ("corrections are statistically
    /// longer than impulses") and §8.5 (trendline geometry of diagonals/triangles)
    /// — influence <c>score</c> rather than kill nodes, and are implemented with
    /// the scoring engine (§16, Step 8) and the projection geometry (Step 6)
    /// respectively.
    /// </para>
    /// </summary>
    public partial class ElliottWaveExactMarkupV2
    {
        /// <summary>
        /// Lower bound of the same-rank duration window (§8.2): a dependent wave's
        /// duration must be at least <c>k_min · durRef</c>.  Wide on the start;
        /// narrows through calibration (§16).
        /// </summary>
        public const double TIME_WINDOW_K_MIN = 0.3;

        /// <summary>
        /// Upper bound of the same-rank duration window (§8.2): a dependent wave's
        /// duration must be at most <c>k_max · durRef</c>.
        /// </summary>
        public const double TIME_WINDOW_K_MAX = 3.0;

        /// <summary>
        /// Minimum fraction of a child wave's duration that the parent wave of the
        /// same role must reach (§9 DURATION_ORDER).  The parent (senior level)
        /// must last at least 90 % of the child (junior level) duration, i.e. a
        /// junior wave may not outlast its senior counterpart.
        /// </summary>
        public const double DURATION_ORDER_MIN_RATIO = 0.9;

        /// <summary>
        /// Incrementally validates the same-rank time-window rule (§8.2/§8.3) for
        /// the given model hypothesis against the waves gathered so far.
        /// </summary>
        /// <param name="model">The model hypothesis being validated.</param>
        /// <param name="waves">The ordered wave segments collected so far.</param>
        /// <param name="kMin">Lower window coefficient (defaults to <see cref="TIME_WINDOW_K_MIN"/>).</param>
        /// <param name="kMax">Upper window coefficient (defaults to <see cref="TIME_WINDOW_K_MAX"/>).</param>
        /// <returns>
        /// <see cref="DeathReason.TIME_WINDOW"/> when a dependent wave's duration
        /// falls outside the window implied by its same-rank reference, otherwise
        /// <see cref="DeathReason.NONE"/>.
        /// </returns>
        public static DeathReason CheckTimeWindow(
            ElliottModelType model,
            IReadOnlyList<Segment> waves,
            double kMin = TIME_WINDOW_K_MIN,
            double kMax = TIME_WINDOW_K_MAX)
        {
            if (waves == null || waves.Count == 0)
                return DeathReason.NONE;

            switch (model)
            {
                case ElliottModelType.IMPULSE:
                    // Hard same-rank pair: W4 (index 3) ↔ W2 (index 1).
                    // W1/W3/W5 timing is soft (§8.3) — handled by scoring.
                    if (waves.Count >= 4)
                        return Window(waves[3], waves[1], kMin, kMax);
                    return DeathReason.NONE;

                case ElliottModelType.DIAGONAL_CONTRACTING_INITIAL:
                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                    // §8.5: diagonals get reduced weight on time — geometry
                    // (trendlines) is the primary criterion.  No hard time
                    // window here; soft penalty is applied via TimeWindowPenalty
                    // in scoring.
                    return DeathReason.NONE;

                case ElliottModelType.ZIGZAG:
                case ElliottModelType.DOUBLE_ZIGZAG:
                case ElliottModelType.TRIPLE_ZIGZAG:
                    // Same-rank pair: C ↔ A (index 2 ↔ 0); for double/triple the
                    // first leg Y ↔ W shares the same index positions.
                    if (waves.Count >= 3)
                        return Window(waves[2], waves[0], kMin, kMax);
                    return DeathReason.NONE;

                default:
                    // Flats and triangles: adjacent-wave timing is soft (§8.3) →
                    // no hard time window here.
                    return DeathReason.NONE;
            }
        }

        /// <summary>
        /// Validates the cross-level duration-order rule (§9): a junior-level wave
        /// may not last longer than the senior-level wave of the same role.
        /// </summary>
        /// <param name="parentWaveBars">Duration (bar span) of the senior wave.</param>
        /// <param name="childWaveBars">Duration (bar span) of the junior wave.</param>
        /// <param name="minParentRatio">
        /// Minimum fraction of the child's duration the parent must reach
        /// (defaults to <see cref="DURATION_ORDER_MIN_RATIO"/>).
        /// </param>
        /// <returns>
        /// <see cref="DeathReason.DURATION_ORDER"/> when the parent is shorter than
        /// <paramref name="minParentRatio"/> × the child, otherwise
        /// <see cref="DeathReason.NONE"/>.
        /// </returns>
        public static DeathReason CheckDurationOrder(
            int parentWaveBars,
            int childWaveBars,
            double minParentRatio = DURATION_ORDER_MIN_RATIO)
        {
            if (childWaveBars <= 0)
                return DeathReason.NONE;

            return parentWaveBars < minParentRatio * childWaveBars
                ? DeathReason.DURATION_ORDER
                : DeathReason.NONE;
        }

        /// <summary>
        /// Checks that <paramref name="dependent"/>'s duration lies within
        /// <c>[kMin · ref, kMax · ref]</c> of <paramref name="reference"/>'s duration.
        /// </summary>
        private static DeathReason Window(
            Segment dependent, Segment reference, double kMin, double kMax)
        {
            int refBars = BarSpan(reference);
            if (refBars <= 0)
                return DeathReason.NONE;

            int depBars = BarSpan(dependent);
            double lo = kMin * refBars;
            double hi = kMax * refBars;

            return depBars < lo || depBars > hi
                ? DeathReason.TIME_WINDOW
                : DeathReason.NONE;
        }

        /// <summary>
        /// Duration of a segment in bars (span between its endpoints), matching
        /// v1's duration semantics used in the hard rules.
        /// </summary>
        internal static int BarSpan(Segment seg) =>
            Math.Abs(seg.End.BarIndex - seg.Start.BarIndex);
    }
}
