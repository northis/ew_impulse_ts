using System;
using System.Collections.Generic;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Step 8 of EW_MARKUP_v2.md §16 — node scoring and model-probability calibration.
    /// <para>
    /// The score of a node is built from three independent factors (§16.1):
    /// </para>
    /// <code>
    /// score(node) = fiboScore × P(model | position) × Π(softPenalties)
    /// </code>
    /// <list type="bullet">
    /// <item><c>fiboScore</c> — the pure Fibonacci geometric mean over the model's
    /// price ratios, reusing the v1 maps via
    /// <see cref="ElliottWaveExactMarkup.CalculatePureFiboScore"/>.</item>
    /// <item><c>P(model | position)</c> — the empirically-calibrated probability of
    /// a sub-model appearing at a given parent position (§16.2/§16.3), applied by the
    /// parent when it combines its children; root models use the position-free base
    /// coefficient.</item>
    /// <item><c>softPenalties</c> — overshoot, time-window and truncation factors in
    /// (0,1] that demote (but never kill) structurally weak fits (§16.1).</item>
    /// </list>
    /// </summary>
    public partial class ElliottWaveExactMarkupV2
    {
        // ───────────────────────── §16.4 calibrated thresholds ─────────────────────────

        /// <summary>Hard lower bound on adjacent-wave bar-duration ratio (§8, k_min).</summary>
        private const double K_MIN_TIME = 0.3;

        /// <summary>Hard upper bound on adjacent-wave bar-duration ratio (§8, k_max).</summary>
        private const double K_MAX_TIME = 3.0;

        /// <summary>Lower edge of the unpenalised time-ratio comfort zone.</summary>
        private const double TIME_COMFORT_LOW = 0.6;

        /// <summary>Upper edge of the unpenalised time-ratio comfort zone.</summary>
        private const double TIME_COMFORT_HIGH = 1.7;

        /// <summary>Smallest factor the time-window soft penalty may reach.</summary>
        private const double TIME_PENALTY_FLOOR = 0.6;

        /// <summary>Retracement ratio below which no overshoot penalty applies.</summary>
        private const double OVERSHOOT_COMFORT = 0.9;

        /// <summary>Retracement ratio at which the overshoot penalty saturates.</summary>
        private const double OVERSHOOT_LIMIT = 1.3;

        /// <summary>Smallest factor the overshoot soft penalty may reach.</summary>
        private const double OVERSHOOT_FLOOR = 0.7;

        /// <summary>Score multiplier for a truncated IMPULSE (W5 fails to exceed W3).</summary>
        private const double SOFT_TRUNCATION_PENALTY = 0.3;

        /// <summary>
        /// Empirically-calibrated conditional probabilities <c>P(child | parent, wavePos)</c>
        /// (EW_MARKUP_v2 §16.2/§16.3). Populated from a calibration run over <c>data/</c>;
        /// missing entries fall back to the position-free model coefficient.
        /// </summary>
        private static readonly Dictionary<(ElliottModelType Parent, string WavePos, ElliottModelType Child), double>
            S_POSITION_PROBABILITY = new();

        /// <summary>
        /// Structural quality of a single node: the pure Fibonacci geometric mean times
        /// the soft penalties (§16.1). The <c>P(model | position)</c> factor is applied
        /// separately — by the parent (for sub-waves) or at the root.
        /// </summary>
        private static double StructuralScore(ElliottModelType model, IReadOnlyList<Segment> waves)
        {
            double fibo = ElliottWaveExactMarkup.CalculatePureFiboScore(model, waves);
            return fibo * SoftPenalties(model, waves);
        }

        /// <summary>
        /// Product of the §16.1 soft penalties — overshoot, time-window and truncation.
        /// Each is in (0,1]; together they demote weak fits without killing them.
        /// </summary>
        private static double SoftPenalties(ElliottModelType model, IReadOnlyList<Segment> waves)
        {
            return OvershootPenalty(waves)
                   * TimeWindowPenalty(waves)
                   * TruncationPenalty(model, waves);
        }

        /// <summary>
        /// Geometric-mean penalty over the corrective (counter-trend) waves, demoting
        /// retracements that approach the structural cancellation boundary (§16.1 overshoot).
        /// </summary>
        private static double OvershootPenalty(IReadOnlyList<Segment> waves)
        {
            if (waves.Count < 2)
                return 1.0;

            bool trendUp = waves[0].IsUp;
            double product = 1.0;
            int count = 0;

            for (int i = 1; i < waves.Count; i++)
            {
                if (waves[i].IsUp == trendUp)
                    continue; // not a counter-trend (corrective) wave

                double prev = waves[i - 1].Length;
                if (prev <= 0)
                    continue;

                product *= OvershootFactor(waves[i].Length / prev);
                count++;
            }

            return count == 0 ? 1.0 : Math.Pow(product, 1.0 / count);
        }

        /// <summary>Maps a retracement ratio to a [<see cref="OVERSHOOT_FLOOR"/>, 1] factor.</summary>
        private static double OvershootFactor(double retracement)
        {
            if (retracement <= OVERSHOOT_COMFORT)
                return 1.0;
            if (retracement >= OVERSHOOT_LIMIT)
                return OVERSHOOT_FLOOR;

            double t = (retracement - OVERSHOOT_COMFORT) / (OVERSHOOT_LIMIT - OVERSHOOT_COMFORT);
            return 1.0 - (1.0 - OVERSHOOT_FLOOR) * t;
        }

        /// <summary>
        /// Geometric-mean penalty over adjacent waves whose bar-duration ratio drifts
        /// toward the hard time-window edges (§16.1 time-window).
        /// </summary>
        private static double TimeWindowPenalty(IReadOnlyList<Segment> waves)
        {
            if (waves.Count < 2)
                return 1.0;

            double product = 1.0;
            int count = 0;

            for (int i = 1; i < waves.Count; i++)
            {
                int prevBars = waves[i - 1].BarsCount;
                int curBars = waves[i].BarsCount;
                if (prevBars <= 0 || curBars <= 0)
                    continue;

                product *= TimeWindowFactor((double)curBars / prevBars);
                count++;
            }

            return count == 0 ? 1.0 : Math.Pow(product, 1.0 / count);
        }

        /// <summary>
        /// Maps a bar-duration ratio to a [<see cref="TIME_PENALTY_FLOOR"/>, 1] factor:
        /// 1.0 inside the comfort zone, decaying linearly toward the hard k_min/k_max edges.
        /// </summary>
        private static double TimeWindowFactor(double ratio)
        {
            if (ratio >= TIME_COMFORT_LOW && ratio <= TIME_COMFORT_HIGH)
                return 1.0;

            double t;
            if (ratio < TIME_COMFORT_LOW)
            {
                double span = TIME_COMFORT_LOW - K_MIN_TIME;
                t = span <= 0 ? 0.0 : (ratio - K_MIN_TIME) / span;
            }
            else
            {
                double span = K_MAX_TIME - TIME_COMFORT_HIGH;
                t = span <= 0 ? 0.0 : (K_MAX_TIME - ratio) / span;
            }

            t = Math.Clamp(t, 0.0, 1.0);
            return TIME_PENALTY_FLOOR + (1.0 - TIME_PENALTY_FLOOR) * t;
        }

        /// <summary>
        /// Truncation penalty (§16.1): an IMPULSE whose wave 5 fails to exceed wave 3's
        /// extreme is a rare, weak formation and is demoted below comparable impulses.
        /// </summary>
        private static double TruncationPenalty(ElliottModelType model, IReadOnlyList<Segment> waves)
        {
            if (model != ElliottModelType.IMPULSE || waves.Count < 5)
                return 1.0;

            bool trendUp = waves[0].IsUp;
            bool truncated = trendUp
                ? waves[4].End.Value < waves[2].End.Value
                : waves[4].End.Value > waves[2].End.Value;

            return truncated ? SOFT_TRUNCATION_PENALTY : 1.0;
        }

        /// <summary>
        /// The §16.2 conditional probability of <paramref name="child"/> appearing at
        /// position <paramref name="wavePos"/> of <paramref name="parent"/>. Falls back
        /// to the position-free base coefficient when no calibrated value exists.
        /// </summary>
        private static double PositionProbability(
            ElliottModelType parent, string wavePos, ElliottModelType child)
        {
            if (wavePos != null
                && S_POSITION_PROBABILITY.TryGetValue((parent, wavePos, child), out double p))
                return p;

            return ModelProbability(child);
        }
    }
}
