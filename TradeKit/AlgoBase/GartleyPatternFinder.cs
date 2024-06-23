using Plotly.NET.TraceObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core;
using TradeKit.Gartley;

namespace TradeKit.AlgoBase
{
    public class GartleyPatternFinder
    {
        private readonly double m_WickAllowanceRatio;
        private readonly IBarsProvider m_BarsProvider;
        private readonly int m_BarsDepth;
//#if GARTLEY_PROD
        private const double SL_RATIO = 0.28;
        private const double TP1_RATIO = 0.37;

//#else
        //private const double SL_RATIO = 0.35;
        //private const double TP1_RATIO = 0.45;
//#endif 
        private const double TP2_RATIO = 0.618;
        private const double MAX_SL_TP_RATIO_ALLOWED = 2;

        private readonly GartleyPattern[] m_RealPatterns;
        private readonly SortedDictionary<DateTime, List<GartleyProjection>> m_ActiveProjections;
        private readonly SortedDictionary<DateTime, DateTime> m_BullXtoA;
        private readonly SortedDictionary<DateTime, DateTime> m_BearXtoA;
        private readonly HashSet<DateTime> m_BullWastedX;
        private readonly HashSet<DateTime> m_BearWastedX;
        private readonly SortedDictionary<DateTime, double> m_BullAMax;
        private readonly SortedDictionary<DateTime, double> m_BearAMin;

        private readonly PivotPointsFinder m_PivotPointsFinder;

        private const int MIN_PERIOD = 1;
        private DateTime? m_BorderDateTime;

        /// <summary>
        /// Initializes a new instance of the <see cref="GartleyPatternFinder"/> class.
        /// </summary>
        /// <param name="wickAllowance">The correction allowance percent.</param>
        /// <param name="barsProvider">The bar provider.</param>
        /// <param name="barsDepth">How many bars we should analyze backwards.</param>
        /// <param name="patterns">Patterns supported.</param>
        public GartleyPatternFinder(
            IBarsProvider barsProvider, 
            double wickAllowance,
            int barsDepth,
            HashSet<GartleyPatternType> patterns = null)
        {
            if (wickAllowance is < 0 or > 100)
                throw new IndexOutOfRangeException(
                    $"{nameof(wickAllowance)} should be between 0 and 100");

            m_PivotPointsFinder = new PivotPointsFinder(MIN_PERIOD, barsProvider, false);
            m_WickAllowanceRatio = wickAllowance / 100;
            m_BarsProvider = barsProvider;
            m_BarsDepth = barsDepth;
            m_RealPatterns = patterns == null
                ? GartleyProjection.PATTERNS
                : GartleyProjection.PATTERNS.Where(a => patterns.Contains(a.PatternType))
                    .ToArray();

            m_ActiveProjections = new SortedDictionary<DateTime, List<GartleyProjection>>();
            m_BullXtoA = new SortedDictionary<DateTime, DateTime>();
            m_BearXtoA = new SortedDictionary<DateTime, DateTime>();
            m_BullWastedX = new HashSet<DateTime>();
            m_BearWastedX = new HashSet<DateTime>();
            m_BullAMax = new SortedDictionary<DateTime, double>();
            m_BearAMin = new SortedDictionary<DateTime, double>();
        }

        private void InitDatesIfNeeded(int index)
        {
            int prevIndex = index - m_BarsDepth;
            m_BorderDateTime = prevIndex < 0
                ? m_BarsProvider.GetOpenTime(0)
                : m_BarsProvider.GetOpenTime(prevIndex);
        }

        private void UpdateProjectionsCache()
        {
            if (!m_BorderDateTime.HasValue) 
                return;

            m_ActiveProjections.RemoveLeft(a => a < m_BorderDateTime);
            m_BullXtoA.RemoveLeft(a => a < m_BorderDateTime);
            m_BearXtoA.RemoveLeft(a => a < m_BorderDateTime);
            m_BullWastedX.RemoveWhere(a => a < m_BorderDateTime);
            m_BearWastedX.RemoveWhere(a => a < m_BorderDateTime);
            m_BullAMax.RemoveLeft(a => a < m_BorderDateTime);
            m_BearAMin.RemoveLeft(a => a < m_BorderDateTime);
        }

        private void UpdateWasted(int index)
        {
            double min = m_BarsProvider.GetLowPrice(index);
            double max = m_BarsProvider.GetHighPrice(index);

            foreach (DateTime bullXtoADate in m_BullXtoA.Keys)
            {
                if (m_BullWastedX.Contains(bullXtoADate))
                    continue;

                if (!m_PivotPointsFinder.LowValues.TryGetValue(bullXtoADate, out double value) ||
                    double.IsNaN(value))
                    continue;

                if (value > min)
                    m_BullWastedX.Add(bullXtoADate);
            }

            foreach (DateTime bearXtoADate in m_BearXtoA.Keys)
            {
                if (m_BearWastedX.Contains(bearXtoADate))
                    continue;

                if (!m_PivotPointsFinder.HighValues.TryGetValue(bearXtoADate, out double value) ||
                    double.IsNaN(value))
                    continue;

                if (value < max)
                    m_BearWastedX.Add(bearXtoADate);
            }
        }

