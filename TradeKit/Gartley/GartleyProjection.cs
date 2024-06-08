using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.AlgoBase;
using TradeKit.Core;

namespace TradeKit.Gartley
{
    internal class GartleyProjection
    {
        private record RealLevelBase(double StartValue, double EndValue)
        {
            public readonly double Max = Math.Max(StartValue, EndValue);
            public readonly double Min = Math.Min(StartValue, EndValue);

            public double StartValue { get; } = StartValue;
            public double EndValue { get; } = EndValue;
        }

        private record RealLevelCombo
        {
            public RealLevelCombo(RealLevel xD, RealLevel bD)
            {
                Xd = xD;
                Bd = bD;
                Max = Math.Min(xD.Max, bD.Max);
                Min = Math.Max(xD.Min, bD.Min);
                IsMaxXd = Math.Abs(xD.Max - Max) < double.Epsilon;
                IsMinXd = Math.Abs(xD.Min - Min) < double.Epsilon;
            }

            public bool IsMaxXd { get; init; }
            public bool IsMinXd { get; init; }

            public RealLevel Xd { get; init; }
            public RealLevel Bd { get; init; }
            public double Max { get; init; }
            public double Min { get; init; }
        }


        private record RealLevel(double Ratio, double StartValue, double EndValue)
            : RealLevelBase(StartValue, EndValue);

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
        private DateTime m_BorderExtremaDateTime; // (slow)
        private DateTime m_BorderCandleDateTime; // (false)
        private readonly RealLevel[] m_RatioToAcLevelsMap;
        private readonly RealLevel[] m_RatioToXbLevelsMap;
        private readonly RealLevel[] m_RatioToXdLevelsMap;
        private RealLevel[] m_RatioToBdLevelsMap;

        private readonly List<RealLevelCombo> m_XdToDbMapSortedItems;
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
            m_BorderExtremaDateTime = itemA.OpenTime;
            LengthAtoX = Math.Abs(ItemA - ItemX);
            m_IsUpK = IsBull ? 1 : -1;
            m_RatioToAcLevelsMap = InitPriceRanges(PatternType.ACValues, false);
            m_RatioToXbLevelsMap = InitPriceRanges(PatternType.XBValues, true);
            m_RatioToXdLevelsMap = InitPriceRanges(PatternType.XDValues, false);
            //We cannot initialize BD ranges until point B is calculated.

            m_XdToDbMapSortedItems = new List<RealLevelCombo>();
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
        private RealLevel[] InitPriceRanges(
            double[] ratios, bool useCounterPoint, double baseLength)
        {
            var resValues = new RealLevel[ratios.Length];
            int isUpLocal = useCounterPoint ? -1 * m_IsUpK : m_IsUpK;
            double countPoint = useCounterPoint ? ItemA.Value : ItemX.Value;
            for (int i = 0; i < ratios.Length; i++)
            {
                double val = ratios[i];
                double xLength = baseLength * val;
                double xLengthAllowance = xLength * (1 + m_WickAllowanceZeroToOne);
                double xPoint = countPoint + isUpLocal * xLength;
                double xPointLimit = countPoint + isUpLocal * xLengthAllowance;

                resValues[i] = new (val, xPoint, xPointLimit);
            }

            return resValues;
        }

        private RealLevel[] InitPriceRanges(
            double[] ratios, bool useCounterPoint)
        {
            return InitPriceRanges(ratios, useCounterPoint, LengthAtoX);
        }

        public void Update(double lastCandleMax, double lastCandleMin)
        {
            Update();
        }

        private void UpdateXdToDbMaps()
        {
            if (m_RatioToBdLevelsMap == null)
                return;

            m_XdToDbMapSortedItems.Clear();// Reset the previous map

            foreach (RealLevel mapBd in m_RatioToBdLevelsMap.OrderBy(a => a.Ratio))
            {
                foreach (RealLevel mapXd in m_RatioToXdLevelsMap.OrderBy(a => a.Ratio))
                {
                    RealLevelCombo toAdd = new RealLevelCombo(mapXd, mapBd);
                    if (toAdd.Max < toAdd.Min)
                        continue;// No intersection for this pair, skip the ratio

                    m_XdToDbMapSortedItems.Add(toAdd);
                }
            }
        }

        private void UpdateC(DateTime dt, double value)
        {
            foreach (RealLevel levelRange in m_RatioToAcLevelsMap)
            {
                if (IsBull && (value < levelRange.StartValue || value > levelRange.EndValue) ||
                    !IsBull && (value > levelRange.StartValue || value < levelRange.EndValue))
                {
                    continue;
                }

                ItemC = new BarPoint(value, dt, m_BarsProvider);
                ActualAtoC = levelRange.Ratio;
            }
        }

