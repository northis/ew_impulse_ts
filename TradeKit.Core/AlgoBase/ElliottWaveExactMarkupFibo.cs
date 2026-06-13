using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    public partial class ElliottWaveExactMarkup
    {
        /// <summary>
        /// Score multiplier applied to any IMPULSE candidate where wave 5 does not
        /// exceed wave 3's end price (truncated fifth).  A truncated impulse is a rare
        /// and structurally weak formation; this penalty ensures it always ranks below a
        /// comparable non-truncated impulse of the same Fibonacci quality.
        /// </summary>
        private const double TRUNCATION_SCORE_PENALTY = 0.3;

        // Fibonacci ratio maps — aligned with PatternGenerator's generation maps.
        // Format: (density-weight-byte, ratio) — weight=0 is sentinel.
        // Weights are selection-probability densities (key / sum-of-keys gives the
        // normalised probability for that Fibonacci level, per EW_RULES §4).
        // No duplicate 0.786: second entry merged into the first (25+35=60 kept as 60)
        private static readonly (byte weight, double ratio)[] ZIGZAG_C_TO_A =
        {
            (0, 0), (5, 0.618), (60, 0.786), (75, 1.0), (85, 1.618), (90, 2.618), (95, 3.618)
        };

        // TRIPLE_ZIGZAG Z/W ratio (EW_RULES §4.7 / PatternGenerator ZIGZAG_X_Z_TO_W).
        private static readonly (byte weight, double ratio)[] ZIGZAG_X_Z_TO_W =
        {
            (0, 0), (5, 1.0), (25, 1.618), (50, 2.618), (80, 3.618), (90, 4.236)
        };

        private static readonly (byte weight, double ratio)[] CONTRACTING_DIAGONAL_3_TO_1 =
        {
            (0, 0), (5, 0.5), (15, 0.618), (20, 0.786)
        };

        // Diagonal corrections (W2/W1 and W4/W3): distribution peaks at 0.66
        // per EW_RULES §7: "Wave 2 retracement of Wave 1: min 50%, max 75%, mean 66%"
        private static readonly (byte weight, double ratio)[] MAP_DIAGONAL_CORRECTION =
        {
            (0, 0), (5, 0.382), (10, 0.5), (50, 0.618), (99, 0.66)
        };

        // W5/W3 in contracting diagonal: typical value ~0.677 (derived from generation logic)
        // Range [0.35, ~1.0), generated as (restTree + W4Len) / W3Len
        private static readonly (byte weight, double ratio)[] MAP_DIAGONAL_5_TO_3 =
        {
            (0, 0), (5, 0.35), (15, 0.5), (35, 0.618), (80, 0.677), (95, 0.786), (99, 0.9)
        };

        // Peak at 1.618 per Prechter (80th percentile); 3.618/4.236 are rare
        private static readonly (byte weight, double ratio)[] IMPULSE_3_TO_1 =
        {
            (0, 0), (5, 0.618), (10, 0.786), (15, 1.0),
            (80, 1.618), (90, 2.618), (97, 3.618), (99, 4.236)
        };

        // When wave 3 is extended, waves 1 and 5 tend to equality (1.0 = 70th percentile)
        private static readonly (byte weight, double ratio)[] IMPULSE_5_TO_1 =
        {
            (0, 0), (5, 0.382), (15, 0.618), (25, 0.786),
            (70, 1.0), (90, 1.618), (97, 2.618), (99, 3.618)
        };

        // Deep corrections peak at 0.786 (85th percentile)
        private static readonly (byte weight, double ratio)[] MAP_DEEP_CORRECTION =
        {
            (0, 0), (5, 0.5), (55, 0.618), (85, 0.786), (99, 0.95)
        };

        private static readonly (byte weight, double ratio)[] MAP_SHALLOW_CORRECTION =
        {
            (0, 0), (5, 0.236), (35, 0.382), (85, 0.5)
        };

        // Per Prechter: C = 1.618 × A is the most typical extended-flat ratio.
        // Aligned with PatternGenerator MAP_EX_FLAT_WAVE_C_TO_A (EW_RULES §4.8).
        private static readonly (byte weight, double ratio)[] MAP_EX_FLAT_WAVE_C_TO_A =
        {
            (0, 0), (75, 1.618), (90, 2.618), (99, 3.618)
        };

        private static readonly (byte weight, double ratio)[] MAP_RUNNING_FLAT_WAVE_C_TO_A =
        {
            (0, 0), (5, 0.5), (20, 0.618), (80, 1.0), (90, 1.272), (95, 1.618)
        };

        // Regular flat: C ≈ A, peak at 1.0; 1.272 and 1.618 are uncommon extensions.
        private static readonly (byte weight, double ratio)[] MAP_REG_FLAT_WAVE_C_TO_A =
        {
            (0, 0), (70, 1.0), (90, 1.272), (99, 1.618)
        };

        // FLAT B/A maps — wave B overshoots the pattern origin, so B/A > 1.0.
        // Extended / running flat: B typically 100–138.2 % of A (Prechter).
        private static readonly (byte weight, double ratio)[] MAP_FLAT_EXTENDED_B_TO_A =
        {
            (0, 0), (5, 0.786), (15, 1.0), (50, 1.236), (85, 1.382), (95, 1.618)
        };

        // Running flat B/A: same overshoot range as extended.
        private static readonly (byte weight, double ratio)[] MAP_FLAT_RUNNING_B_TO_A =
        {
            (0, 0), (5, 0.786), (15, 1.0), (50, 1.236), (85, 1.382), (95, 1.618)
        };

        // Regular flat: B ≈ 90–100 % of A (EW_RULES §13).  B does NOT overshoot A's
        // origin and stays strictly within A's amplitude.
        private static readonly (byte weight, double ratio)[] MAP_FLAT_REGULAR_B_TO_A =
        {
            (0, 0), (70, 0.9), (90, 0.95), (99, 1.0)
        };

        private static readonly (byte weight, double ratio)[] MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV =
        {
            (0, 0), (5, 0.5), (20, 0.618), (80, 0.786), (90, 0.9), (95, 0.95)
        };

        // W4 retracement of W3 in an IMPULSE.
        // Unlike W2 (always a simple correction), W4 may be a triangle, which produces
        // a very shallow *price* retracement (often 0.118–0.236 of W3) while consuming
        // significant time.  The map therefore explicitly includes small ratios so that
        // triangle-shaped Wave 4s are scored fairly alongside zigzag/flat Wave 4s.
        private static readonly (byte weight, double ratio)[] IMPULSE_4_TO_3 =
        {
            (0, 0), (5, 0.118), (50, 0.172), (60, 0.236), (75, 0.382), (90, 0.5), (95, 0.618)
        };

        /// <summary>
        /// Returns a score [0,1] based on how well the given ratio matches the fibo map.
        /// Uses exponential penalty: score = weight * exp(-k * |ratio - nearest| / nearest).
        /// Normalizes by the maximum weight in the map so every map's best-fit scores 1.0.
        /// </summary>
        private static double GetFiboWeight((byte weight, double ratio)[] map, double ratio)
        {
            // Find best-fit entry (nearest ratio in map)
            double bestRaw = 0.0;
            double maxWeight = 0.0;
            const double k = 5.0;

            for (int i = 1; i < map.Length; i++) // skip sentinel at 0
            {
                double r = map[i].ratio;
                double w = map[i].weight / 100.0;
                if (r <= 0) continue;
                if (w > maxWeight) maxWeight = w;
                double dist = Math.Abs(ratio - r) / r;
                double s = w * Math.Exp(-k * dist);
                if (s > bestRaw) bestRaw = s;
            }

            if (maxWeight <= 0) return 0.001;
            // Normalize so that a perfect match at the highest-weight entry scores ~1.0
            return Math.Max(0.001, bestRaw / maxWeight);
        }

        private static double GetCorrectionFiboWeight(double ratio)
        {
            double deep = GetFiboWeight(MAP_DEEP_CORRECTION, ratio);
            double shallow = GetFiboWeight(MAP_SHALLOW_CORRECTION, ratio);
            return Math.Max(deep, shallow);
        }

        /// <summary>
        /// Calculates a combined Fibonacci score for a wave array under the given model.
        /// Uses geometric mean over the Fibo ratio scores so that models with more
        /// ratios to check (e.g. IMPULSE with 4) are on equal footing with simpler models
        /// (e.g. ZIGZAG with 2). Returns a positive value; higher is better.
        /// </summary>
        private static double CalculateFiboScore(ElliottModelType model, Segment[] w)
        {
            (double product, int numRatios) = CollectFiboRatios(model, w, includeDuration: true);
            if (numRatios == 0)
                return 0;

            double modelCoeff = GetDetectionModelCoeff(model);

            // Geometric mean: normalize by number of ratios so a model with more ratio checks
            // doesn't intrinsically score lower than a simpler model.
            double geometricMean = Math.Pow(product, 1.0 / numRatios);

            // Complexity bonus: a model that satisfies more independent Fibo relationships is
            // more constrained (more specific) and should be preferred when the fit is equal.
            double complexityBonus = 1.0 + Math.Log2(numRatios + 1) * 0.15;

            // Bar-count bonus for corrective waves.
            double barCountBonus = CalculateCorrectiveBarCountBonus(model, w);

            double score = modelCoeff * geometricMean * complexityBonus * barCountBonus;

            // Truncation penalty: IMPULSE where wave 5 does not exceed wave 3's end.
            if (model == ElliottModelType.IMPULSE && w.Length >= 5)
            {
                bool isUpImp = w[0].IsUp;
                bool truncated = isUpImp
                    ? w[4].End.Value < w[2].End.Value
                    : w[4].End.Value > w[2].End.Value;
                if (truncated)
                    score *= TRUNCATION_SCORE_PENALTY;
            }

            return score;
        }

        /// <summary>
        /// Detection-scoring model coefficient. Mirrors the inline overrides v1 used
        /// for diagonals (1.3) and triangles (2.0) so they compete fairly against the
        /// several 3-wave interpretations that occupy the same positions; all other
        /// models use their <see cref="ModelRules.ProbabilityCoefficient"/>.
        /// </summary>
        private static double GetDetectionModelCoeff(ElliottModelType model)
        {
            switch (model)
            {
                case ElliottModelType.DIAGONAL_CONTRACTING_INITIAL:
                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                    return 1.3;
                case ElliottModelType.TRIANGLE_CONTRACTING:
                case ElliottModelType.TRIANGLE_RUNNING:
                    return 2.0;
                default:
                    return GetModelCoeff(model);
            }
        }

        /// <summary>
        /// Pure Fibonacci geometric-mean score for v2 (EW_MARKUP_v2 §16.1). Reuses the
        /// exact v1 ratio maps and <see cref="GetFiboWeight"/> but returns ONLY the
        /// geometric mean of the price-ratio weights — without v1's model coefficient,
        /// complexity/bar-count bonuses, truncation or duration penalties (those are
        /// applied explicitly by the v2 scorer as the <c>P(model|position)</c> factor
        /// and the soft penalties). Returns 1.0 for models that carry no Fibo ratios
        /// (e.g. <see cref="ElliottModelType.SIMPLE_IMPULSE"/>) and a small floor for
        /// invalid geometry.
        /// </summary>
        public static double CalculatePureFiboScore(
            ElliottModelType model, IReadOnlyList<ElliottWaveExactMarkupV2.Segment> waves)
        {
            var w = new Segment[waves.Count];
            for (int i = 0; i < waves.Count; i++)
                w[i] = new Segment { Start = waves[i].Start, End = waves[i].End };

            (double product, int numRatios) = CollectFiboRatios(model, w, includeDuration: false);
            if (numRatios == 0)
                return product <= 0.0 ? 0.001 : 1.0;

            return Math.Pow(product, 1.0 / numRatios);
        }

        /// <summary>
        /// Collects the per-model Fibonacci ratio weights into a running product and a
        /// ratio counter (shared by the v1 <see cref="CalculateFiboScore"/> and the v2
        /// <see cref="CalculatePureFiboScore"/>). When <paramref name="includeDuration"/>
        /// is set, the §20.1 W2/W4 duration penalty is folded into the product (v1
        /// behaviour). Returns <c>product = 0</c> with <c>numRatios = 0</c> to signal
        /// invalid geometry (a zero-length leading wave).
        /// </summary>
        private static (double product, int numRatios) CollectFiboRatios(
            ElliottModelType model, Segment[] w, bool includeDuration)
        {
            double product = 1.0;
            int numRatios = 0;

            switch (model)
            {
                case ElliottModelType.IMPULSE:
                {
                    double len1 = w[0].Length;
                    double len2 = w[1].Length;
                    double len3 = w[2].Length;
                    double len4 = w[3].Length;
                    double len5 = w[4].Length;

                    if (len1 <= 0) return (0.0, 0);
                    product *= GetCorrectionFiboWeight(len2 / len1); numRatios++;
                    product *= GetFiboWeight(IMPULSE_3_TO_1, len3 / len1); numRatios++;
                    if (len3 > 0) { product *= GetFiboWeight(IMPULSE_4_TO_3, len4 / len3); numRatios++; }
                    product *= GetFiboWeight(IMPULSE_5_TO_1, len5 / len1); numRatios++;

                    // EW_MARKUP §20.1: penalise W2/W4 duration imbalance
                    if (includeDuration)
                    {
                        double durPenImp = CalculateDurationPenalty(w);
                        if (durPenImp < 1.0) { product *= durPenImp; numRatios++; }
                    }
                    break;
                }

                case ElliottModelType.DIAGONAL_CONTRACTING_INITIAL:
                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                {
                    // INITIAL and ENDING are structurally identical at the 5-wave price level
                    // (both generated by the same GetDiagonal logic). In v1 they cannot be
                    // distinguished without sub-wave analysis. Both use modelCoeff=1.0 so the
                    // TargetModels list order (INITIAL before ENDING) acts as a tie-breaker.
                    // The probability coefficients from EW_RULES.md (0.03 / 1.0) are generation
                    // weights only and are not used for detection scoring (see GetDetectionModelCoeff).
                    double len1 = w[0].Length;
                    double len2 = w[1].Length;
                    double len3 = w[2].Length;
                    double len4 = w[3].Length;
                    double len5 = w[4].Length;

                    if (len1 <= 0) return (0.0, 0);
                    // W2/W1: diagonal corrections peak at 0.66 (not 0.786 like impulse)
                    product *= GetFiboWeight(MAP_DIAGONAL_CORRECTION, len2 / len1); numRatios++;
                    // W3/W1: contracting diagonal, same map applies (peaks at 0.786)
                    product *= GetFiboWeight(CONTRACTING_DIAGONAL_3_TO_1, len3 / len1); numRatios++;
                    if (len3 > 0)
                    {
                        // W4/W3: diagonal correction, peaks at 0.66
                        product *= GetFiboWeight(MAP_DIAGONAL_CORRECTION, len4 / len3); numRatios++;
                        // W5/W3: contracting, typical ~0.677, dedicated map
                        product *= GetFiboWeight(MAP_DIAGONAL_5_TO_3, len5 / len3); numRatios++;
                    }

                    // EW_MARKUP §20.4: same W2/W4 duration proportionality as impulse
                    if (includeDuration)
                    {
                        double durPenDiag = CalculateDurationPenalty(w);
                        if (durPenDiag < 1.0) { product *= durPenDiag; numRatios++; }
                    }
                    break;
                }

                case ElliottModelType.ZIGZAG:
                {
                    double lenA = w[0].Length;
                    double lenB = w[1].Length;
                    double lenC = w[2].Length;

                    if (lenA <= 0) return (0.0, 0);
                    product *= GetCorrectionFiboWeight(lenB / lenA); numRatios++;
                    product *= GetFiboWeight(ZIGZAG_C_TO_A, lenC / lenA); numRatios++;
                    break;
                }

                case ElliottModelType.DOUBLE_ZIGZAG:
                {
                    double lenW = w[0].Length;
                    double lenX = w[1].Length;
                    double lenY = w[2].Length;

                    if (lenW <= 0) return (0.0, 0);
                    product *= GetCorrectionFiboWeight(lenX / lenW); numRatios++;
                    product *= GetFiboWeight(ZIGZAG_C_TO_A, lenY / lenW); numRatios++;
                    break;
                }

                case ElliottModelType.TRIPLE_ZIGZAG:
                {
                    // EW_RULES §11: 5-wave structure W·X·Y·XX·Z.
                    // Z/W uses ZIGZAG_X_Z_TO_W (1.0–4.236); intermediate retracements
                    // (X/W, XX/Y) use correction fibo; Y/W uses ZIGZAG_C_TO_A.
                    double lenW = w[0].Length;
                    double lenX = w[1].Length;
                    double lenY = w[2].Length;
                    double lenXx = w[3].Length;
                    double lenZ = w[4].Length;

                    if (lenW <= 0) return (0.0, 0);
                    product *= GetCorrectionFiboWeight(lenX / lenW);   numRatios++;
                    product *= GetFiboWeight(ZIGZAG_C_TO_A, lenY / lenW); numRatios++;
                    if (lenY > 0)
                    {
                        product *= GetCorrectionFiboWeight(lenXx / lenY); numRatios++;
                        product *= GetFiboWeight(ZIGZAG_X_Z_TO_W, lenZ / lenW); numRatios++;
                    }
                    break;
                }

                case ElliottModelType.FLAT_EXTENDED:
                {
                    double lenA = w[0].Length;
                    double lenB = w[1].Length;
                    double lenC = w[2].Length;

                    if (lenA <= 0) return (0.0, 0);
                    product *= GetFiboWeight(MAP_FLAT_EXTENDED_B_TO_A, lenB / lenA); numRatios++;
                    product *= GetFiboWeight(MAP_EX_FLAT_WAVE_C_TO_A, lenC / lenA); numRatios++;
                    break;
                }

                case ElliottModelType.FLAT_RUNNING:
                {
                    double lenA = w[0].Length;
                    double lenB = w[1].Length;
                    double lenC = w[2].Length;

                    if (lenA <= 0) return (0.0, 0);
                    product *= GetFiboWeight(MAP_FLAT_RUNNING_B_TO_A, lenB / lenA); numRatios++;
                    product *= GetFiboWeight(MAP_RUNNING_FLAT_WAVE_C_TO_A, lenC / lenA); numRatios++;
                    break;
                }

                case ElliottModelType.FLAT_REGULAR:
                {
                    double lenA = w[0].Length;
                    double lenB = w[1].Length;
                    double lenC = w[2].Length;

                    if (lenA <= 0) return (0.0, 0);
                    product *= GetFiboWeight(MAP_FLAT_REGULAR_B_TO_A, lenB / lenA); numRatios++;
                    product *= GetFiboWeight(MAP_REG_FLAT_WAVE_C_TO_A, lenC / lenA); numRatios++;
                    break;
                }

                case ElliottModelType.TRIANGLE_CONTRACTING:
                case ElliottModelType.TRIANGLE_RUNNING:
                {
                    // Running triangle probability coefficient (0.3) is a generation weight only.
                    // For detection scoring both triangle types should compete equally.
                    // Set to 2.0 so that a 5-wave triangle can outrank the five distinct
                    // 3-wave interpretations (ZIGZAG, FLAT_RUNNING, FLAT_EXTENDED, etc.)
                    // that fit the first 3 triangle legs and occupy positions 0-4
                    // (see GetDetectionModelCoeff).
                    for (int i = 1; i < w.Length; i++)
                    {
                        double prev = w[i - 1].Length;
                        double curr = w[i].Length;
                        if (prev <= 0) continue;
                        product *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, curr / prev);
                        numRatios++;
                    }
                    break;
                }
            }

            return (product, numRatios);
        }

        private static double GetModelCoeff(ElliottModelType model)
        {
            if (ElliottWavePatternHelper.ModelRules.TryGetValue(model, out ModelRules rules))
                return rules.ProbabilityCoefficient;
            return 0.25;
        }

        /// <summary>
        /// Returns the wave indices (0-based) that represent corrective sub-waves for
        /// the given model.  These are the positions where a longer bar count is preferred
        /// — e.g. wave B in a zigzag, waves 2 and 4 in an impulse, wave X in a double-zigzag.
        /// </summary>
        private static int[] GetCorrectiveWaveIndices(ElliottModelType model) => model switch
        {
            ElliottModelType.IMPULSE
                or ElliottModelType.DIAGONAL_CONTRACTING_INITIAL
                or ElliottModelType.DIAGONAL_CONTRACTING_ENDING => new[] { 1, 3 },
            ElliottModelType.ZIGZAG
                or ElliottModelType.FLAT_EXTENDED
                or ElliottModelType.FLAT_RUNNING
                or ElliottModelType.FLAT_REGULAR => new[] { 1 },
            ElliottModelType.DOUBLE_ZIGZAG => new[] { 1 },
            ElliottModelType.TRIANGLE_CONTRACTING
                or ElliottModelType.TRIANGLE_RUNNING => new[] { 1, 2, 3, 4 },
            _ => Array.Empty<int>()
        };

        /// <summary>
        /// Computes a score multiplier ≥ 1.0 that rewards candidates where corrective
        /// sub-waves occupy a larger fraction of the total pattern's bar span.
        /// Range: [1.0, 1.3].  Kept small so it acts as a tie-breaker only and does
        /// not override Fibonacci quality or hard-rule decisions.
        /// </summary>
        private static double CalculateCorrectiveBarCountBonus(
            ElliottModelType model, Segment[] w)
        {
            int[] indices = GetCorrectiveWaveIndices(model);
            if (indices.Length == 0) return 1.0;

            int totalBars = Math.Max(1,
                w[w.Length - 1].End.BarIndex - w[0].Start.BarIndex);

            double sumFraction = 0.0;
            int count = 0;

            foreach (int idx in indices)
            {
                if (idx >= w.Length) continue;
                int wBars = Math.Max(1, w[idx].End.BarIndex - w[idx].Start.BarIndex);
                sumFraction += (double)wBars / totalBars;
                count++;
            }

            if (count == 0) return 1.0;

            double avgFraction = sumFraction / count;
            // Bonus range: 1.0 (corrective wave 0 % of time) → 1.3 (100 % of time)
            return 1.0 + 0.3 * avgFraction;
        }

        /// <summary>
        /// Computes a score penalty [0.1, 1.0] for impulse/diagonal candidates where
        /// the corrective waves (W2 and W4) are disproportionately different in
        /// bar duration.  Based on EW_MARKUP §20.1 and §20.4.
        /// <list type="bullet">
        /// <item>W4 ≤ 1.5 × W2 bars → soft limit</item>
        /// <item>W2 ≤ 1.1 × W4 bars → soft limit</item>
        /// </list>
        /// Penalty formula: <c>Clamp(1 − (imbalanceRatio − 1.0) × 0.5, 0.1, 1.0)</c>
        /// applied per violated limit, then multiplied together.
        /// </summary>
        private static double CalculateDurationPenalty(Segment[] w)
        {
            if (w.Length < 4) return 1.0;

            int w2Bars = Math.Abs(w[1].End.BarIndex - w[1].Start.BarIndex);
            int w4Bars = Math.Abs(w[3].End.BarIndex - w[3].Start.BarIndex);
            if (w2Bars <= 0 || w4Bars <= 0) return 1.0;

            double penalty = 1.0;

            // W4 ≤ 1.5 × W2
            const double limit4over2 = 1.5;
            double ratio4over2 = (double)w4Bars / w2Bars;
            if (ratio4over2 > limit4over2)
            {
                double imb = ratio4over2 / limit4over2;
                penalty *= Math.Max(0.1, 1.0 - (imb - 1.0) * 0.5);
            }

            // W2 ≤ 1.1 × W4
            const double limit2over4 = 1.1;
            double ratio2over4 = (double)w2Bars / w4Bars;
            if (ratio2over4 > limit2over4)
            {
                double imb = ratio2over4 / limit2over4;
                penalty *= Math.Max(0.1, 1.0 - (imb - 1.0) * 0.5);
            }

            return penalty;
        }
    }
}