        private void ProcessProjections(
            DateTime pointDateTimeX, 
            SortedDictionary<DateTime, double> values,
            SortedDictionary<DateTime, double> counterValues,
            bool isUp)
        {
            HashSet<DateTime> wastedValues = isUp
                ? m_BullWastedX
                : m_BearWastedX;

            if (wastedValues.Contains(pointDateTimeX))
                return;

            if (!values.TryGetValue(pointDateTimeX, out double valX))
                return;

            if (double.IsNaN(valX))
                return;

            SortedDictionary<DateTime, double> aRanges = isUp
                ? m_BullAMax
                : m_BearAMin;
            if (!aRanges.TryGetValue(pointDateTimeX, out double aExtrema))
            {
                aExtrema = valX;
                aRanges[pointDateTimeX] = aExtrema;
            }

            SortedDictionary<DateTime, DateTime> processedValues = isUp 
                ? m_BullXtoA 
                : m_BearXtoA;

            processedValues.TryAdd(pointDateTimeX, pointDateTimeX);
            DateTime processedA = processedValues[pointDateTimeX];

            foreach (KeyValuePair<DateTime, double> pointA in counterValues
                         .SkipWhile(a => a.Key <= processedA))
            {
                if (pointA.Key > processedA)
                    processedValues[pointDateTimeX] = pointA.Key;

                if (double.IsNaN(pointA.Value))
                    continue;

                if (isUp && valX >= pointA.Value ||
                    !isUp && valX <= pointA.Value)
                {
                    wastedValues.Add(pointDateTimeX);
                    return;
                }

                if (values.SkipWhile(a => a.Key <= pointDateTimeX)
                    .TakeWhile(a => a.Key < pointA.Key)
                    .Any(a => isUp ? a.Value < valX : a.Value > valX))
                {
                    return; // other sides of the candles can also reach X point
                }

                if (isUp && aExtrema > pointA.Value ||
                    !isUp && aExtrema < pointA.Value)
                    continue;

                aExtrema = aRanges[pointDateTimeX] = pointA.Value;

                var aBarPoint = new BarPoint(pointA.Value, pointA.Key, m_BarsProvider);
                var xBarPoint = new BarPoint(valX, pointDateTimeX, m_BarsProvider);
                
                foreach (GartleyPattern realPattern in m_RealPatterns)
                {
                    var projection = new GartleyProjection(
                        m_BarsProvider,
                        realPattern.PatternType,
                        xBarPoint,
                        aBarPoint, 
                        m_WickAllowanceRatio);

                    m_ActiveProjections.AddValue(pointDateTimeX, projection);
                }
            }
        }
        
        /// <summary>
        /// Finds the gartley patterns or null if not found.
        /// </summary>
        /// <param name="index">The point we want to calculate against.</param>
        /// <returns>Gartley pattern or null</returns>
        public HashSet<GartleyItem> FindGartleyPatterns(int index)
        {
            m_PivotPointsFinder.Calculate(index);
            InitDatesIfNeeded(index);
            UpdateProjectionsCache();
            UpdateWasted(index);

            foreach (DateTime pointDateTimeX in
                     m_PivotPointsFinder.LowExtrema.SkipWhile(a => a < m_BorderDateTime))
            {
                ProcessProjections(pointDateTimeX,
                    values: m_PivotPointsFinder.LowValues,
                    counterValues: m_PivotPointsFinder.HighValues,
                    true);
            }

            foreach (DateTime pointDateTimeX in
                     m_PivotPointsFinder.HighExtrema.SkipWhile(a => a < m_BorderDateTime))
            {
                ProcessProjections(pointDateTimeX,
                    values: m_PivotPointsFinder.HighValues,
                    counterValues: m_PivotPointsFinder.LowValues,
                    false);
            }

            HashSet<GartleyItem> patterns = null;
            foreach (List<GartleyProjection> activeProjections in m_ActiveProjections.Values)
            {
                foreach (GartleyProjection activeProjection in activeProjections)
                {
                    ProjectionState updateResult = activeProjection.Update(index);
                    if (updateResult == ProjectionState.PROJECTION_FORMED)
                    {
                        // Here we can fire a projection event
                    }

                    if (updateResult != ProjectionState.PATTERN_FORMED)
                        continue;// Got only new patterns (not projections)

                    GartleyItem pattern = CreatePattern(activeProjection);
                    if (pattern == null)
                        continue;

                    patterns ??= new HashSet<GartleyItem>(new GartleyItemComparer());
                    patterns.Add(pattern);
                }
            }

            return patterns;
        }

