using cAlgo.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TradeKit.Core;
using TradeKit.Gartley;

namespace TradeKit.AlgoBase
{
    internal class GartleyPatternFinder
    {
        private class BorderPoint
        {
            private BarPoint m_BarPoint;
            internal DateTime? DatePoint { get; private set; }

            internal BarPoint BarPoint
            {
                get => m_BarPoint;
                set
                {
                    if (value == null)
                        return;

                    m_BarPoint = value;
                    DatePoint = value.OpenTime;
                }
            }

            internal void UpdateDate(DateTime date)
            {
                if (DatePoint != null && !(DatePoint < date)) return;
                DatePoint = date;
                BarPoint = null;
            }
        }

        private readonly double m_WickAllowanceRatio;
        private readonly IBarsProvider m_BarsProvider;
        private readonly int m_BarsDepth;
        private const int GARTLEY_EXTREMA_COUNT = 6;
        private const int PRE_X_EXTREMA_BARS_COUNT = 7;
//#if GARTLEY_PROD
        private const double SL_RATIO = 0.272;
        private const double TP1_RATIO = 0.382;

//#else
        //private const double SL_RATIO = 0.35;
        //private const double TP1_RATIO = 0.45;
//#endif 
        private const double TP2_RATIO = 0.618;
        private const double MAX_SL_TP_RATIO_ALLOWED = 2;

        private readonly GartleyPattern[] m_RealPatterns;
        private readonly SortedDictionary<DateTime, List<GartleyProjection>> m_ActiveProjections;

        private readonly PivotPointsFinder m_PivotPointsFinder;

        private const int MIN_PERIOD = 1;

        private readonly BorderPoint m_BorderPointAHigh = new();
        private readonly BorderPoint m_BorderPointALow = new();
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

            m_PivotPointsFinder = new PivotPointsFinder(MIN_PERIOD, barsProvider);
            m_WickAllowanceRatio = wickAllowance / 100;
            m_BarsProvider = barsProvider;
            m_BarsDepth = barsDepth;
            m_RealPatterns = patterns == null
                ? GartleyProjection.PATTERNS
                : GartleyProjection.PATTERNS.Where(a => patterns.Contains(a.PatternType))
                    .ToArray();

            m_ActiveProjections = new SortedDictionary<DateTime, List<GartleyProjection>>();
        }

        private void InitDatesIfNeeded(int startIndex)
        {
            DateTime currentDt = m_BarsProvider.GetOpenTime(startIndex);

            int prevIndex = startIndex - m_BarsDepth;
            m_BorderDateTime = prevIndex < 0
                ? currentDt.Add(-TimeFrameHelper.GetTimeFrameInfo(m_BarsProvider.TimeFrame).TimeSpan)
                : m_BarsProvider.GetOpenTime(prevIndex);
            m_BorderPointAHigh.UpdateDate(m_BorderDateTime.Value);
            m_BorderPointALow.UpdateDate(m_BorderDateTime.Value);
        }

        private void UpdateProjectionsCache()
        {
            if (!m_BorderDateTime.HasValue) 
                return;

            m_ActiveProjections.RemoveLeft(a => a < m_BorderDateTime);
        }

        private void UpdateReversed(int index)
        {
            double low = m_BarsProvider.GetHighPrice(index);
            m_PivotPointsFinder.LowValuesReversed.RemoveLeft(a => a <= low);

            double high = m_BarsProvider.GetHighPrice(index);
            m_PivotPointsFinder.HighValuesReversed.RemoveRight(a => a >= high);
        }

