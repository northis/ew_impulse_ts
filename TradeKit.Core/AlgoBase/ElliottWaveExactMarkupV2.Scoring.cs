using System;
using System.Collections.Generic;
using TradeKit.Core.Common;
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
    /// <item><c>softPenalties</c> — overshoot and time-window factors in
    /// (0,1] that demote (but never kill) structurally weak fits (§16.1). Truncation
    /// is excluded here: W5 failing to exceed W3 is a structural violation that kills
    /// the node via hard-price rule (§7.1).</item>
    /// </list>
    /// </summary>
    public partial class ElliottWaveExactMarkupV2
    {
        // ───────────────────────── §16.4 calibrated thresholds ─────────────────────────

        /// <summary>Smallest factor the time-window soft penalty may reach.</summary>
        private const double TIME_PENALTY_FLOOR = 0.6;

        /// <summary>Smallest factor the trendline overshoot penalty may reach.</summary>
        private const double TRENDLINE_PENALTY_FLOOR = 0.7;

        /// <summary>Retracement ratio below which no overshoot penalty applies.</summary>
        private const double OVERSHOOT_COMFORT = 0.9;

        /// <summary>Retracement ratio at which the overshoot penalty saturates.</summary>
        private const double OVERSHOOT_LIMIT = 1.3;

        /// <summary>Smallest factor the overshoot soft penalty may reach.</summary>
        private const double OVERSHOOT_FLOOR = 0.7;

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
        /// Product of the §16.1 soft penalties — overshoot and time-window.
        /// Each is in (0,1]; together they demote weak fits without killing them.
        /// Truncation is no longer a soft penalty; it is a hard-price rule (§7.1).
        /// </summary>
        private static double SoftPenalties(ElliottModelType model, IReadOnlyList<Segment> waves)
        {
            return OvershootPenalty(model, waves)
                   * TimeWindowPenalty(model, waves)
                   * TrendlinePenalty(model, waves);
        }

        /// <summary>
        /// §8.5 trendline-geometry penalty for <b>completed</b> 5-wave triangles
        /// and diagonals.  Builds the two converging trendlines from same-direction
        /// endpoints (A–C / B–D for triangles, 1–3 / 2–4 for diagonals) and checks
        /// that the final wave's end lies within the corridor.  Overshoot draws a
        /// soft penalty proportional to the channel width.
        /// </summary>
        private static double TrendlinePenalty(
            ElliottModelType model, IReadOnlyList<Segment> waves)
        {
            if (waves.Count < 5)
                return 1.0;

            bool isTriangle = model == ElliottModelType.TRIANGLE_CONTRACTING
                           || model == ElliottModelType.TRIANGLE_RUNNING;
            bool isDiagonal = model == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL
                           || model == ElliottModelType.DIAGONAL_CONTRACTING_ENDING;

            if (!isTriangle && !isDiagonal)
                return 1.0;

            Segment w0 = waves[0], w1 = waves[1], w2 = waves[2],
                     w3 = waves[3], w4 = waves[4];

            // Line 1: endpoints of waves 0 & 2 (A–C / 1–3)
            double price1 = Extrapolate(w0.End, w2.End, w4.End.BarIndex);
            // Line 2: endpoints of waves 1 & 3 (B–D / 2–4)
            double price2 = Extrapolate(w1.End, w3.End, w4.End.BarIndex);

            double upper = Math.Max(price1, price2);
            double lower = Math.Min(price1, price2);
            double channelWidth = upper - lower;

            if (channelWidth <= 0)
                return 1.0; // degenerate — parallel or diverging at target bar

            double finalPrice = w4.End.Value;

            if (finalPrice >= lower && finalPrice <= upper)
                return 1.0; // within corridor

            // Overshoot relative to channel width, capped at 1× width.
            double overshoot = finalPrice < lower
                ? (lower - finalPrice) / channelWidth
                : (finalPrice - upper) / channelWidth;
            overshoot = Math.Min(overshoot, 1.0);

            return TRENDLINE_PENALTY_FLOOR
                   + (1.0 - TRENDLINE_PENALTY_FLOOR) * (1.0 - overshoot);
        }

        /// <summary>
        /// Linear extrapolation: returns Y at <paramref name="xTarget"/> on the
        /// line through two <see cref="BarPoint"/>s.
        /// </summary>
        private static double Extrapolate(BarPoint p1, BarPoint p2, int xTarget)
        {
            if (p2.BarIndex == p1.BarIndex)
                return p2.Value;
            double slope = (p2.Value - p1.Value) / (p2.BarIndex - p1.BarIndex);
            return p2.Value + slope * (xTarget - p2.BarIndex);
        }

        /// <summary>
        /// Geometric-mean penalty over the corrective (counter-trend) waves, demoting
        /// retracements that approach the structural cancellation boundary (§16.1 overshoot).
        /// <para>
        /// Flat models (<see cref="ElliottModelType.FLAT_EXTENDED"/>,
        /// <see cref="ElliottModelType.FLAT_RUNNING"/>, <see cref="ElliottModelType.FLAT_REGULAR"/>)
        /// and <see cref="ElliottModelType.TRIANGLE_RUNNING"/> expect wave B to retrace >100 %
        /// of wave A by definition, so position 1 is exempt from the overshoot penalty.
        /// </para>
        /// </summary>
        private static double OvershootPenalty(ElliottModelType model, IReadOnlyList<Segment> waves)
        {
            if (waves.Count < 2)
                return 1.0;

            bool trendUp = waves[0].IsUp;
            double product = 1.0;
            int count = 0;

            // Models where wave B is expected to exceed wave A by definition:
            // flat extensions/running/regular and running triangles.
            bool skipB = model == ElliottModelType.FLAT_EXTENDED
                      || model == ElliottModelType.FLAT_RUNNING
                      || model == ElliottModelType.FLAT_REGULAR
                      || model == ElliottModelType.TRIANGLE_RUNNING;

            for (int i = 1; i < waves.Count; i++)
            {
                if (waves[i].IsUp == trendUp)
                    continue; // not a counter-trend (corrective) wave

                // Wave B of a flat / running triangle is expected to overshoot
                // the origin — a deep retracement is the rule, not a warning sign.
                if (skipB && i == 1)
                    continue;

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
        /// §8.4 directional time penalty: corrections are statistically longer than
        /// impulses, so we only penalise when the <b>impulse</b> is longer than the
        /// following <b>correction</b> (ratio correction/impulse &lt; 1).
        /// The reverse — correction 2–3× longer than impulse — is healthy and
        /// draws <b>no</b> penalty.
        /// <para>
        /// §8.5: diagonals are exempt (geometry is primary).
        /// </para>
        /// </summary>
        private static double TimeWindowPenalty(
            ElliottModelType model, IReadOnlyList<Segment> waves)
        {
            // §8.5: diagonals get no time penalty — trendline geometry is primary.
            if (model == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL ||
                model == ElliottModelType.DIAGONAL_CONTRACTING_ENDING)
                return 1.0;

            var pairs = GetImpulseCorrectionPairs(model);
            if (pairs == null || pairs.Count == 0)
                return 1.0;

            double product = 1.0;
            int count = 0;

            foreach ((int impIdx, int corIdx) in pairs)
            {
                if (corIdx >= waves.Count)
                    continue;

                int impBars = BarSpan(waves[impIdx]);
                int corBars = BarSpan(waves[corIdx]);
                if (impBars <= 0 || corBars <= 0)
                    continue;

                // §8.4: correction ≥ impulse is healthy (ratio ≥ 1).
                // Penalise only when impulse > correction (ratio < 1).
                double ratio = (double)corBars / impBars;
                product *= CorrectionTimeFactor(ratio);
                count++;
            }

            return count == 0 ? 1.0 : Math.Pow(product, 1.0 / count);
        }

        /// <summary>
        /// Returns the (impulse, correction) index pairs for the given model
        /// (§8.4).  Flats and triangles have no impulse sub-waves so the rule
        /// does not apply.
        /// </summary>
        private static List<(int Impulse, int Correction)>? GetImpulseCorrectionPairs(
            ElliottModelType model)
        {
            switch (model)
            {
                case ElliottModelType.IMPULSE:
                    // W1→W2 (0,1), W3→W4 (2,3)
                    return new List<(int, int)> { (0, 1), (2, 3) };

                case ElliottModelType.ZIGZAG:
                    // A→B (0,1)
                    return new List<(int, int)> { (0, 1) };

                case ElliottModelType.DOUBLE_ZIGZAG:
                    // W→X (0,1)
                    return new List<(int, int)> { (0, 1) };

                case ElliottModelType.TRIPLE_ZIGZAG:
                    // W→X (0,1), Y→XX (2,3)
                    return new List<(int, int)> { (0, 1), (2, 3) };

                default:
                    // Flats, triangles — all sub-waves are corrective.
                    return null;
            }
        }

        /// <summary>
        /// Maps correction/impulse duration ratio to a
        /// [<see cref="TIME_PENALTY_FLOOR"/>, 1] factor.  1.0 when correction ≥
        /// impulse (ratio ≥ 1, healthy per §8.4); ramps linearly down to
        /// <see cref="TIME_PENALTY_FLOOR"/> as the ratio approaches
        /// <see cref="ElliottWaveExactMarkupV2.TIME_WINDOW_K_MIN"/> (0.3).
        /// </summary>
        private static double CorrectionTimeFactor(double ratio)
        {
            // ratio = correction / impulse.  ≥1 → healthy, no penalty.
            if (ratio >= 1.0)
                return 1.0;

            // ratio in (k_min, 1).  Linear ramp: 1.0 at ratio=1, FLOOR at ratio=k_min.
            double span = 1.0 - TIME_WINDOW_K_MIN;
            double t = span <= 0 ? 0.0 : (ratio - TIME_WINDOW_K_MIN) / span;
            t = Math.Clamp(t, 0.0, 1.0);
            return TIME_PENALTY_FLOOR + (1.0 - TIME_PENALTY_FLOOR) * t;
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
