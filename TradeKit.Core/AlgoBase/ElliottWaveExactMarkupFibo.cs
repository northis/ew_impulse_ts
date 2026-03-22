using System;
using TradeKit.Core.Common;

namespace TradeKit.Core.AlgoBase
{
    public partial class ElliottWaveExactMarkup
    {
        private static readonly (byte weight, double ratio)[] ZIGZAG_C_TO_A = { (0, 0), (5, 0.618), (25, 0.786), (35, 0.786), (75, 1), (85, 1.618), (90, 2.618), (95, 3.618) };
        private static readonly (byte weight, double ratio)[] CONTRACTING_DIAGONAL_3_TO_1 = { (0, 0), (5, 0.5), (15, 0.618), (20, 0.786) };
        private static readonly (byte weight, double ratio)[] IMPULSE_3_TO_1 = { (0, 0), (5, 0.618), (10, 0.786), (15, 1), (25, 1.618), (60, 2.618), (75, 3.618), (90, 4.236) };
        private static readonly (byte weight, double ratio)[] IMPULSE_5_TO_1 = { (0, 0), (5, 0.382), (10, 0.618), (20, 0.786), (25, 1), (75, 1.618), (85, 2.618), (95, 3.618), (99, 4.236) };
        private static readonly (byte weight, double ratio)[] MAP_DEEP_CORRECTION = { (0, 0), (5, 0.5), (25, 0.618), (70, 0.786), (99, 0.95) };
        private static readonly (byte weight, double ratio)[] MAP_SHALLOW_CORRECTION = { (0, 0), (5, 0.236), (35, 0.382), (85, 0.5) };
        private static readonly (byte weight, double ratio)[] MAP_EX_FLAT_WAVE_C_TO_A = { (0, 0), (20, 1.618), (80, 2.618), (95, 3.618) };
        private static readonly (byte weight, double ratio)[] MAP_RUNNING_FLAT_WAVE_C_TO_A = { (0, 0), (5, 0.5), (20, 0.618), (80, 1), (90, 1.272), (95, 1.618) };
        private static readonly (byte weight, double ratio)[] MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV = { (0, 0), (5, 0.5), (20, 0.618), (80, 0.786), (90, 0.9), (95, 0.95) };

        private double GetFiboWeight((byte weight, double ratio)[] map, double ratio)
        {
            for (int i = map.Length - 1; i >= 0; i--)
            {
                if (ratio >= map[i].ratio - 1e-5)
                {
                    return Math.Max(0.01, map[i].weight / 100.0);
                }
            }
            return 0.01;
        }
    }
}
