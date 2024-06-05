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
            D,
            NONE
        }

        private readonly IBarsProvider m_BarsProvider;
        private CalculationState m_CalculationStateCache;
        private readonly PivotPointsFinder m_ExtremaFinder;
        private readonly double m_WickAllowanceZeroToOne;
        private readonly int m_IsUpK;
        private bool m_PatternIsReady = false;
        private readonly DateTime m_BorderDateTime;
        private readonly (double, double, double)[] m_RatioToAcLevelsMap;
        private readonly (double, double, double)[] m_RatioToXbLevelsMap;
        private readonly (double, double, double)[] m_RatioToXdLevelsMap;
        private (double, double, double)[] m_RatioToBdLevelsMap;//TODO handle intersections with XD
        private BarPoint m_ItemB;

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
            IBarsProvider barsProvider,
            PivotPointsFinder extremaFinder,
            GartleyPatternType patternType, 
            BarPoint itemX, 
            BarPoint itemA,
            double wickAllowanceZeroToOne)
        {
            m_BarsProvider = barsProvider;
            m_ExtremaFinder = extremaFinder;
            m_WickAllowanceZeroToOne = wickAllowanceZeroToOne;
            PatternType = PATTERNS_MAP[patternType];
            ItemX = itemX;
            ItemA = itemA;
            IsBull = itemX < itemA;
            m_BorderDateTime = itemA.OpenTime;
            LengthAtoX = Math.Abs(ItemA - ItemX);
            m_IsUpK = IsBull ? 1 : -1;
            m_RatioToAcLevelsMap = InitPriceRanges(PatternType.ACValues, false);
            m_RatioToXbLevelsMap = InitPriceRanges(PatternType.XBValues, true);
            m_RatioToXdLevelsMap = InitPriceRanges(PatternType.XDValues, false);
            //We cannot initialize BD ranges until point B is calculated.
        }

        /// <summary>
        /// Initializes the actual price ranges from the ratio values given.
        /// Let's assume we have XA from 100 to 105, this is a bull pattern. L=LengthAtoX=(105-100). We use XB=0.618, so the point B = A-L*0.618 (useCounterPoint=true). If we use AC=0.382, the point A = X+L*0.382 (useCounterPoint=false).
        /// NOTE. Order in the result array is important! It should always go in X->A|A->B|A->C|A->D|B->D directions.
        /// </summary>
        /// <param name="ratios">The ratios.</param>
        /// <param name="useCounterPoint">if set to <c>true</c> we should use the counter-point (for instance, if XA is up, so AB is down, and we should use low extrema instead of high ones; AC is up again, so we use the highs).</param>
        /// <param name="baseLength">The basic value we should calculate the ranges against.</param>
        /// <returns>The ratios with actual prices with allowance (initial relative ratio, actual price level, actual price level with allowance).</returns>
        private (double, double, double)[] InitPriceRanges(
            double[] ratios, bool useCounterPoint, double baseLength)
        {
            var resValues = new (double, double, double)[ratios.Length];
            int isUpLocal = useCounterPoint ? -1 * m_IsUpK : m_IsUpK;
            double countPoint = useCounterPoint ? ItemA.Value : ItemX.Value;
            for (int i = 0; i < ratios.Length; i++)
            {
                double val = ratios[i];
                double xLength = baseLength * val;
                double xLengthAllowance = xLength * (1 + m_WickAllowanceZeroToOne);
                double xPoint = countPoint + isUpLocal * xLength;
                double xPointLimit = countPoint + isUpLocal * xLengthAllowance;

                resValues[i] = (val, xPoint, xPointLimit);
            }

            return resValues;
        }

        private (double, double, double)[] InitPriceRanges(
            double[] ratios, bool useCounterPoint)
        {
            return InitPriceRanges(ratios, useCounterPoint, LengthAtoX);
        }

        public void Update(double lastCandleMax, double lastCandleMin)
        {
            Update();
        }

        private void UpdateC(DateTime dt, double value)
        {
            foreach ((double, double, double) levelRange in m_RatioToAcLevelsMap)
            {
                if (IsBull && (value < levelRange.Item2 || value > levelRange.Item3) ||
                    !IsBull && (value > levelRange.Item2 || value < levelRange.Item3))
                {
                    continue;
                }

                ItemC = new BarPoint(value, dt, m_BarsProvider);
                ActualAtoC = levelRange.Item1;
            }
        }

        private void UpdateD(DateTime dt, double value)
        {
            foreach ((double, double, double) levelRange in m_RatioToXdLevelsMap)
            {
                if (IsBull && (value > levelRange.Item2 || value < levelRange.Item3) ||
                    !IsBull && (value < levelRange.Item2 || value > levelRange.Item3))
                {
                    continue;
                }

                //We won't update the same ratio range
                if (Math.Abs(ActualXtoD - levelRange.Item1) < double.Epsilon) continue;

                ItemD = new BarPoint(value, dt, m_BarsProvider);
                ActualXtoD = levelRange.Item1;
                m_PatternIsReady = true;
            }
        }

        private void UpdateB(DateTime dt, double value)
        {
            if (m_RatioToXbLevelsMap.Length == 0 &&
                (ItemB == null ||
                 IsBull && value < ItemB ||
                 !IsBull && value > ItemB))
            {
                ItemB = new BarPoint(value, dt, m_BarsProvider);
                return;
            }

            foreach ((double, double, double) levelRange in m_RatioToXbLevelsMap)
            {
                if (IsBull)
                {
                    if (value > levelRange.Item2 || value < levelRange.Item3) continue;
                    if (ItemB != null && ItemB.Value < value) continue;
                }
                else
                {
                    if (value < levelRange.Item2 || value > levelRange.Item3) continue;
                    if (ItemB != null && ItemB.Value > value) continue;
                }

                ItemB = new BarPoint(value, dt, m_BarsProvider);
                ActualXtoB = levelRange.Item1;
            }
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
            UpdateCalculateState();
            bool isStraightExtrema = IsBull == isHigh;

            if (m_CalculationStateCache is CalculationState.A_TO_B or CalculationState.A_TO_C)
                if (IsBull && value < ItemX || !IsBull && value > ItemX)
                    return false;

            switch (m_CalculationStateCache)
            {
                case CalculationState.A_TO_B:
                    if (IsBull && value > ItemA || !IsBull && value < ItemA)
                        return false;

                    if (isStraightExtrema)// counter-extrema needed only
                        return true;

                    UpdateB(dt, value);

                    break;
                case CalculationState.B_TO_C:
                case CalculationState.A_TO_C:
                    if (!isStraightExtrema)// direct extrema needed only
                    {
                        UpdateB(dt, value);
                        return true;
                    }

                    UpdateC(dt, value);
                    break;
                case CalculationState.C_TO_D:
                    if (!isStraightExtrema)
                    {
                        UpdateD(dt, value);
                    }

                    // Here we can re-calculate B and C points.
                    if (IsBull && ItemC > ItemA || !IsBull && ItemC > ItemA)
                    {
                        // Since then, we no longer can move the point B.
                        if (!isStraightExtrema)
                            return true;

                        UpdateC(dt, value);
                    }
                    else
                    {
                        if (isStraightExtrema)
                            UpdateC(dt, value);
                        else
                            UpdateB(dt, value);
                    }

                    break;
                case CalculationState.D:
                    if (!isStraightExtrema)
                        UpdateD(dt, value);
                    break;
                case CalculationState.NONE:
                    return false;
                default:
                    Logger.Write($"{nameof(CheckPoint)}: invalid state, check it");
                    break;
            }

            return true;
        }

        private void UpdateCalculateState()
        {
            if(m_CalculationStateCache == CalculationState.NONE)
                return;
            
            if (ItemC == null && !PatternType.XBValues.Any())//for shark
                m_CalculationStateCache = CalculationState.A_TO_C;

            if (ItemB == null)
            {
                m_CalculationStateCache = CalculationState.A_TO_B;
                return;
            }

            if (ItemC == null)
            {
                m_CalculationStateCache = CalculationState.B_TO_C;
                return;
            }

            m_CalculationStateCache = ItemD == null ? CalculationState.C_TO_D : CalculationState.D;
        }

        /// <summary>
        /// Updates the projections based on new extrema.
        /// </summary>
        public void Update()
        {
            bool prevPatternIsReady = m_PatternIsReady;
            m_PatternIsReady = false;
            foreach (DateTime extremaDt in 
                     m_ExtremaFinder.AllExtrema.SkipWhile(a => a <= m_BorderDateTime))
            {
                bool result = true;
                if (m_ExtremaFinder.HighExtrema.Contains(extremaDt))
                    result = CheckPoint(extremaDt, m_ExtremaFinder.HighValues[extremaDt], true);

                if (m_ExtremaFinder.LowExtrema.Contains(extremaDt))
                    result &= CheckPoint(extremaDt, m_ExtremaFinder.LowValues[extremaDt], false);

                if (result) continue;

                m_CalculationStateCache = CalculationState.NONE;
                return;
            }

            //TODO update D based on latest candles

            if (!m_PatternIsReady)
                return;

            State = prevPatternIsReady
                ? ProjectionState.ProjectionChanged
                : ProjectionState.PatternFormed;
        }

        internal bool IsBull { get; private set; }
        internal double LengthAtoX { get; private set; }

        internal ProjectionState State { get; private set; }

        internal GartleyPattern PatternType { get; }
        internal BarPoint ItemX { get; }
        internal BarPoint ItemA { get; }

        internal BarPoint ItemB
        {
            get => m_ItemB;
            set
            {
                m_ItemB = value;
                m_RatioToBdLevelsMap = InitPriceRanges(
                    PatternType.BDValues, true, Math.Abs(ItemA - value));
            }
        }

        internal BarPoint ItemC { get; set; }
        internal BarPoint ItemD { get; set; }

        Tuple<double,double>[] XtoD { get; set; }
        Tuple<double, double>[] AtoC { get; set; }
        Tuple<double, double>[] BtoD { get; set; }
        Tuple<double, double>[] XtoB { get; set; }
        internal double ActualXtoD { get; set; }
        internal double ActualAtoC { get; set; }
        internal double ActualBtoD { get; set; }
        internal double ActualXtoB { get; set; }
    }
}