        private void UpdateD(DateTime dt, double value)
        {
            List<RealLevel> toLevelsToDelete = null;

            void RemoveMin(RealLevelCombo levelRangeCombo)
            {
                toLevelsToDelete ??= new List<RealLevel>();
                toLevelsToDelete.Add(levelRangeCombo.IsMinXd
                    ? levelRangeCombo.Xd
                    : levelRangeCombo.Bd);
            }

            void RemoveMax(RealLevelCombo levelRangeCombo)
            {
                toLevelsToDelete ??= new List<RealLevel>();
                toLevelsToDelete.Add(levelRangeCombo.IsMaxXd
                    ? levelRangeCombo.Xd
                    : levelRangeCombo.Bd);
            }

            foreach (RealLevelCombo levelRangeCombo in
                     m_XdToDbMapSortedItems.OrderBy(a => a.Xd.Ratio))
            {
                if (IsBull)
                {
                    if (value > levelRangeCombo.Max)
                        continue;

                    RemoveMin(levelRangeCombo);

                    //the price goes beyond the range, we should remove the corresponding ratios.
                    if (value < levelRangeCombo.Min)
                        continue;
                }
                else
                {
                    if (value < levelRangeCombo.Min)
                        continue;

                    RemoveMax(levelRangeCombo);
                    if (value > levelRangeCombo.Max)
                        continue;
                }

                //We won't update the same ratio range
                if (Math.Abs(ActualXtoD - levelRangeCombo.Xd.Ratio) < double.Epsilon) continue;

                ItemD = new BarPoint(value, dt, m_BarsProvider);
                ActualXtoD = levelRangeCombo.Xd.Ratio;
                m_PatternIsReady = true;
            }

            if (toLevelsToDelete == null)
                return;

            foreach (RealLevel levelsToDelete in toLevelsToDelete)
            {
                // If the level is used, we should remove the whole level from the combos collection
                m_XdToDbMapSortedItems.RemoveAll(
                    a => levelsToDelete == a.Bd || levelsToDelete == a.Xd);
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

            foreach (RealLevel levelRange in m_RatioToXbLevelsMap)
            {
                if (IsBull)
                {
                    if (value > levelRange.StartValue || value < levelRange.EndValue) continue;
                    if (ItemB != null && ItemB.Value < value) continue;
                }
                else
                {
                    if (value < levelRange.StartValue || value > levelRange.EndValue) continue;
                    if (ItemB != null && ItemB.Value > value) continue;
                }

                ItemB = new BarPoint(value, dt, m_BarsProvider);
                ActualXtoB = levelRange.Ratio;
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
            if (m_CalculationStateCache == CalculationState.NONE)
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
        /// <returns>Result of the Update process</returns>
        public ProjectionState Update()
        {
            bool prevPatternIsReady = m_PatternIsReady;
            m_PatternIsReady = false;

            DateTime borderExtremaDateTimeLocal = m_BorderExtremaDateTime;
            foreach (DateTime extremaDt in
                     m_ExtremaFinder.AllExtrema.SkipWhile(a => a <= borderExtremaDateTimeLocal))
            {
                bool result = true;
                m_BorderExtremaDateTime = extremaDt;
                if (m_ExtremaFinder.HighExtrema.Contains(extremaDt))
                    result = CheckPoint(extremaDt, m_ExtremaFinder.HighValues[extremaDt], true);

                if (m_ExtremaFinder.LowExtrema.Contains(extremaDt))
                    result &= CheckPoint(extremaDt, m_ExtremaFinder.LowValues[extremaDt], false);

                if (result) continue;

                m_CalculationStateCache = CalculationState.NONE;
                return ProjectionState.NoProjection;
            }

            m_BorderCandleDateTime = m_BorderExtremaDateTime;

            //update D based on latest candles
            if (m_CalculationStateCache is CalculationState.C_TO_D or CalculationState.D)
            {
                int lastUsedIndex = m_BarsProvider.GetIndexByTime(m_BorderCandleDateTime);
                for (int i = lastUsedIndex + 1; i < m_BarsProvider.Count; i++)
                {
                    DateTime currentDt = m_BarsProvider.GetOpenTime(i);
                    double low = m_BarsProvider.GetLowPrice(i);
                    double high = m_BarsProvider.GetLowPrice(i);

                    if (IsBull)
                    {
                        if (high > ItemA)
                        {
                            m_CalculationStateCache = CalculationState.NONE;
                            return ProjectionState.NoProjection;
                        }

                        if (CheckPoint(currentDt, low, false))
                            continue;
                    }
                    else
                    {
                        if (low < ItemA)
                        {
                            m_CalculationStateCache = CalculationState.NONE;
                            return ProjectionState.NoProjection;
                        }

                        if (CheckPoint(currentDt, high, true))
                            continue;
                    }

                    m_BorderCandleDateTime = currentDt;
                    m_CalculationStateCache = CalculationState.NONE;
                    return ProjectionState.NoProjection;
                }
            }

            if (m_PatternIsReady)
                return prevPatternIsReady
                    ? ProjectionState.ProjectionChanged
                    : ProjectionState.PatternFormed;

            return prevPatternIsReady
                ? ProjectionState.PatternFormed
                : ProjectionState.NoProjection;
        }

        internal bool IsBull { get; private set; }
        internal double LengthAtoX { get; private set; }
        
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
                UpdateXdToDbMaps();
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