        private void ProcessProjections(
            DateTime pointDateTimeX, 
            BorderPoint border,
            SortedDictionary<DateTime, double> values,
            SortedDictionary<DateTime, double> counterValues,
            SortedDictionary<double, List<DateTime>> valuesReversed,
            bool isUp)
        {
            if (!values.TryGetValue(pointDateTimeX, out double valX) ||
                !valuesReversed.ContainsKey(valX)) return;

            BarPoint aBarPoint = null;
            foreach (KeyValuePair<DateTime, double> pointA in counterValues
                         // m_BorderPointAHigh.DatePoint always > m_BorderDateTime
                         .SkipWhile(a => a.Key <= border.DatePoint))
            {
                if (isUp ? valX >= pointA.Value : valX <= pointA.Value)
                    continue;

                aBarPoint = new BarPoint(pointA.Value, pointA.Key, m_BarsProvider);
                var xBarPoint = new BarPoint(valX, pointDateTimeX, m_BarsProvider);
                foreach (GartleyPattern realPattern in m_RealPatterns)
                {
                    var projection = new GartleyProjection(
                        m_BarsProvider,
                        m_PivotPointsFinder,
                        realPattern.PatternType,
                        xBarPoint,
                        aBarPoint, 
                        m_WickAllowanceRatio);

                    m_ActiveProjections.AddValue(pointDateTimeX, projection);
                }
            }

            if (aBarPoint != null)
                border.BarPoint = aBarPoint;
        }
        
        /// <summary>
        /// Finds the gartley patterns or null if not found.
        /// </summary>
        /// <param name="index">The point we want to start the search from.</param>
        /// <returns>Gartley pattern or null</returns>
        public HashSet<GartleyItem> FindGartleyPatterns(int index)
        {
            m_PivotPointsFinder.Calculate(index);
            InitDatesIfNeeded(index);
            UpdateProjectionsCache();

            if (!m_BorderPointAHigh.DatePoint.HasValue ||
                !m_BorderPointALow.DatePoint.HasValue)
                return null;

            foreach (DateTime pointDateTimeX in
                     m_PivotPointsFinder.AllExtrema.SkipWhile(a => a < m_BorderDateTime))
            {
                ProcessProjections(pointDateTimeX,
                    m_BorderPointAHigh,
                    m_PivotPointsFinder.LowValues,
                    m_PivotPointsFinder.HighValues,
                    m_PivotPointsFinder.LowValuesReversed,
                    true);

                ProcessProjections(pointDateTimeX,
                    m_BorderPointALow,
                    m_PivotPointsFinder.HighValues,
                    m_PivotPointsFinder.LowValues,
                    m_PivotPointsFinder.HighValuesReversed,
                    false);
            }
            
            UpdateReversed(index);

            HashSet<GartleyItem> patterns = null;
            foreach (List<GartleyProjection> activeProjections in m_ActiveProjections.Values)
            {
                foreach (GartleyProjection activeProjection in activeProjections)
                {
                    ProjectionState updateResult = activeProjection.Update();
                    if (updateResult == ProjectionState.PROJECTION_FORMED)
                    {
                        // Here we can fire a projection event
                    }

                    if (updateResult != ProjectionState.PATTERN_FORMED)
                        continue;// Got only new patterns (not projections)

                    patterns ??= new HashSet<GartleyItem>(new GartleyItemComparer());
                    patterns.Add(CreatePattern(activeProjection));
                }
            }

            return patterns;
        }
        
        /// <summary>
        /// Creates the pattern if it is possible
        /// </summary>
        /// <param name="projection">The Gartley projection with ready pattern state</param>
        /// <returns><see cref="GartleyItem"/> if it is valid or null if it doesn't</returns>
        private GartleyItem CreatePattern(GartleyProjection projection)
        {
            GartleyItem item = CreatePattern(projection.PatternType, 
                projection.ItemX, 
                projection.ItemA, 
                projection.ItemB,
                projection.ItemC, 
                projection.ItemD);
            return item;
        }

