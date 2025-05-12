using System.Diagnostics;
using TradeKit.Core.Common;

namespace TradeKit.Core.Gartley
{
    /// <summary>
    /// Contains pattern finding logic for one XA points given.
    /// This class saves its state, and it is not a thread-safe
    /// </summary>
    internal class GartleyProjection
    {
        private readonly IBarsProvider m_BarsProvider;
        private readonly double m_WickAllowanceZeroToOne;
        private double m_Min;
        private DateTime m_MinDate;
        private double m_Max;
        private DateTime m_MaxDate;
        private double m_ItemDCancelPrice = double.NaN;
        private readonly int m_IsUpK;
        private bool m_PatternIsReady;
        private bool m_ProjectionIsReady;
        private bool m_IsInvalid;
        private RealLevel[] m_RatioToAcLevelsMap;
        private readonly RealLevel[] m_RatioToXbLevelsMap;
        private readonly RealLevel[] m_RatioToXdLevelsMap;
        private RealLevel[] m_RatioToBdLevelsMap;
        private readonly bool m_IsCd;

        private readonly List<RealLevelCombo> m_XdToDbMapSortedItems;
        private BarPoint m_ItemC;
        private BarPoint m_ItemB;
        
        private const double SL_RATIO = 0.272;
        private const double TP1_RATIO = 0.382;
        private const double TP2_RATIO = 0.618;
        private const double MAX_SL_TP_RATIO_ALLOWED = 2;

