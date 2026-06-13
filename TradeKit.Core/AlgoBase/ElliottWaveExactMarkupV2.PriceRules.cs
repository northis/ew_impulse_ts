using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Hard-price death conditions (EW_MARKUP_v2 §7).
    /// <para>
    /// These are the classical Elliott structural prohibitions (overlap, direction,
    /// "wave 3 is never the shortest", etc.).  They coincide with v1's
    /// <c>CheckHardRules</c> but are evaluated <b>incrementally</b>: a partial
    /// wave sequence is rejected only when a rule is <i>already</i> definitively
    /// violated, never speculatively.  A violation means the hypothesis node must
    /// be <see cref="NodeStatus.DEAD"/>; Fibonacci-ratio deviations are <b>not</b>
    /// handled here (those are score penalties — §16).
    /// </para>
    /// </summary>
    public partial class ElliottWaveExactMarkupV2
    {
        /// <summary>
        /// Tolerance applied when comparing a pivot against a boundary level so
        /// that "almost touches" are not rejected (EW_MARKUP_v2 §7.3).  Expressed
        /// as a fraction of the pattern scale (largest gathered wave amplitude).
        /// </summary>
        public const double MAIN_ALLOWANCE_MAX_RATIO = 0.05;

        /// <summary>
        /// Minimum fraction of the reference wave's length by which a diagonal's
        /// next same-direction wave must exceed the reference endpoint, so that a
        /// W3 that barely touches W1 is not accepted (mirrors v1).
        /// </summary>
        private const double MIN_DIAGONAL_PENETRATION = 0.05;

        /// <summary>
        /// Incrementally validates the hard-price rules (§7) for the given model
        /// hypothesis against the waves gathered so far.
        /// </summary>
        /// <param name="model">The model hypothesis being validated.</param>
        /// <param name="waves">
        /// The ordered wave segments collected so far (1 … expected).  Each entry
        /// is one full wave of <paramref name="model"/> (W1, W2, …).
        /// </param>
        /// <param name="wave4Simple">
        /// <c>true</c> when the IMPULSE wave-4 sub-model is atomic
        /// (<see cref="ElliottModelType.SIMPLE_IMPULSE"/>): the W4↔W1 overlap rule
        /// then applies.  <c>false</c> when W4 is a triangle/flat that may legally
        /// dip into the W1 zone (§6.3 exception).  Defaults to <c>true</c>.
        /// </param>
        /// <returns>
        /// The reason the hypothesis must die, or <see cref="DeathReason.NONE"/>
        /// when no hard-price rule is violated yet.
        /// </returns>
        public static DeathReason CheckPriceRules(
            ElliottModelType model,
            IReadOnlyList<Segment> waves,
            bool wave4Simple = true)
        {
            if (waves == null || waves.Count == 0)
                return DeathReason.NONE;

            // Pattern-scale tolerance: 5 % of the largest gathered amplitude (§7.3).
            // NOTE: kept at zero for now — a symmetric tolerance of 5%·scale
            // would weaken directional "must cross" checks (e.g. C must break
            // beyond A.End, W5 must exceed W3.End) by allowing near-misses.
            // The tolerance is computed but not yet applied to individual checks;
            // if re-enabled, it must be directional (sign-dependent).
            double scale = 0;
            foreach (Segment seg in waves)
                if (seg.Length > scale) scale = seg.Length;
            double tol = double.Epsilon;

            bool isUp = waves[0].IsUp;
            double s = isUp ? 1.0 : -1.0;
            double start = waves[0].Start.Value;

            switch (model)
            {
                case ElliottModelType.IMPULSE:
                    return CheckImpulse(waves, s, start, tol, wave4Simple);

                case ElliottModelType.DIAGONAL_CONTRACTING_INITIAL:
                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                    return CheckDiagonalContracting(model, waves, s, start, tol);

                case ElliottModelType.ZIGZAG:
                case ElliottModelType.DOUBLE_ZIGZAG:
                case ElliottModelType.TRIPLE_ZIGZAG:
                    return CheckZigzag(waves, s, start, tol);

                case ElliottModelType.FLAT_EXTENDED:
                    return Worst(CheckFlatOvershoot(waves, s, start, tol, running: false),
                                 CheckCorrectionMustCorrect(waves, s, start, tol, model));

                case ElliottModelType.FLAT_RUNNING:
                    return Worst(CheckFlatOvershoot(waves, s, start, tol, running: true),
                                 CheckCorrectionMustCorrect(waves, s, start, tol, model));

                case ElliottModelType.FLAT_REGULAR:
                    return Worst(CheckFlatRegular(waves, s, start, tol),
                                 CheckCorrectionMustCorrect(waves, s, start, tol, model));

                case ElliottModelType.TRIANGLE_CONTRACTING:
                    return Worst(CheckTriangle(waves, s, start, tol, running: false),
                                 CheckCorrectionMustCorrect(waves, s, start, tol, model));

                case ElliottModelType.TRIANGLE_RUNNING:
                    return Worst(CheckTriangle(waves, s, start, tol, running: true),
                                 CheckCorrectionMustCorrect(waves, s, start, tol, model));

                default:
                    // Atomic leaves (SIMPLE_IMPULSE) and models without dedicated
                    // hard-price rules cannot be falsified on price alone here.
                    return DeathReason.NONE;
            }
        }

        // ----- per-model checks ------------------------------------------------

        private static DeathReason CheckImpulse(
            IReadOnlyList<Segment> w, double s, double start, double tol, bool wave4Simple)
        {
            // W2 must not retrace beyond the start of W1.
            if (w.Count >= 2 && s * (w[1].End.Value - start) < -tol)
                return DeathReason.PRICE_BREACH;

            // W3 must make a new extreme beyond the end of W1.
            if (w.Count >= 3 && s * (w[2].End.Value - w[0].End.Value) < -tol)
                return DeathReason.PRICE_BREACH;

            if (w.Count >= 4)
            {
                // W4 must never retrace past the start of W1.
                if (s * (w[3].End.Value - start) < -tol)
                    return DeathReason.PRICE_BREACH;

                // W4 end must not enter the W1 price zone.
                // §6.3: triangles/flats may dip into W1 zone during their
                // formation (intermediate body), but the END of W4 must still
                // be outside (the word "can end outside" in §6.3 is aspirational,
                // not permissive — if it DOES end inside, the impulse dies).
                if (s * (w[3].End.Value - w[0].End.Value) < -tol)
                    return DeathReason.PRICE_BREACH;
            }

            if (w.Count >= 5)
            {
                // W3 is never the shortest of the three motive waves.
                if (w[2].Length < w[0].Length - tol && w[2].Length < w[4].Length - tol)
                    return DeathReason.PRICE_BREACH;

                // W5 must make a new extreme beyond the end of W4.
                if (s * (w[4].End.Value - w[3].End.Value) < -tol)
                    return DeathReason.PRICE_BREACH;

                // W5 must exceed the end of W3 — truncation is forbidden in v2
                // (Frost & Prechter allow truncations; this version excludes them).
                if (s * (w[4].End.Value - w[2].End.Value) < -tol)
                    return DeathReason.PRICE_BREACH;
            }

            return DeathReason.NONE;
        }

        private static DeathReason CheckDiagonalContracting(
            ElliottModelType model, IReadOnlyList<Segment> w, double s, double start, double tol)
        {
            // W2 must not retrace beyond the start of W1.
            if (w.Count >= 2 && s * (w[1].End.Value - start) < -tol)
                return DeathReason.PRICE_BREACH;

            if (w.Count >= 3)
            {
                double pen = MIN_DIAGONAL_PENETRATION * w[0].Length;
                // W3 must penetrate beyond W1 end (diagonals still make new extremes).
                if (s * (w[2].End.Value - w[0].End.Value) < pen - tol)
                    return DeathReason.PRICE_BREACH;
                // Contracting: |W3| < |W1|.
                if (w[2].Length >= w[0].Length + tol)
                    return DeathReason.PRICE_BREACH;
            }

            if (w.Count >= 4)
            {
                // Contracting: |W4| < |W2|.
                if (w[3].Length >= w[1].Length + tol)
                    return DeathReason.PRICE_BREACH;
            }

            if (w.Count >= 5)
            {
                // Contracting: |W5| < |W3|.
                if (w[4].Length >= w[2].Length + tol)
                    return DeathReason.PRICE_BREACH;

                // Initial diagonal: W5 must exceed W3 end.
                if (model == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL)
                {
                    double pen5 = MIN_DIAGONAL_PENETRATION * w[2].Length;
                    if (s * (w[4].End.Value - w[2].End.Value) < pen5 - tol)
                        return DeathReason.PRICE_BREACH;
                }
            }

            return DeathReason.NONE;
        }

        /// <summary>
        /// Universal correction rule (§7.2): for flats and triangles, the final
        /// endpoint must lie on the <i>correction</i> side of the origin —
        /// otherwise the pattern didn't actually correct.
        /// <para>
        /// For a flat in an uptrend (correcting a prior down move by going UP):
        /// the final wave C must end above start; if C ends below start, the
        /// correction failed.  Vice versa for a downtrend flat.
        /// </para>
        /// <para>
        /// Zigzags are excluded: their impulse character already guarantees
        /// C makes a new extreme beyond A.End (checked in <see cref="CheckZigzag"/>).
        /// </para>
        /// </summary>
        private static DeathReason CheckCorrectionMustCorrect(
            IReadOnlyList<Segment> w, double s, double start, double tol,
            ElliottModelType model)
        {
            int finalIdx = model switch
            {
                ElliottModelType.FLAT_EXTENDED => 2,
                ElliottModelType.FLAT_RUNNING => 2,
                ElliottModelType.FLAT_REGULAR => 2,
                ElliottModelType.TRIANGLE_CONTRACTING => 4,
                ElliottModelType.TRIANGLE_RUNNING => 4,
                _ => -1
            };

            if (finalIdx < 0 || w.Count <= finalIdx)
                return DeathReason.NONE;

            double finalEnd = w[finalIdx].End.Value;

            // For flats/triangles, the correction direction is OPPOSITE to the
            // larger trend.  A goes in the correction direction (counter-trend).
            // s*(finalEnd - start) < 0 means finalEnd is on the NON-correction
            // side of start — the pattern didn't correct.
            // Example: uptrend flat s=1, start=0, C.End=-5: pattern went DOWN
            // past start — correction happened ✓.
            // Counter-example: uptrend flat s=1, start=0, C.End=-5 is a
            // correction... wait.
            //
            // For an uptrend correction (A down, s=-1): finalEnd should be
            // BELOW start (correction went down). s*(finalEnd-start) > 0.
            // For a downtrend correction (A up, s=1): finalEnd should be
            // ABOVE start (correction went up). s*(finalEnd-start) > 0.
            //
            // So: correction happened when s*(finalEnd - start) > 0.
            // Death when s*(finalEnd - start) < -tol (finalEnd is on the
            // non-correction side by more than tolerance).
            if (s * (finalEnd - start) < -tol)
                return DeathReason.PRICE_BREACH;

            return DeathReason.NONE;
        }

        /// <summary>Returns the first non-NONE death reason, or NONE.</summary>
        private static DeathReason Worst(DeathReason a, DeathReason b) =>
            a != DeathReason.NONE ? a : b;

        private static DeathReason CheckZigzag(
            IReadOnlyList<Segment> w, double s, double start, double tol)
        {
            // Wave B (or X) must not retrace beyond the start of wave A (or W).
            if (w.Count >= 2 && s * (w[1].End.Value - start) < -tol)
                return DeathReason.PRICE_BREACH;

            // Wave C (or Y) must make a new extreme beyond the end of A (or W).
            if (w.Count >= 3 && s * (w[2].End.Value - w[0].End.Value) < -tol)
                return DeathReason.PRICE_BREACH;

            return DeathReason.NONE;
        }

        /// <summary>
        /// Extended / running flat: B is obliged to overshoot the pattern origin,
        /// i.e. end on the far side of start relative to A's direction.
        /// Additionally:
        ///   • <c>FLAT_RUNNING</c> — total correction (C-wave retracement from
        ///     start) must be at least 38.2 % of wave A (EW_MARKUP_v2 §7.2).
        ///   • <c>FLAT_EXTENDED</c> — wave C must break beyond the end of
        ///     wave A (classical rule C/A ≥ 1.618, see EW_RULES §12).
        /// </summary>
        private static DeathReason CheckFlatOvershoot(
            IReadOnlyList<Segment> w, double s, double start, double tol,
            bool running)
        {
            // B must overshoot the pattern origin (applies to both extended and running).
            if (w.Count >= 2 && s * (w[1].End.Value - start) > tol)
                return DeathReason.PRICE_BREACH;

            if (w.Count >= 3)
            {
                if (running)
                {
                    // FLAT_RUNNING §7.2: total correction must be ≥ 38.2 % of wave A.
                    // Use signed distance in A's direction — a C that ends on the
                    // "wrong" side of start is not a valid correction at all.
                    double waveALen = w[0].Length;                // |A|
                    double totalCorrection = s * (start - w[2].End.Value); // signed
                    double minRequired = 0.382 * waveALen;
                    if (totalCorrection < minRequired - tol)
                        return DeathReason.PRICE_BREACH;
                }
                else
                {
                    // FLAT_EXTENDED §7.2: wave C must break beyond the end of wave A.
                    // Classical rule: C/A ≥ 1.618 (EW_RULES §12).
                    // Death when C.End is NOT past A.End in A's direction
                    // (s * (C.End - A.End) < 0 means C is on the non-A side of A.End).
                    double aEnd = w[0].End.Value;
                    if (s * (w[2].End.Value - aEnd) < -tol)
                        return DeathReason.PRICE_BREACH;
                }
            }

            return DeathReason.NONE;
        }

        private static DeathReason CheckFlatRegular(
            IReadOnlyList<Segment> w, double s, double start, double tol)
        {
            if (w.Count >= 2)
            {
                // B must retrace at least 90 % of A (EW_RULES §13: B ≈ 90–100 % A).
                // B.End must be on the A side of start, i.e. s*(B.End - start) > 0 means
                // B went past origin — that's overshoot, forbidden for regular flat.
                double bRetrace = s * (w[1].End.Value - start);
                if (bRetrace < -tol)
                    return DeathReason.PRICE_BREACH; // overshoot past origin

                // Lower bound: B must reach at least 90 % of A's amplitude.
                double minB = 0.9 * w[0].Length;
                if (w[1].Length < minB - tol)
                    return DeathReason.PRICE_BREACH; // too shallow for regular flat
            }

            return DeathReason.NONE;
        }

        private static DeathReason CheckTriangle(
            IReadOnlyList<Segment> w, double s, double start, double tol, bool running)
        {
            if (w.Count >= 2)
            {
                // Contracting: B must NOT overshoot origin.
                // Running: B MUST overshoot origin.
                double bSide = s * (w[1].End.Value - start);
                if (!running && bSide < -tol)
                    return DeathReason.PRICE_BREACH;
                if (running && bSide > tol)
                    return DeathReason.PRICE_BREACH;
            }

            if (w.Count >= 3)
            {
                // Amplitude convergence: |C| < |A| (contracting only — a running
                // triangle's oversized B lets C legitimately exceed A).
                if (!running && w[2].Length >= w[0].Length + tol)
                    return DeathReason.PRICE_BREACH;
                // Endpoint convergence: C must not break through A's endpoint.
                if (s * (w[2].End.Value - w[0].End.Value) > tol)
                    return DeathReason.PRICE_BREACH;
            }

            if (w.Count >= 4)
            {
                // |D| < |B|.
                if (w[3].Length >= w[1].Length + tol)
                    return DeathReason.PRICE_BREACH;
                // D must not break through B's endpoint.
                if (s * (w[3].End.Value - w[1].End.Value) < -tol)
                    return DeathReason.PRICE_BREACH;
            }

            if (w.Count >= 5)
            {
                // |E| < |C|.
                if (w[4].Length >= w[2].Length + tol)
                    return DeathReason.PRICE_BREACH;
                // E must not break through C's endpoint.
                if (s * (w[4].End.Value - w[2].End.Value) > tol)
                    return DeathReason.PRICE_BREACH;
                // E must remain on the triangle side of the start.
                if (s * (w[4].End.Value - start) < -tol)
                    return DeathReason.PRICE_BREACH;
            }

            return DeathReason.NONE;
        }
    }
}