        /// <summary>
        /// Creates the pattern if it is possible
        /// </summary>
        /// <param name="pattern">The Gartley pattern</param>
        /// <param name="x">Point X</param>
        /// <param name="a">Point A</param>
        /// <param name="b">Point B</param>
        /// <param name="c">Point C</param>
        /// <param name="d">Point D</param>
        /// <returns><see cref="GartleyItem"/> if it is valid or null if it doesn't</returns>
        private GartleyItem CreatePattern(
            GartleyPattern pattern, BarPoint x, BarPoint a, BarPoint b, BarPoint c, BarPoint d)
        {
            if (0d == x || 0d == a || 0d == b || 0d == c || 0d == d)
                return null;

            double xA = Math.Abs(a - x);
            double aB = Math.Abs(b - a);
            double cB = Math.Abs(c - b);
            double cD = Math.Abs(c - d);
            double xC = Math.Abs(c - x);
            double aD = Math.Abs(a - d);

            if (xA <= 0 || aB <= 0 || cB <= 0 || cD <= 0 || aD <= 0)
                return null;

            double xB = aB / xA;
            double xD = cD / xC;
            double bD = cD / cB;
            double aC = xC / xA;

            var accuracyList = new List<double>();
            double FetchCloseValue(double[] values, double similarValue)
            {
                double fetched = (from val in values
                    let allowance = val * m_WickAllowanceRatio
                    where Math.Abs(similarValue - val) < allowance
                    select val).FirstOrDefault();

                if (similarValue != 0 && fetched != 0)
                {
                    double maxAccuracy = Math.Max(similarValue, fetched);
                    double minAccuracy = Math.Min(similarValue, fetched);
                    double accuracy = minAccuracy / maxAccuracy;
                    accuracyList.Add(accuracy);
                }

                return fetched;
            }

            double valAc = FetchCloseValue(pattern.ACValues, aC);
            if (valAc == 0)
                return null;

            double valBd = FetchCloseValue(pattern.BDValues, bD);
            if (valBd == 0)
                return null;

            double valXd = FetchCloseValue(pattern.XDValues, xD);
            if (valXd == 0)
                return null;

            double valXb = 0;
            if (pattern.XBValues.Length > 0)
            {
                valXb = FetchCloseValue(pattern.XBValues, xB);
                if (valXb == 0)
                    return null;
            }

            bool isBull = x < a;
            double closeD = m_BarsProvider.GetClosePrice(d.BarIndex);
            double dLevel = (isBull ? -1 : 1) * xA / xD + a;

            if (isBull && closeD < dLevel || !isBull && closeD > dLevel)
            {
                //Logger.Write("Candle body doesn't fit."); // allowance?
                return null;
            }

            double barLen = Math.Abs(m_BarsProvider.GetHighPrice(d.BarIndex) -
                                     m_BarsProvider.GetLowPrice(d.BarIndex));
            double wickAllowRange = barLen / 3;

            if (barLen <= 0)
                return null;

            if (isBull && closeD - d.Value < wickAllowRange ||
                !isBull && d.Value - closeD < wickAllowRange)
            {
                //Logger.Write("Candle body is too full.");
                return null;
            }

            double actualSize = pattern.SetupType == GartleySetupType.AD ? aD : cD;

            double slLen = actualSize * SL_RATIO;
            double tp1Len = actualSize * TP1_RATIO;
            double sl = isBull ? -slLen + d : slLen + d;
            //double tp1Len = Math.Abs(sl - closeD);

            double tp1 = isBull ? tp1Len + d : -tp1Len + d;
            if (isBull && closeD - tp1 >= 0 || !isBull && closeD - tp1 <= 0)
            {
                //Logger.Write("TP is already hit.");
                return null;
            }

            double tp2Len = actualSize * TP2_RATIO;
            double tp2 = isBull ? tp2Len + d : -tp2Len + d;

            double def = Math.Abs(closeD - sl)/ Math.Abs(closeD - tp1);
            if (def > MAX_SL_TP_RATIO_ALLOWED)
            {
                //Logger.Write("SL/TP is too big.");
                return null;
            }

            return new GartleyItem(
                Convert.ToInt32(accuracyList.Average() * 100),
                pattern.PatternType,
                x, a, b, c, d, sl, tp1, tp2, xD, valXd, aC, valAc, bD, valBd, xB, valXb);
        }
    }
}
