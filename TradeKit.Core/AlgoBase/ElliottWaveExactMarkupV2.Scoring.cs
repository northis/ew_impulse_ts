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
        /// (EW_MARKUP_v2 §16.2/§16.3). Calibrated by <c>ElliottWaveV2CalibrationTests</c> from a
        /// whole-history stitch (<see cref="ParseContinuous"/>) over all 56 <c>data/</c> files
        /// (8224 top-level tiles): each observed parent→child edge frequency is renormalised to
        /// preserve its <c>(parent, wavePos)</c> base-coefficient budget, so values stay on the
        /// same scale as the fallback. Missing entries fall back to the position-free coefficient.
        /// Regenerate with <c>dotnet test --filter "FullyQualifiedName~ElliottWaveV2Calibration"</c>.
        /// </summary>
        private static readonly Dictionary<(ElliottModelType Parent, string WavePos, ElliottModelType Child), double>
            S_POSITION_PROBABILITY = new()
            {
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "1", ElliottModelType.SIMPLE_IMPULSE), 1.663835 }, // 457/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "1", ElliottModelType.ZIGZAG), 0.294903 }, // 81/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "1", ElliottModelType.DOUBLE_ZIGZAG), 0.291262 }, // 80/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "2", ElliottModelType.SIMPLE_IMPULSE), 2.104369 }, // 578/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "2", ElliottModelType.ZIGZAG), 0.087379 }, // 24/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "2", ElliottModelType.DOUBLE_ZIGZAG), 0.058252 }, // 16/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "3", ElliottModelType.SIMPLE_IMPULSE), 1.714806 }, // 471/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "3", ElliottModelType.ZIGZAG), 0.298544 }, // 82/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "3", ElliottModelType.DOUBLE_ZIGZAG), 0.23665 }, // 65/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "4", ElliottModelType.SIMPLE_IMPULSE), 2.115291 }, // 581/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "4", ElliottModelType.DOUBLE_ZIGZAG), 0.083738 }, // 23/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "4", ElliottModelType.ZIGZAG), 0.050971 }, // 14/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "5", ElliottModelType.SIMPLE_IMPULSE), 2.089806 }, // 574/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "5", ElliottModelType.DOUBLE_ZIGZAG), 0.091019 }, // 25/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "5", ElliottModelType.ZIGZAG), 0.069175 }, // 19/618
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "1", ElliottModelType.SIMPLE_IMPULSE), 1.841667 }, // 51/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "1", ElliottModelType.ZIGZAG), 0.505556 }, // 14/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "1", ElliottModelType.IMPULSE), 0.469444 }, // 13/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "1", ElliottModelType.DOUBLE_ZIGZAG), 0.433333 }, // 12/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "2", ElliottModelType.SIMPLE_IMPULSE), 2.1 }, // 84/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "2", ElliottModelType.ZIGZAG), 0.1 }, // 4/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "2", ElliottModelType.DOUBLE_ZIGZAG), 0.05 }, // 2/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "3", ElliottModelType.SIMPLE_IMPULSE), 1.408333 }, // 39/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "3", ElliottModelType.ZIGZAG), 1.011111 }, // 28/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "3", ElliottModelType.IMPULSE), 0.433333 }, // 12/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "3", ElliottModelType.DOUBLE_ZIGZAG), 0.397222 }, // 11/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "4", ElliottModelType.SIMPLE_IMPULSE), 2.05 }, // 82/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "4", ElliottModelType.ZIGZAG), 0.125 }, // 5/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "4", ElliottModelType.DOUBLE_ZIGZAG), 0.075 }, // 3/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "5", ElliottModelType.SIMPLE_IMPULSE), 2.927778 }, // 62/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "5", ElliottModelType.DOUBLE_ZIGZAG), 0.613889 }, // 13/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "5", ElliottModelType.ZIGZAG), 0.425 }, // 9/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "5", ElliottModelType.IMPULSE), 0.236111 }, // 5/90
                { (ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "5", ElliottModelType.DIAGONAL_CONTRACTING_ENDING), 0.047222 }, // 1/90
                { (ElliottModelType.DOUBLE_ZIGZAG, "w", ElliottModelType.SIMPLE_IMPULSE), 0.825374 }, // 3975/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "w", ElliottModelType.ZIGZAG), 0.424626 }, // 2045/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "x", ElliottModelType.SIMPLE_IMPULSE), 3.513073 }, // 3467/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "x", ElliottModelType.FLAT_RUNNING), 1.301063 }, // 1284/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "x", ElliottModelType.DOUBLE_ZIGZAG), 0.336412 }, // 332/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "x", ElliottModelType.FLAT_EXTENDED), 0.286761 }, // 283/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "x", ElliottModelType.ZIGZAG), 0.277641 }, // 274/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "x", ElliottModelType.TRIANGLE_CONTRACTING), 0.208738 }, // 206/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "x", ElliottModelType.TRIANGLE_RUNNING), 0.122608 }, // 121/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "x", ElliottModelType.FLAT_REGULAR), 0.053704 }, // 53/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "y", ElliottModelType.SIMPLE_IMPULSE), 0.813953 }, // 3920/6020
                { (ElliottModelType.DOUBLE_ZIGZAG, "y", ElliottModelType.ZIGZAG), 0.436047 }, // 2100/6020
                { (ElliottModelType.FLAT_EXTENDED, "a", ElliottModelType.SIMPLE_IMPULSE), 2.123016 }, // 1605/1701
                { (ElliottModelType.FLAT_EXTENDED, "a", ElliottModelType.ZIGZAG), 0.07672 }, // 58/1701
                { (ElliottModelType.FLAT_EXTENDED, "a", ElliottModelType.DOUBLE_ZIGZAG), 0.050265 }, // 38/1701
                { (ElliottModelType.FLAT_EXTENDED, "b", ElliottModelType.SIMPLE_IMPULSE), 1.533069 }, // 1159/1701
                { (ElliottModelType.FLAT_EXTENDED, "b", ElliottModelType.ZIGZAG), 0.390212 }, // 295/1701
                { (ElliottModelType.FLAT_EXTENDED, "b", ElliottModelType.DOUBLE_ZIGZAG), 0.32672 }, // 247/1701
                { (ElliottModelType.FLAT_EXTENDED, "c", ElliottModelType.SIMPLE_IMPULSE), 1.854497 }, // 1402/1701
                { (ElliottModelType.FLAT_EXTENDED, "c", ElliottModelType.IMPULSE), 0.236772 }, // 179/1701
                { (ElliottModelType.FLAT_EXTENDED, "c", ElliottModelType.DIAGONAL_CONTRACTING_ENDING), 0.15873 }, // 120/1701
                { (ElliottModelType.FLAT_REGULAR, "a", ElliottModelType.SIMPLE_IMPULSE), 1.588132 }, // 907/1285
                { (ElliottModelType.FLAT_REGULAR, "a", ElliottModelType.ZIGZAG), 0.390467 }, // 223/1285
                { (ElliottModelType.FLAT_REGULAR, "a", ElliottModelType.DOUBLE_ZIGZAG), 0.271401 }, // 155/1285
                { (ElliottModelType.FLAT_REGULAR, "b", ElliottModelType.SIMPLE_IMPULSE), 1.891051 }, // 1080/1285
                { (ElliottModelType.FLAT_REGULAR, "b", ElliottModelType.ZIGZAG), 0.25214 }, // 144/1285
                { (ElliottModelType.FLAT_REGULAR, "b", ElliottModelType.DOUBLE_ZIGZAG), 0.106809 }, // 61/1285
                { (ElliottModelType.FLAT_REGULAR, "c", ElliottModelType.SIMPLE_IMPULSE), 2.162451 }, // 1235/1285
                { (ElliottModelType.FLAT_REGULAR, "c", ElliottModelType.IMPULSE), 0.050778 }, // 29/1285
                { (ElliottModelType.FLAT_REGULAR, "c", ElliottModelType.DIAGONAL_CONTRACTING_ENDING), 0.03677 }, // 21/1285
                { (ElliottModelType.FLAT_RUNNING, "a", ElliottModelType.SIMPLE_IMPULSE), 1.818537 }, // 6318/7817
                { (ElliottModelType.FLAT_RUNNING, "a", ElliottModelType.DOUBLE_ZIGZAG), 0.238327 }, // 828/7817
                { (ElliottModelType.FLAT_RUNNING, "a", ElliottModelType.ZIGZAG), 0.193137 }, // 671/7817
                { (ElliottModelType.FLAT_RUNNING, "b", ElliottModelType.SIMPLE_IMPULSE), 1.488966 }, // 5173/7817
                { (ElliottModelType.FLAT_RUNNING, "b", ElliottModelType.DOUBLE_ZIGZAG), 0.419662 }, // 1458/7817
                { (ElliottModelType.FLAT_RUNNING, "b", ElliottModelType.ZIGZAG), 0.341371 }, // 1186/7817
                { (ElliottModelType.FLAT_RUNNING, "c", ElliottModelType.SIMPLE_IMPULSE), 2.150985 }, // 7473/7817
                { (ElliottModelType.FLAT_RUNNING, "c", ElliottModelType.IMPULSE), 0.053537 }, // 186/7817
                { (ElliottModelType.FLAT_RUNNING, "c", ElliottModelType.DIAGONAL_CONTRACTING_ENDING), 0.045478 }, // 158/7817
                { (ElliottModelType.IMPULSE, "1", ElliottModelType.SIMPLE_IMPULSE), 1.208096 }, // 6351/6729
                { (ElliottModelType.IMPULSE, "1", ElliottModelType.IMPULSE), 0.069241 }, // 364/6729
                { (ElliottModelType.IMPULSE, "1", ElliottModelType.DIAGONAL_CONTRACTING_INITIAL), 0.002663 }, // 14/6729
                { (ElliottModelType.IMPULSE, "2", ElliottModelType.SIMPLE_IMPULSE), 1.956056 }, // 3061/6729
                { (ElliottModelType.IMPULSE, "2", ElliottModelType.FLAT_RUNNING), 1.488289 }, // 2329/6729
                { (ElliottModelType.IMPULSE, "2", ElliottModelType.FLAT_EXTENDED), 0.310566 }, // 486/6729
                { (ElliottModelType.IMPULSE, "2", ElliottModelType.ZIGZAG), 0.249859 }, // 391/6729
                { (ElliottModelType.IMPULSE, "2", ElliottModelType.DOUBLE_ZIGZAG), 0.162312 }, // 254/6729
                { (ElliottModelType.IMPULSE, "2", ElliottModelType.FLAT_REGULAR), 0.132917 }, // 208/6729
                { (ElliottModelType.IMPULSE, "3", ElliottModelType.SIMPLE_IMPULSE), 0.930859 }, // 5011/6729
                { (ElliottModelType.IMPULSE, "3", ElliottModelType.IMPULSE), 0.319141 }, // 1718/6729
                { (ElliottModelType.IMPULSE, "4", ElliottModelType.SIMPLE_IMPULSE), 2.5845 }, // 2851/6729
                { (ElliottModelType.IMPULSE, "4", ElliottModelType.FLAT_RUNNING), 1.386075 }, // 1529/6729
                { (ElliottModelType.IMPULSE, "4", ElliottModelType.FLAT_REGULAR), 0.817685 }, // 902/6729
                { (ElliottModelType.IMPULSE, "4", ElliottModelType.FLAT_EXTENDED), 0.374394 }, // 413/6729
                { (ElliottModelType.IMPULSE, "4", ElliottModelType.ZIGZAG), 0.292807 }, // 323/6729
                { (ElliottModelType.IMPULSE, "4", ElliottModelType.TRIANGLE_CONTRACTING), 0.287368 }, // 317/6729
                { (ElliottModelType.IMPULSE, "4", ElliottModelType.DOUBLE_ZIGZAG), 0.231164 }, // 255/6729
                { (ElliottModelType.IMPULSE, "4", ElliottModelType.TRIANGLE_RUNNING), 0.126007 }, // 139/6729
                { (ElliottModelType.IMPULSE, "5", ElliottModelType.SIMPLE_IMPULSE), 2.078132 }, // 6215/6729
                { (ElliottModelType.IMPULSE, "5", ElliottModelType.IMPULSE), 0.115025 }, // 344/6729
                { (ElliottModelType.IMPULSE, "5", ElliottModelType.DIAGONAL_CONTRACTING_ENDING), 0.056844 }, // 170/6729
                { (ElliottModelType.TRIANGLE_CONTRACTING, "a", ElliottModelType.SIMPLE_IMPULSE), 1.55042 }, // 492/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "a", ElliottModelType.DOUBLE_ZIGZAG), 0.434874 }, // 138/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "a", ElliottModelType.ZIGZAG), 0.264706 }, // 84/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "b", ElliottModelType.SIMPLE_IMPULSE), 1.755252 }, // 557/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "b", ElliottModelType.DOUBLE_ZIGZAG), 0.305672 }, // 97/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "b", ElliottModelType.ZIGZAG), 0.189076 }, // 60/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "c", ElliottModelType.SIMPLE_IMPULSE), 1.859244 }, // 590/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "c", ElliottModelType.DOUBLE_ZIGZAG), 0.201681 }, // 64/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "c", ElliottModelType.ZIGZAG), 0.189076 }, // 60/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "d", ElliottModelType.SIMPLE_IMPULSE), 2.00105 }, // 635/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "d", ElliottModelType.DOUBLE_ZIGZAG), 0.135504 }, // 43/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "d", ElliottModelType.ZIGZAG), 0.113445 }, // 36/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "e", ElliottModelType.SIMPLE_IMPULSE), 2.12395 }, // 674/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "e", ElliottModelType.DOUBLE_ZIGZAG), 0.069328 }, // 22/714
                { (ElliottModelType.TRIANGLE_CONTRACTING, "e", ElliottModelType.ZIGZAG), 0.056723 }, // 18/714
                { (ElliottModelType.TRIANGLE_RUNNING, "a", ElliottModelType.SIMPLE_IMPULSE), 1.781394 }, // 517/653
                { (ElliottModelType.TRIANGLE_RUNNING, "a", ElliottModelType.DOUBLE_ZIGZAG), 0.234303 }, // 68/653
                { (ElliottModelType.TRIANGLE_RUNNING, "a", ElliottModelType.ZIGZAG), 0.234303 }, // 68/653
                { (ElliottModelType.TRIANGLE_RUNNING, "b", ElliottModelType.SIMPLE_IMPULSE), 1.278331 }, // 371/653
                { (ElliottModelType.TRIANGLE_RUNNING, "b", ElliottModelType.DOUBLE_ZIGZAG), 0.520291 }, // 151/653
                { (ElliottModelType.TRIANGLE_RUNNING, "b", ElliottModelType.ZIGZAG), 0.451378 }, // 131/653
                { (ElliottModelType.TRIANGLE_RUNNING, "c", ElliottModelType.SIMPLE_IMPULSE), 1.498851 }, // 435/653
                { (ElliottModelType.TRIANGLE_RUNNING, "c", ElliottModelType.DOUBLE_ZIGZAG), 0.382466 }, // 111/653
                { (ElliottModelType.TRIANGLE_RUNNING, "c", ElliottModelType.ZIGZAG), 0.368683 }, // 107/653
                { (ElliottModelType.TRIANGLE_RUNNING, "d", ElliottModelType.SIMPLE_IMPULSE), 1.819296 }, // 528/653
                { (ElliottModelType.TRIANGLE_RUNNING, "d", ElliottModelType.ZIGZAG), 0.223966 }, // 65/653
                { (ElliottModelType.TRIANGLE_RUNNING, "d", ElliottModelType.DOUBLE_ZIGZAG), 0.206738 }, // 60/653
                { (ElliottModelType.TRIANGLE_RUNNING, "e", ElliottModelType.SIMPLE_IMPULSE), 1.946784 }, // 565/653
                { (ElliottModelType.TRIANGLE_RUNNING, "e", ElliottModelType.DOUBLE_ZIGZAG), 0.151608 }, // 44/653
                { (ElliottModelType.TRIANGLE_RUNNING, "e", ElliottModelType.ZIGZAG), 0.151608 }, // 44/653
                { (ElliottModelType.ZIGZAG, "a", ElliottModelType.SIMPLE_IMPULSE), 1.24456 }, // 10816/11124
                { (ElliottModelType.ZIGZAG, "a", ElliottModelType.IMPULSE), 0.03475 }, // 302/11124
                { (ElliottModelType.ZIGZAG, "a", ElliottModelType.DIAGONAL_CONTRACTING_INITIAL), 0.00069 }, // 6/11124
                { (ElliottModelType.ZIGZAG, "b", ElliottModelType.SIMPLE_IMPULSE), 3.604396 }, // 6573/11124
                { (ElliottModelType.ZIGZAG, "b", ElliottModelType.FLAT_RUNNING), 1.466873 }, // 2675/11124
                { (ElliottModelType.ZIGZAG, "b", ElliottModelType.FLAT_EXTENDED), 0.284601 }, // 519/11124
                { (ElliottModelType.ZIGZAG, "b", ElliottModelType.ZIGZAG), 0.227571 }, // 415/11124
                { (ElliottModelType.ZIGZAG, "b", ElliottModelType.TRIANGLE_RUNNING), 0.215507 }, // 393/11124
                { (ElliottModelType.ZIGZAG, "b", ElliottModelType.DOUBLE_ZIGZAG), 0.129414 }, // 236/11124
                { (ElliottModelType.ZIGZAG, "b", ElliottModelType.TRIANGLE_CONTRACTING), 0.104738 }, // 191/11124
                { (ElliottModelType.ZIGZAG, "b", ElliottModelType.FLAT_REGULAR), 0.0669 }, // 122/11124
                { (ElliottModelType.ZIGZAG, "c", ElliottModelType.SIMPLE_IMPULSE), 2.156553 }, // 10662/11124
                { (ElliottModelType.ZIGZAG, "c", ElliottModelType.IMPULSE), 0.063511 }, // 314/11124
                { (ElliottModelType.ZIGZAG, "c", ElliottModelType.DIAGONAL_CONTRACTING_ENDING), 0.029935 }, // 148/11124
            };

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
