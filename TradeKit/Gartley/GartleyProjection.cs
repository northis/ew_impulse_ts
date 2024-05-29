using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.AlgoBase;
using TradeKit.Core;

namespace TradeKit.Gartley
{
    internal class GartleyProjection
    {
        internal enum ProjectionState
        {
            NoProjection,
            ProjectionChanged,
            PatternFormed
        }
        internal enum CalculationState
        {
            A_TO_B,
            B_TO_C,
            A_TO_C,
            C_TO_D,
            D
        }

        private readonly PivotPointsFinder m_ExtremaFinder;
        private readonly Action<GartleyProjection> m_CancelAction;
        private readonly DateTime m_BorderDateTime;

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

            PATTERNS_MAP = PATTERNS.ToDictionary(a => a.PatternType, a => a);
        }

        internal static readonly GartleyPattern[] PATTERNS;
        internal static readonly Dictionary<GartleyPatternType, GartleyPattern> PATTERNS_MAP;

        public GartleyProjection(
            PivotPointsFinder extremaFinder,
            GartleyPatternType patternType, 
            BarPoint itemX, 
            BarPoint itemA,
            Action<GartleyProjection> cancelAction)
        {
            m_ExtremaFinder = extremaFinder;
            m_CancelAction = cancelAction;
            PatternType = PATTERNS_MAP[patternType];
            ItemX = itemX;
            ItemA = itemA;
            IsBull = itemX < itemA;
            m_BorderDateTime = itemA.OpenTime;
        }

        public void Update(double lastCandleMax, double lastCandleMin)
        {
            Update();
        }

        /// <summary>
        /// Checks the point.
        /// </summary>
        /// <param name="dt">The current dt.</param>
        /// <param name="value">The current value.</param>
        /// <param name="isHigh">if set to <c>true</c> if the value is a high extremum.</param>
        /// <returns>True if we can continue the calculation, false if the projection should be cancelled.</returns>
        private bool CheckPoint(DateTime dt, double value, bool isHigh)
        {
            CalculationState state = CalculateState();

            if (state is CalculationState.A_TO_B or CalculationState.A_TO_C)
                if (IsBull && value < ItemX || !IsBull && value > ItemX)
                    return false;

            switch (state)
            {
                case CalculationState.A_TO_B:
                    if (IsBull && value > ItemA || !IsBull && value < ItemA)
                        return false;
                    break;
                case CalculationState.B_TO_C:
                    break;
                case CalculationState.A_TO_C:
                    break;
                case CalculationState.C_TO_D:
                    break;
                case CalculationState.D:
                    break;
                default:
                    Logger.Write($"{nameof(CheckPoint)}: invalid state, check it");
                    break;
            }

            return true;
        }

        private CalculationState CalculateState()
        {
            if (ItemC == null && !PatternType.XBValues.Any())//for shark
                return CalculationState.A_TO_C;

            if (ItemB == null)
                return CalculationState.A_TO_B;

            if (ItemC == null)
                return CalculationState.B_TO_C;
            
            return ItemD == null ? CalculationState.C_TO_D : CalculationState.D;
        }

        public void Update()
        {
            foreach (DateTime extremaDt in 
                     m_ExtremaFinder.AllExtrema.SkipWhile(a => a <= m_BorderDateTime))
            {
                bool result = true;
                if (m_ExtremaFinder.HighExtrema.Contains(extremaDt))
                    result = CheckPoint(extremaDt, m_ExtremaFinder.HighValues[extremaDt], true);

                if (m_ExtremaFinder.LowExtrema.Contains(extremaDt))
                    result &= CheckPoint(extremaDt, m_ExtremaFinder.LowValues[extremaDt], false);

                if (result) continue;

                State = ProjectionState.NoProjection;
                m_CancelAction(this);
                return;
            }

            if (ItemB == null)
            {

            }

            if (IsBull)
            {
            }
            else
            {
                
            }
        }

        internal bool IsBull { get; private set; }

        internal ProjectionState State { get; private set; }

        internal GartleyPattern PatternType { get; }
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
