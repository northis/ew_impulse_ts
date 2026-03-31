using System;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    public partial class ElliottWaveExactMarkup
    {
        // Fibonacci ratio maps — aligned with PatternGenerator's generation maps
        // Format: (cumulative-weight-byte, ratio)  — weight=0 is sentinel
        // Weights are cumulative percentiles: (w, r) means w% of samples have ratio ≤ r.
        // No duplicate 0.786: second entry merged into the first (25+35=60 kept as 60)
        private static readonly (byte weight, double ratio)[] ZIGZAG_C_TO_A =
        {
            (0, 0), (5, 0.618), (60, 0.786), (75, 1.0), (85, 1.618), (90, 2.618), (95, 3.618)
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

        private static readonly (byte weight, double ratio)[] MAP_EX_FLAT_WAVE_C_TO_A =
        {
            (0, 0), (20, 1.618), (80, 2.618), (95, 3.618)
        };

        private static readonly (byte weight, double ratio)[] MAP_RUNNING_FLAT_WAVE_C_TO_A =
        {
            (0, 0), (5, 0.5), (20, 0.618), (80, 1.0), (90, 1.272), (95, 1.618)
        };

        private static readonly (byte weight, double ratio)[] MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV =
        {
            (0, 0), (5, 0.5), (20, 0.618), (80, 0.786), (90, 0.9), (95, 0.95)
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
            double modelCoeff = GetModelCoeff(model);
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

                    if (len1 <= 0) return 0;
                    product *= GetCorrectionFiboWeight(len2 / len1); numRatios++;
                    product *= GetFiboWeight(IMPULSE_3_TO_1, len3 / len1); numRatios++;
                    if (len3 > 0) { product *= GetCorrectionFiboWeight(len4 / len3); numRatios++; }
                    product *= GetFiboWeight(IMPULSE_5_TO_1, len5 / len1); numRatios++;
                    break;
                }

                case ElliottModelType.DIAGONAL_CONTRACTING_INITIAL:
                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                {
                    // Both types score equally from Fibo ratios; INITIAL vs ENDING is
                    // discriminated by sub-wave structure (see TryCombinations sub-wave count).
                    modelCoeff = 1.5;

                    double len1 = w[0].Length;
                    double len2 = w[1].Length;
                    double len3 = w[2].Length;
                    double len4 = w[3].Length;
                    double len5 = w[4].Length;

                    if (len1 <= 0) return 0;
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
                    break;
                }

                case ElliottModelType.ZIGZAG:
                {
                    double lenA = w[0].Length;
                    double lenB = w[1].Length;
                    double lenC = w[2].Length;

                    if (lenA <= 0) return 0;
                    product *= GetCorrectionFiboWeight(lenB / lenA); numRatios++;
                    product *= GetFiboWeight(ZIGZAG_C_TO_A, lenC / lenA); numRatios++;
                    break;
                }

                case ElliottModelType.DOUBLE_ZIGZAG:
                {
                    double lenW = w[0].Length;
                    double lenX = w[1].Length;
                    double lenY = w[2].Length;

                    if (lenW <= 0) return 0;
                    product *= GetCorrectionFiboWeight(lenX / lenW); numRatios++;
                    product *= GetFiboWeight(ZIGZAG_C_TO_A, lenY / lenW); numRatios++;
                    break;
                }

                case ElliottModelType.FLAT_EXTENDED:
                {
                    double lenA = w[0].Length;
                    double lenB = w[1].Length;
                    double lenC = w[2].Length;

                    if (lenA <= 0) return 0;
                    product *= GetCorrectionFiboWeight(lenB / lenA); numRatios++;
                    product *= GetFiboWeight(MAP_EX_FLAT_WAVE_C_TO_A, lenC / lenA); numRatios++;
                    break;
                }

                case ElliottModelType.FLAT_RUNNING:
                {
                    double lenA = w[0].Length;
                    double lenB = w[1].Length;
                    double lenC = w[2].Length;

                    if (lenA <= 0) return 0;
                    product *= GetCorrectionFiboWeight(lenB / lenA); numRatios++;
                    product *= GetFiboWeight(MAP_RUNNING_FLAT_WAVE_C_TO_A, lenC / lenA); numRatios++;
                    break;
                }

                case ElliottModelType.TRIANGLE_CONTRACTING:
                case ElliottModelType.TRIANGLE_RUNNING:
                {
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

            if (numRatios == 0) return 0;

            // Geometric mean: normalize by number of ratios so a model with more ratio checks
            // doesn't intrinsically score lower than a simpler model.
            double geometricMean = Math.Pow(product, 1.0 / numRatios);

            // Complexity bonus: a model that satisfies more independent Fibo relationships is
            // more constrained (more specific) and should be preferred when the fit is equal.
            // Using sqrt(numRatios) as the bonus so IMPULSE(4)=×2 vs ZIGZAG(2)=×1.41.
            double complexityBonus = Math.Sqrt(numRatios);

            return modelCoeff * geometricMean * complexityBonus;
        }

        private static double GetModelCoeff(ElliottModelType model)
        {
            if (ElliottWavePatternHelper.ModelRules.TryGetValue(model, out ModelRules rules))
                return rules.ProbabilityCoefficient;
            return 0.25;
        }
    }
}