        private bool HasExtremaBetweenPoints(BarPoint bp1, BarPoint bp2)
        {
            double max = Math.Max(bp1.Value, bp2.Value);
            double min = Math.Max(bp1.Value, bp2.Value);

            for (int i = bp2.BarIndex; i < bp2.BarIndex; i++)
            {
                if (max < m_BarsProvider.GetHighPrice(i) ||
                    min > m_BarsProvider.GetLowPrice(i))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasExtremaBetweenPoints(GartleyProjection projection)
        {
            bool result = HasExtremaBetweenPoints(projection.ItemX, projection.ItemA) ||
                          HasExtremaBetweenPoints(projection.ItemA, projection.ItemB) ||
                          HasExtremaBetweenPoints(projection.ItemB, projection.ItemC) ||
                          HasExtremaBetweenPoints(projection.ItemC, projection.ItemD);

            return result;
        }

        /// <summary>
        /// Creates the pattern if it is possible
        /// </summary>
        /// <param name="projection">The Gartley projection with ready pattern state</param>
        /// <returns><see cref="GartleyItem"/> if it is valid or null if it doesn't</returns>
        private GartleyItem CreatePattern(GartleyProjection projection)
        {
            if (projection.ItemX == null ||
                projection.ItemA == null ||
                projection.ItemB == null ||
                projection.ItemC == null ||
                projection.ItemD == null)
                return null;

            if (0d == projection.ItemX.Value || 
                0d == projection.ItemA.Value || 
                0d == projection.ItemB.Value || 
                0d == projection.ItemC.Value || 
                0d == projection.ItemD.Value)
                return null;

            double xA = Math.Abs(projection.ItemA - projection.ItemX);
            double aB = Math.Abs(projection.ItemB - projection.ItemA);
            double cB = Math.Abs(projection.ItemC - projection.ItemB);
            double cD = Math.Abs(projection.ItemC - projection.ItemD);
            double xC = Math.Abs(projection.ItemC - projection.ItemX);
            double aD = Math.Abs(projection.ItemA - projection.ItemD);

            if (xA <= 0 || aB <= 0 || cB <= 0 || cD <= 0 || aD <= 0)
                return null;

            //if (HasExtremaBetweenPoints(projection))
            //{
            //    Logger.Write($"{nameof(HasExtremaBetweenPoints)}: {projection.PatternType.PatternType}");
            //    return null;
            //}

            double xB = aB / xA;
            double xD = cD / xA;
            double bD = cD / aB;
            double aC = xC / xA;

            double accuracy = 
                GetRatio(projection.XtoD + projection.AtoC + projection.BtoD, xD + aC + bD);
            //if (accuracy < 0.95)
            //    return null;

            bool isBull = projection.IsBull;
            double closeD = m_BarsProvider.GetClosePrice(projection.ItemD.BarIndex);
            
            double actualSize = projection.PatternType.SetupType == GartleySetupType.AD ? aD : cD;

            double slLen = actualSize * SL_RATIO;
            double tp1Len = actualSize * TP1_RATIO;
            double sl = isBull ? -slLen + projection.ItemD : slLen + projection.ItemD;
            //double tp1Len = Math.Abs(sl - closeD);

            double tp1 = isBull ? tp1Len + projection.ItemD : -tp1Len + projection.ItemD;
            if (isBull && closeD - tp1 >= 0 || !isBull && closeD - tp1 <= 0)
            {
                //Logger.Write("TP is already hit.");
                return null;
            }

            double tp2Len = actualSize * TP2_RATIO;
            double tp2 = isBull ? tp2Len + projection.ItemD : -tp2Len + projection.ItemD;

            double def = Math.Abs(closeD - sl) / Math.Abs(closeD - tp1);
            if (def > MAX_SL_TP_RATIO_ALLOWED)
            {
                //Logger.Write("SL/TP is too big.");
                return null;
            }
            
            var item = new GartleyItem(Convert.ToInt32(accuracy * 100),
                projection.PatternType.PatternType,
                projection.ItemX,
                projection.ItemA,
                projection.ItemB,
                projection.ItemC,
                projection.ItemD,
                sl, tp1, tp2,
                xD, projection.XtoD,
                aC, projection.AtoC,
                bD, projection.BtoD,
                xB, projection.XtoB);
            return item;
        }

        private double GetRatio(double val1, double val2)
        {
            double min = Math.Min(val1, val2);
            double max = Math.Max(val1, val2);

            return min / max;
        }
    }
}
