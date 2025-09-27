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
        private readonly double m_TpRatio;
        private readonly double m_SlRatio;
        private double m_Min;
        private DateTime m_MinDate;
        private double m_Max;
        private DateTime m_MaxDate;
        private double m_ItemDCancelPrice = double.NaN;
        private double m_ItemECancelPrice = double.NaN;
        private readonly int m_IsUpK;
        private bool m_PatternIsReady;
        private bool m_ProjectionIsReady;
        private bool m_IsInvalid;
        private RealLevel[] m_RatioToAcLevelsMap;
        private readonly RealLevel[] m_RatioToXbLevelsMap;
        private readonly RealLevel[] m_RatioToXdLevelsMap;
        private RealLevel[] m_RatioToXeLevelsMap;
        private RealLevel[] m_RatioToBdLevelsMap;
        private readonly bool m_IsCd;
        private readonly bool m_HasE;

        private readonly List<RealLevelCombo> m_XdToDbMapSortedItems;
        private readonly List<RealLevel> m_CdToDeMapSortedItems;
        private BarPoint m_ItemC;
        private BarPoint m_ItemB;
        private BarPoint m_ItemD;

        private const double TP2_RATIO = 0.618;
        private const double MAX_SL_TP_RATIO_ALLOWED = 4;

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
                    BDValues: new[] {1.618},//LEVELS.RangeVal(1.13, 1.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886),
                    CEValues:Array.Empty<double>()),
                new(GartleyPatternType.BUTTERFLY,
                    XBValues: new[] {0.786},
                    XDValues: new[] {1.41},//LEVELS.RangeVal(1.27, 1.41),
                    BDValues: new[] {2.24},//LEVELS.RangeVal(1.618, 2.24),
                    ACValues: LEVELS.RangeVal(0.382, 0.886),
                    CEValues:Array.Empty<double>()),
                new(GartleyPatternType.SHARK,
                    XBValues: LEVELS.RangeVal(0.382, 0.618),
                    XDValues: new[] {1.13},//LEVELS.RangeVal(0.886, 1.13),
                    BDValues: new[] {2.24},//LEVELS.RangeVal(1.618, 2.24),
                    ACValues: LEVELS.RangeVal(1.13, 1.618),
                    CEValues:Array.Empty<double>()),
                new(GartleyPatternType.CRAB,
                    XBValues: LEVELS.RangeVal(0.382, 0.618),
                    XDValues: new[] {1.618},
                    BDValues: new[] {3.618},//LEVELS.RangeVal(2.618, 3.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886),
                    CEValues:Array.Empty<double>()),
                new(GartleyPatternType.DEEP_CRAB,
                    XBValues: new[] {0.886},
                    XDValues: new[] {1.618},
                    BDValues: new[] {3.618},//LEVELS.RangeVal(2, 3.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886),
                    CEValues:Array.Empty<double>()),
                new(GartleyPatternType.BAT,
                    XBValues: LEVELS.RangeVal(0.382, 0.5),
                    XDValues: new[] {0.886},
                    BDValues: new[] {2.618},//LEVELS.RangeVal(1.618, 2.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886),
                    CEValues:Array.Empty<double>()),
                new(GartleyPatternType.ALT_BAT,
                    XBValues: new[] {0.382},
                    XDValues: new[] {1.13},
                    BDValues: new[] {3.618},//LEVELS.RangeVal(2, 3.618),
                    ACValues: LEVELS.RangeVal(0.382, 0.886),
                    CEValues:Array.Empty<double>()),
                new(GartleyPatternType.CYPHER,
                    XBValues: LEVELS.RangeVal(0.382, 0.618),
                    XDValues: new[] {0.786},
                    BDValues: new[] {2d},//LEVELS.RangeVal(1.272, 2),
                    ACValues: LEVELS.RangeVal(1.13, 1.41),
                    SetupType: GartleySetupType.CD,
                    CEValues:Array.Empty<double>()),
               new(GartleyPatternType.FIVE_ZERO,
                   XBValues: Array.Empty<double>(),
                   XDValues: Array.Empty<double>(),
                   BDValues: LEVELS.RangeVal(1.618, 2.24),
                   ACValues: LEVELS.RangeVal(1.13, 1.618),
                   CEValues: new[] {0.5},
                   SetupType: GartleySetupType.CD),
                //new(GartleyPatternType.NEN_STAR,
                //    XBValues: LEVELS.RangeVal(0.382, 0.618),
                //    XDValues: new[] {1.272},
                //    BDValues: new[] {2d},//LEVELS.RangeVal(1.272, 2),
                //    ACValues: LEVELS.RangeVal(1.13, 1.41),
                //    SetupType: GartleySetupType.CD,
                //    CEValues:Array.Empty<double>()),
                //new(GartleyPatternType.LEONARDO,
                //    XBValues: new[] {0.5},
                //    XDValues: new[] {0.786},
                //    BDValues: new[] {2.618},//LEVELS.RangeVal(1.272, 2.618),
                //    ACValues: LEVELS.RangeVal(0.382, 0.886),
                    
                //    CEValues:Array.Empty<double>())
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

        private void UpdateDtoE()
        {
            if (ItemD == null || ItemC == null || !m_HasE)
                return;
            
            m_CdToDeMapSortedItems.Clear();
            
            double lengthCtoD = Math.Abs(ItemC - ItemD);
            m_RatioToXeLevelsMap = InitPriceRanges(
                PatternType.CEValues, false, lengthCtoD, ItemD.Value);

            foreach (RealLevel map in m_RatioToXeLevelsMap)
            {
                m_CdToDeMapSortedItems.Add(map);
            }

            m_ItemECancelPrice = IsBull
                ? m_RatioToXeLevelsMap.MaxBy(a => a.Max).Max
                : m_RatioToXeLevelsMap.MinBy(a => a.Min).Min;
        }
        
        public GartleyProjection(
            IBarsProvider barsProvider,
            GartleyPatternType patternType, 
            BarPoint itemX, 
            BarPoint itemA,
            double wickAllowanceZeroToOne,
            double tpRatio,
            double slRatio)
        {
            m_BarsProvider = barsProvider;
            m_WickAllowanceZeroToOne = wickAllowanceZeroToOne;
            m_TpRatio = tpRatio;
            m_SlRatio = slRatio;
            PatternType = PATTERNS_MAP[patternType];
            ItemX = itemX;
            ItemA = itemA;
            IsBull = itemX < itemA;
            LengthAtoX = Math.Abs(ItemA - ItemX);
            m_IsCd = PatternType.SetupType == GartleySetupType.CD;
            m_HasE = PatternType.CEValues.Length > 0;

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

            m_RatioToXdLevelsMap = PatternType.XDValues.Any()
                ? InitPriceRanges(
                    PatternType.XDValues, true, LengthAtoX, ItemA.Value)
                : Array.Empty<RealLevel>();

            //We cannot initialize BD/XD ranges until points B/C is calculated.
            m_XdToDbMapSortedItems = new List<RealLevelCombo>();
            m_CdToDeMapSortedItems = new List<RealLevel>();
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

            RealLevel xdLevel = null;
            if (m_RatioToXdLevelsMap.Length == 0)
            {
                double max = m_RatioToBdLevelsMap.MaxBy(a => a.Max).Max;
                double min = m_RatioToBdLevelsMap.MinBy(a => a.Min).Min;

                xdLevel = new RealLevel(0, IsBull ? max : min,
                    IsBull ? min : max);
            }

            foreach (RealLevel mapBd in m_RatioToBdLevelsMap.OrderByDescending(a => a.Ratio))
            {
                if (xdLevel != null)
                {
                    var toAdd = new RealLevelCombo(xdLevel, mapBd);
                    m_XdToDbMapSortedItems.Add(toAdd);
                    continue;
                }

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
            BarPoint targetPoint = m_HasE ? ItemE : ItemD;
            bool isBull = m_HasE ? !IsBull : IsBull;// Well, the 5-0 pattern has one extra point, and it was easier to calculate it like a Shark pattern and reverse the direction in the end.
            double closeLastCandle =
                m_BarsProvider.GetClosePrice((m_HasE ? ItemE : ItemD).BarIndex);
 
            double actualSize = PatternType.SetupType == GartleySetupType.AD ? aD : cD;

            double slLen = actualSize * m_SlRatio;
            double tp1Len = actualSize * m_TpRatio;
            sl = isBull ? -slLen + targetPoint : slLen + targetPoint;
            //double tp1Len = Math.Abs(sl - closeD);

            tp1 = isBull ? tp1Len + targetPoint : -tp1Len + targetPoint;
            if (isBull && closeLastCandle - tp1 >= 0 || !isBull && closeLastCandle - tp1 <= 0)
            {
                //Logger.Write("TP is already hit.");
                tp2 = double.NaN;
                return false;
            }

            double tp2Len = actualSize * TP2_RATIO;
            tp2 = isBull ? tp2Len + targetPoint : -tp2Len + targetPoint;

            double def = Math.Abs(closeLastCandle - sl) / Math.Abs(closeLastCandle - tp1);
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

        private void UpdateD(DateTime dt, double value)
        {
            List<RealLevel> levelsToDelete = null;
            void Remove(RealLevel levelRange)
            {
                levelsToDelete ??= new List<RealLevel>();
                levelsToDelete.Add(levelRange);
            }

            bool patternIsReady = false;
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
                if (!m_HasE && !IsPatternFitForTrade(out _, out _, out _))
                {
                    //ItemD = null;
                    continue;
                }
                
                XtoD = levelRangeCombo.Xd.Ratio;
                BtoD = levelRangeCombo.Bd.Ratio;
                patternIsReady = true;

                break;
            }

            if (levelsToDelete == null || !patternIsReady)
                return;

            if (!m_HasE)
            {
                m_PatternIsReady = true;
                m_ProjectionIsReady = false;// Stop using the projection when we got the whole pattern
            }

            foreach (RealLevel levelToDelete in levelsToDelete)
            {
                // If the level is used, we should remove the whole level from the combos collection
                m_XdToDbMapSortedItems.RemoveAll(
                    a => levelToDelete == a.Bd || levelToDelete == a.Xd);
            }
        }

        private void UpdateE(DateTime dt, double value)
        {
            if (!m_HasE || ItemD == null || ItemC == null)
                return;
            
            List<RealLevel> levelsToDelete = new List<RealLevel>();
            foreach (RealLevel level in
                     m_CdToDeMapSortedItems.OrderByDescending(a => a.Ratio))
            {
                if (IsBull)
                {
                    if (value < level.StartValue)
                        continue;
                    
                    if (value > level.EndValue)
                    {
                        levelsToDelete.Add(level);
                        continue;
                    }
                }
                else
                {
                    if (value > level.StartValue)
                        continue;
                    
                    if (value < level.EndValue)
                    {
                        levelsToDelete.Add(level);
                        continue;
                    }
                }

                ItemE = new BarPoint(value, dt, m_BarsProvider);
                CtoE = level.Ratio;
                m_PatternIsReady = true;
                m_ProjectionIsReady = false;

                break;
            }

            foreach (RealLevel item in levelsToDelete)
            {
                m_CdToDeMapSortedItems.Remove(item);
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
        /// Updates points of the current pattern projection.
        /// </summary>
        /// <param name="dt">The current dt.</param>
        /// <param name="value">The current value.</param>
        /// <param name="isHigh">if set to <c>true</c> if the value is a high extremum.</param>
        private void UpdatePoints(DateTime dt, double value, bool isHigh)
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

            if (isStraightExtrema)
                UpdateE(dt, value);
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

            //if the price squeezes through all the levels without a pattern
            if (!m_PatternIsReady)
            {
                if (!double.IsNaN(m_ItemDCancelPrice))
                {
                    if (IsBull && low < m_ItemDCancelPrice ||
                        !IsBull && high > m_ItemDCancelPrice)
                    {
                        m_IsInvalid = true;
                        return ProjectionState.NO_PROJECTION;
                    }
                }
                
                if (!double.IsNaN(m_ItemECancelPrice))
                {
                    if (IsBull && high > m_ItemECancelPrice ||
                        !IsBull && low < m_ItemECancelPrice)
                    {
                        m_IsInvalid = true;
                        return ProjectionState.NO_PROJECTION;
                    }
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

            if (m_HasE && ItemC != null)
            {
                if (IsBull && high > ItemC || !IsBull && low < ItemC)
                {
                    m_IsInvalid = true;
                    return ProjectionState.NO_PROJECTION;
                }
            }
            
            UpdatePoints(currentDt, high, true);
            UpdatePoints(currentDt, low, false);

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
                    ItemE = null;
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

                    if (!m_HasE)
                        m_ProjectionIsReady = true;
                }
            }
        }

        internal BarPoint ItemD
        {
            get => m_ItemD;
            set
            {
                m_ItemD = value;
                if (!m_HasE)
                    return;

                if (value == null)
                {
                    m_RatioToXeLevelsMap = Array.Empty<RealLevel>();
                    m_PatternIsReady = false;
                    m_ProjectionIsReady = false;
                    CtoE = 0;
                }
                else
                {
                    UpdateDtoE();
                    m_ProjectionIsReady = true;
                }
            }
        }
        
        internal BarPoint ItemE { get; set; }

        internal double XtoD { get; set; }
        internal double AtoC { get; set; }
        internal double CtoE { get; set; }
        internal double BtoD { get; set; }
        internal double XtoB { get; set; }
        internal double XtoBSecond { get; set; }
    }
}
