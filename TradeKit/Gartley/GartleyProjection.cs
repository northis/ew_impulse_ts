using System;
using TradeKit.Core;

namespace TradeKit.Gartley
{
    internal class GartleyProjection
    {
        internal static readonly double[] LEVELS =
        {
            0.236,
            0.382,
            0.5,
            0.618,
            0.707,
            0.786,
            0.886,
            1,
            1.13,
            1.272,
            1.41,
            1.618,
            2,
            2.24,
            2.618,
            3.14,
            3.618
        };

        static GartleyProjection()
        {
            PATTERNS = new GartleyPattern[]
            {
                new(GartleyPatternType.GARTLEY,
                    XBValues: new[] {0.618},
                    XDValues: new[] {0.786},
                    BDValues: LEVELS.RangeVal(1.13, 1.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886)),
                new(GartleyPatternType.BUTTERFLY,
                    XBValues: new[] {0.786},
                    XDValues: LEVELS.RangeVal(1.27, 1.41),
                    BDValues: LEVELS.RangeVal(1.618, 2.24),
                    ACValues: LEVELS.RangeVal(0.382, 0.886)),
                new(GartleyPatternType.SHARK,
                    XBValues: Array.Empty<double>(),
                    XDValues: LEVELS.RangeVal(0.886, 1.13),
                    BDValues: LEVELS.RangeVal(1.618, 2.24),
                    ACValues: LEVELS.RangeVal(1.13, 1.618)),
                new(GartleyPatternType.CRAB,
                    XBValues: LEVELS.RangeVal(0.382, 0.618),
                    XDValues: new[] {1.618},
                    BDValues: LEVELS.RangeVal(2.618, 3.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886)),
                new(GartleyPatternType.DEEP_CRAB,
                    XBValues: new[] {0.886},
                    XDValues: new[] {1.618},
                    BDValues: LEVELS.RangeVal(2, 3.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886)),
                new(GartleyPatternType.BAT,
                    XBValues: LEVELS.RangeVal(0.382, 0.5),
                    XDValues: new[] {0.886},
                    BDValues: LEVELS.RangeVal(1.618, 2.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886)),
                new(GartleyPatternType.ALT_BAT,
                    XBValues: new[] {0.382},
                    XDValues: new[] {1.13},
                    BDValues: LEVELS.RangeVal(2, 3.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886)),
                new(GartleyPatternType.CYPHER,
                    XBValues: LEVELS.RangeVal(0.382, 0.618),
                    XDValues: new[] {0.786},
                    BDValues: LEVELS.RangeVal(1.272, 2),
                    ACValues: LEVELS.RangeVal(1.13, 1.41),
                    SetupType: GartleySetupType.CD)
            };
        }

        internal static readonly GartleyPattern[] PATTERNS;

        public GartleyProjection(GartleyPatternType patternType, BarPoint itemX, BarPoint itemA)
        {
            PatternType = patternType;
            ItemX = itemX;
            ItemA = itemA;
        }

        internal bool IsBull => ItemX.Value < ItemA.Value;

        internal GartleyPatternType PatternType { get; }
        internal BarPoint ItemX { get; }
        internal BarPoint ItemA { get; }
        internal BarPoint ItemB { get; set; }
        internal BarPoint ItemC { get; set; }
        internal BarPoint ItemD { get; set; }

        Tuple<double,double>[] XtoD { get; set; }
        Tuple<double, double>[] AtoC { get; set; }
        Tuple<double, double>[] BtoD { get; set; }
        Tuple<double, double>[] XtoB { get; set; }
    }
}