        private static readonly double[] LEVELS =
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
            Patterns = new GartleyPattern[]
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
                    SetupType: GartleySetupType.CD),
               // new(GartleyPatternType.FIVE_ZERO,
               //     XBValues: Array.Empty<double>(),
               //     XDValues: Array.Empty<double>(),
               //     BDValues: LEVELS.RangeVal(1.618, 2.24),
               //     ACValues: LEVELS.RangeVal(1.13, 1.618),
               //     CEValues: new[] {0.5},
               //     SetupType: GartleySetupType.CD)
            };

            PATTERNS_MAP = Patterns.ToDictionary(a => a.PatternType, a => a);
        }

        internal static readonly GartleyPattern[] Patterns;
        private static readonly Dictionary<GartleyPatternType, GartleyPattern> PATTERNS_MAP;

        private void UpdateAtoC()
        {
            if (m_IsCd)
            {
                m_RatioToAcLevelsMap = InitPriceRanges(
                    PatternType.ACValues, false, LengthAtoX, ItemX.Value);
            }
            else
            {
                if (ItemB == null)
                    return;
                
                double lengthAtoB = Math.Abs(ItemA - ItemB);
                m_RatioToAcLevelsMap = InitPriceRanges(
                    PatternType.ACValues, false, lengthAtoB, ItemB.Value);
            }
        }
        
        public GartleyProjection(
            IBarsProvider barsProvider,
            GartleyPatternType patternType, 
            BarPoint itemX, 
            BarPoint itemA,
            double wickAllowanceZeroToOne)
        {
            m_BarsProvider = barsProvider;
            m_WickAllowanceZeroToOne = wickAllowanceZeroToOne;
            PatternType = PATTERNS_MAP[patternType];
            ItemX = itemX;
            ItemA = itemA;
            IsBull = itemX < itemA;
            LengthAtoX = Math.Abs(ItemA - ItemX);
            m_IsCd = PatternType.SetupType == GartleySetupType.CD;

            m_Min = double.PositiveInfinity;
            m_Max = double.NegativeInfinity;
            m_MinDate = itemA.OpenTime;
            m_MaxDate = itemA.OpenTime;

            m_IsUpK = IsBull ? 1 : -1;
            UpdateAtoC();

            m_RatioToXbLevelsMap = PatternType.XBValues.Any()
                ? InitPriceRanges(
                    PatternType.XBValues, true, LengthAtoX, ItemA.Value)
                : new[] { new RealLevel(0, itemA.Value, itemX.Value) };

            m_RatioToXdLevelsMap = InitPriceRanges(
                PatternType.XDValues, true, LengthAtoX, ItemA.Value);

            //We cannot initialize BD/XD ranges until points B/C is calculated.
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
        /// <param name="countPoint">Count point we should count ratios against.</param>
        /// <returns>The ratios with actual prices with allowance (initial relative ratio, actual price level, actual price level with allowance).</returns>
        private RealLevel[] InitPriceRanges(
            double[] ratios, bool useCounterPoint, double baseLength, double countPoint)
        {
            var resValues = new RealLevel[ratios.Length];
            int isUpLocal = useCounterPoint ? -1 * m_IsUpK : m_IsUpK;

            for (int i = 0; i < ratios.Length; i++)
            {
                double val = ratios[i];
                double ratioStart = val * (1 - m_WickAllowanceZeroToOne);
                double ratioEnd = val * (1 + m_WickAllowanceZeroToOne);
                double xLengthStart = baseLength * ratioStart;
                double xLengthEnd = baseLength * ratioEnd;
                double xPointStart = countPoint + isUpLocal * xLengthStart;
                double xPointEnd = countPoint + isUpLocal * xLengthEnd;

                resValues[i] = new (val, xPointStart, xPointEnd);
            }

            return resValues;
        }
        
        private void UpdateXdToDbMaps()
        {
            if (m_RatioToBdLevelsMap == null)
                return;

            m_XdToDbMapSortedItems.Clear();// Reset the previous map

            foreach (RealLevel mapBd in m_RatioToBdLevelsMap.OrderByDescending(a => a.Ratio))
            {
                foreach (RealLevel mapXd in m_RatioToXdLevelsMap.OrderByDescending(a => a.Ratio))
                {
                    var toAdd = new RealLevelCombo(mapXd, mapBd);
                    if (toAdd.Max < toAdd.Min)
                        continue;// No intersection for this pair, skip the ratio

                    m_XdToDbMapSortedItems.Add(toAdd);
                }
            }

            if (!m_XdToDbMapSortedItems.Any())
                return;

            RealLevelCombo lastLevel = IsBull
                ? m_XdToDbMapSortedItems.MinBy(a => a.Min)
                : m_XdToDbMapSortedItems.MaxBy(a => a.Max);

            //m_XdToDbMapSortedItems.RemoveAll(a => a != lastLevel);
            m_ItemDCancelPrice = IsBull 
                ? lastLevel.Min 
                : lastLevel.Max;
        }

        public bool IsPatternFitForTrade(out double sl, out double tp1, out double tp2)
        {
            double cD = Math.Abs(ItemC - ItemD);
            double aD = Math.Abs(ItemA - ItemD);
            bool isBull = IsBull;
            double closeD = m_BarsProvider.GetClosePrice(ItemD.BarIndex);
 
            double actualSize = PatternType.SetupType == GartleySetupType.AD ? aD : cD;

            double slLen = actualSize * SL_RATIO;
            double tp1Len = actualSize * TP1_RATIO;
            sl = isBull ? -slLen + ItemD : slLen + ItemD;
            //double tp1Len = Math.Abs(sl - closeD);

            tp1 = isBull ? tp1Len + ItemD : -tp1Len + ItemD;
            if (isBull && closeD - tp1 >= 0 || !isBull && closeD - tp1 <= 0)
            {
                //Logger.Write("TP is already hit.");
                tp2 = double.NaN;
                return false;
            }

            double tp2Len = actualSize * TP2_RATIO;
            tp2 = isBull ? tp2Len + ItemD : -tp2Len + ItemD;

            double def = Math.Abs(closeD - sl) / Math.Abs(closeD - tp1);
            if (def > MAX_SL_TP_RATIO_ALLOWED)
            {
                //Logger.Write("SL/TP is too big.");
                return false;
            }

            return true;
        }


        private void UpdateC(DateTime dt, double value)
        {
            if (ItemB == null)
                return;

            if (ItemC != null &&
                (IsBull && value > ItemC || !IsBull && value < ItemC))
            {
                // The price goes beyond the existing C, we should reset it
                ItemC = null;
            }

            foreach (RealLevel levelRange in m_RatioToAcLevelsMap.OrderBy(a => a.Ratio))
            {
                if (IsBull)
                {
                    if (value < levelRange.StartValue || value > levelRange.EndValue) continue;
                    if (ItemC != null && ItemC.Value > value) continue;
                }
                else
                {
                    if (value > levelRange.StartValue || value < levelRange.EndValue) continue;
                    if (ItemC != null && ItemC.Value < value) continue;
                }

                if (dt <= ItemB.OpenTime) //TODO do we need this?
                    break;

                if (IsBull && m_Min < ItemB && m_MinDate > ItemB.OpenTime ||
                    !IsBull && m_Max > ItemB && m_MaxDate > ItemB.OpenTime)
                {
                    ItemB = null;
                    break;
                }

                ItemC = new BarPoint(value, dt, m_BarsProvider);
                AtoC = levelRange.Ratio;
                break;
            }
        }

        /// <summary>
        /// Gets ranges for the D point for the projection if available.
        /// </summary>
        /// <returns>The D point.</returns>
        public List<RealLevelCombo> GetProjectionDPoint()
        {
            if (!m_ProjectionIsReady)
                return null;

            return m_XdToDbMapSortedItems;
        }

        private void UpdateD(DateTime dt, double value)
        {
            List<RealLevel> levelsToDelete = null;
            void Remove(RealLevel levelRange)
            {
                levelsToDelete ??= new List<RealLevel>();
                levelsToDelete.Add(levelRange);
            }

            foreach (RealLevelCombo levelRangeCombo in m_XdToDbMapSortedItems.OrderBy(a => a.Bd.Ratio))
            {
                if (IsBull)
                {
                    if (value > levelRangeCombo.Max)
                        continue;

                    if (value < levelRangeCombo.Min)
                    {
                        if (value < levelRangeCombo.Bd.Min)
                            Remove(levelRangeCombo.Bd);

                        if (value < levelRangeCombo.Xd.Min)
                            Remove(levelRangeCombo.Xd);
                        continue;
                    }
                }
                else
                {
                    if (value < levelRangeCombo.Min)
                        continue;

                    if (value > levelRangeCombo.Max)
                    {
                        if (value > levelRangeCombo.Bd.Max)
                            Remove(levelRangeCombo.Bd);

                        if (value > levelRangeCombo.Xd.Max)
                            Remove(levelRangeCombo.Xd);
                        continue;
                    }
                }

                Remove(levelRangeCombo.Bd);
                Remove(levelRangeCombo.Xd);

                //We won't update the same ratio range
                //if (Math.Abs(XtoD - levelRangeCombo.Xd.Ratio) < double.Epsilon) continue;

                if (ItemD != null && (IsBull && ItemD.Value < value ||
                                      !IsBull && ItemD.Value > value))
                    continue;

                ItemD = new BarPoint(value, dt, m_BarsProvider);
                if (!IsPatternFitForTrade(out _, out _, out _))
                {
                    //ItemD = null;
                    continue;
                }
                
                XtoD = levelRangeCombo.Xd.Ratio;
                BtoD = levelRangeCombo.Bd.Ratio;

                m_PatternIsReady = true;
                m_ProjectionIsReady = false;// Stop using the projection when we got the whole pattern
                break;
            }

            if (levelsToDelete == null || !m_PatternIsReady)
                return;

            foreach (RealLevel levelToDelete in levelsToDelete)
            {
                // If the level is used, we should remove the whole level from the combos collection
                m_XdToDbMapSortedItems.RemoveAll(
                    a => levelToDelete == a.Bd || levelToDelete == a.Xd);
            }
        }

        private void UpdateB(DateTime dt)
        {
            //if (m_IsBItemLocked)
            //    return;

            if (ItemC != null && ItemB != null && ItemC.OpenTime <= dt ||
                ItemBSecond == null)
                return;

            double value = ItemBSecond.Value;
            dt = ItemBSecond.OpenTime;
            
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
                XtoB = levelRange.Ratio;

                break;
            }
        }

        /// <summary>
        /// Checks the point.
        /// </summary>
        /// <param name="dt">The current dt.</param>
        /// <param name="value">The current value.</param>
        /// <param name="isHigh">if set to <c>true</c> if the value is a high extremum.</param>
        /// <returns>True if we can continue the calculation, false if the projection should be canceled.</returns>
        private bool CheckPoint(DateTime dt, double value, bool isHigh)
        {
            bool isStraightExtrema = IsBull == isHigh;
            // if (m_ItemBRange.Min > value || m_ItemBRange.Max < value)
            // {
            //     if (ItemB == null || ItemC == null)
            //         return false;
            // }

            if (!isStraightExtrema)
            {
                if ((ItemBSecond == null ||
                    IsBull && ItemBSecond.Value > value ||
                    !IsBull && ItemBSecond.Value < value) &&
                    ItemA.OpenTime != dt)
                {
                    ItemBSecond = new BarPoint(value, dt, m_BarsProvider);
                }

                if (ItemC == null)
                    UpdateB(dt);
            }

            if (isStraightExtrema)
                UpdateC(dt, value);

            if (!isStraightExtrema)
                UpdateD(dt, value);

            return true;
        }

        /// <summary>
        /// Updates the projections based on new extreme.
        /// </summary>
        /// <param name="index">The point we want to calculate against.</param>
        /// <returns>Result of the Update process</returns>
        public ProjectionState Update(int index)
        {
            if (m_PatternIsReady)
                return ProjectionState.PATTERN_SAME;

            if (m_IsInvalid)
                return ProjectionState.NO_PROJECTION;

            DateTime currentDt = m_BarsProvider.GetOpenTime(index);

            bool prevPatternIsReady = m_PatternIsReady;
            bool prevProjectionIsReady = m_ProjectionIsReady;
            m_ProjectionIsReady = false;
            m_PatternIsReady = false;
            
            double high = m_BarsProvider.GetHighPrice(index);
            double low = m_BarsProvider.GetLowPrice(index);

            if (low < m_Min)
            {
                m_Min = low;
                m_MinDate = currentDt;
            }

            if (high > m_Max)
            {
                m_Max = high;
                m_MaxDate = currentDt;
            }

            // if (IsBull && high > m_ItemACancelPrice ||
            //     !IsBull && low < m_ItemACancelPrice)
            // {
            //     m_IsInvalid = true;
            //     return ProjectionState.NO_PROJECTION;
            // }

            //if the price squeezes through all the levels without a pattern
            if (!double.IsNaN(m_ItemDCancelPrice) && !m_PatternIsReady)
            {
                if (IsBull && low < m_ItemDCancelPrice ||
                    !IsBull && high > m_ItemDCancelPrice)
                {
                    m_IsInvalid = true;
                    return ProjectionState.NO_PROJECTION;
                }
            }

            if (ItemB == null)
            {
                if (IsBull && (low < ItemX || high > ItemA) ||
                    !IsBull && (high > ItemX || high < ItemA))
                {
                    m_IsInvalid = true;
                    return ProjectionState.NO_PROJECTION;
                }
            }

            if (ItemC == null)
            {
                if (IsBull && low < ItemX || !IsBull && high > ItemX)
                {
                    m_IsInvalid = true;
                    return ProjectionState.NO_PROJECTION;
                }
            }
            
            bool result = CheckPoint(currentDt, high, true);
            result &= CheckPoint(currentDt, low, false);

            if (!result)
            {
                m_PatternIsReady = false;
                m_ProjectionIsReady = false;
                m_IsInvalid = true;
                return ProjectionState.NO_PROJECTION;
            }

            if (m_PatternIsReady)
                return ProjectionState.PATTERN_FORMED;
            m_PatternIsReady = prevPatternIsReady;

            if (m_ProjectionIsReady)
                return ProjectionState.PROJECTION_FORMED;

            m_ProjectionIsReady = prevProjectionIsReady;

            if (prevPatternIsReady)
                return ProjectionState.PATTERN_SAME;

            return prevProjectionIsReady 
                ? ProjectionState.PROJECTION_SAME 
                : ProjectionState.NO_PROJECTION;
        }

        internal bool IsBull { get; }
        internal double LengthAtoX { get; }
        
        internal GartleyPattern PatternType { get; }
        internal BarPoint ItemX { get; }
        internal BarPoint ItemA { get; }

        internal BarPoint ItemB
        {
            get => m_ItemB;
            set
            {
                m_ItemB = value;

                if (value == null)
                {
                    ItemC = null;
                    ItemD = null;
                }
                else if (!m_IsCd)
                {
                    UpdateAtoC();
                }
            }
        }

        internal BarPoint ItemBSecond { get; set; }

        internal BarPoint ItemC
        {
            get => m_ItemC;
            set
            {
                m_ItemC = value;
                if (value == null)
                {
                    m_RatioToBdLevelsMap = Array.Empty<RealLevel>();
                    m_XdToDbMapSortedItems?.Clear();
                    m_PatternIsReady = false;
                    m_ProjectionIsReady = false;
                    m_ItemDCancelPrice = double.NaN;
                    AtoC = 0;
                }
                else
                {
                    m_RatioToBdLevelsMap = InitPriceRanges(
                        PatternType.BDValues, true, Math.Abs(ItemB - ItemC), m_ItemC.Value);

                    UpdateXdToDbMaps();
                    m_ProjectionIsReady = true;
                }
            }
        }

        internal BarPoint ItemD { get; set; }
        internal double XtoD { get; set; }
        internal double AtoC { get; set; }
        internal double BtoD { get; set; }
        internal double XtoB { get; set; }
        internal double XtoBSecond { get; set; }
    }
}
