using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core;
using TradeKit.Gartley;

namespace TradeKit.AlgoBase
{
    internal class GartleyPatternFinder
    {
        private readonly double m_WickAllowanceRatio;
        private readonly IBarsProvider m_BarsProvider;
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

        static GartleyPatternFinder()
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
                    XDValues: LEVELS.RangeVal(1.27, 1.414),
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
                    XDValues: new[] {1.618},
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

        private static readonly GartleyPattern[] PATTERNS;
        private readonly GartleyPattern[] m_RealPatterns;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="GartleyPatternFinder"/> class.
        /// </summary>
        /// <param name="wickAllowance">The correction allowance percent.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="patterns">Patterns supported.</param>
        public GartleyPatternFinder(
            IBarsProvider barsProvider, 
            double wickAllowance, 
            HashSet<GartleyPatternType> patterns = null)
        {
            if (wickAllowance is < 0 or > 100)
                throw new IndexOutOfRangeException(
                    $"{nameof(wickAllowance)} should be between 0 and 100");

            m_WickAllowanceRatio = wickAllowance / 100;
            m_BarsProvider = barsProvider;
            m_RealPatterns = patterns == null
                ? PATTERNS
                : PATTERNS.Where(a => patterns.Contains(a.PatternType))
                    .ToArray();
        }

        /// <summary>
        /// Finds the gartley patterns or null if not found.
        /// </summary>
        /// <param name="startIndex">The point we want to start the search from.</param>
        /// <param name="endIndex">The point we want to end the search from.</param>
        /// <returns>Gartley pattern or null</returns>
        public HashSet<GartleyItem> FindGartleyPatterns(int startIndex, int endIndex)
        {
            if (endIndex - startIndex < GARTLEY_EXTREMA_COUNT)
                return null;

            double max = m_BarsProvider.GetHighPrice(endIndex);
            double min = m_BarsProvider.GetLowPrice(endIndex);
            bool isBull = false;
            
            BarPoint pointD = null;
            HashSet<GartleyItem> patterns = null;
            int nextDIndex = endIndex - 1;
            double cMax = m_BarsProvider.GetHighPrice(nextDIndex);
            double cMin = m_BarsProvider.GetLowPrice(nextDIndex);
            for (int i = nextDIndex; i > startIndex; i--)
            {
                double lMax = m_BarsProvider.GetHighPrice(i);
                double lMin = m_BarsProvider.GetLowPrice(i);

                if (pointD is null)
                {
                    isBull = lMax > max;
                    bool isBear = lMin < min;

                    if (isBull && isBear || !isBull && !isBear)
                    {
                        //Logger.Write("Candle is too big");
                        return null;
                    }

                    pointD = new BarPoint(isBull ? min : max, endIndex, m_BarsProvider);
                    continue;
                }

                double cValue;
                if (isBull)
                {
                    if (lMin < pointD)
                    {
                        //Logger.Write($"Min ({lMin}) < D ({pointD.Value})");
                        return patterns;
                    }

                    if (cMax > lMax)
                        continue;
                    cValue = lMax;
                }
                else
                {
                    if (lMax > pointD)
                    {
                        //Logger.Write($"Min ({lMax}) > D ({pointD.Value})");
                        return patterns;
                    }

                    if (cMin < lMin)
                        continue;
                    cValue = lMin;
                }

                var pointC = new BarPoint(cValue, i, m_BarsProvider);
                List<GartleyItem> patternsIn = FindPatternAgainstC(pointD, pointC, isBull, startIndex);
                if (patternsIn != null)
                {
                    patterns ??= new HashSet<GartleyItem>(new GartleyItemComparer());
                    foreach (GartleyItem patternIn in patternsIn)
                    {
                        if (patterns.Add(patternIn))
                        {
                            continue;
                        }

                        //Logger.Write("Got the same Gartley pattern, ignore it");
                    }
                }

                cMax = Math.Max(cMax, lMax);
                cMin = Math.Min(cMin, lMin);
            }

            return patterns;
        }

        private List<GartleyItem> FindPatternAgainstC(
            BarPoint pointD, BarPoint pointC, bool isBull, int startIndex)
        {
            if (pointC is null || pointD is null)
                return null;

            double valCtoD = Math.Abs(pointC.Value - pointD.Value);
            double allowance = valCtoD * m_WickAllowanceRatio;
            double varC = pointC.Value;
            List<GartleyItem> res = null;

            foreach (GartleyPattern pattern in m_RealPatterns)
            {
                double[] pointsB = new double[pattern.BDValues.Length];
                for (int i = 0; i < pattern.BDValues.Length; i++)
                {
                    double varBtoD = pattern.BDValues[i];
                    double ratio = valCtoD / varBtoD;
                    double varB = varC + ratio * (isBull ? -1 : 1);
                    pointsB[i] = varB;
                }
                
                double[] pointsX = new double[pattern.XDValues.Length];
                HashSet<double> pointsA = new HashSet<double>();
                for (int i = 0; i < pattern.XDValues.Length; i++)
                {
                    double varXtoD = pattern.XDValues[i];
                    double ratio = valCtoD / varXtoD;
                    double varX = varC + ratio * (isBull ? -1 : 1);
                    pointsX[i] = varX;

                    double valXtoC = Math.Abs(pointC.Value - varX);
                    foreach (double varAtoC in pattern.ACValues)
                    {
                        double ratioA = valXtoC / varAtoC;
                        double varA = varX + ratioA * (isBull ? 1 : -1);
                        pointsA.Add(varA);
                    }
                }

                int nextCIndex = pointC.BarIndex - 1;
                double bMax = m_BarsProvider.GetHighPrice(nextCIndex);
                double bMin = m_BarsProvider.GetLowPrice(nextCIndex);

                for (int i = nextCIndex; i >= startIndex; i--)
                {
                    double lMax = m_BarsProvider.GetHighPrice(i);
                    double lMin = m_BarsProvider.GetLowPrice(i);

                    bool isBullBreak = isBull && (lMax > pointC || lMin < pointD);
                    bool isBearishBreak = !isBull && (lMax > pointD || lMin < pointC);
                    if (isBullBreak || isBearishBreak)
                    {
                        // No B points are possible beyond this point
                        break;
                    }

                    HashSet<BarPoint> bExtrema = null;
                    foreach (double pointB in pointsB)
                    {
                        if (isBull)
                        {
                            if (lMin <= bMin && lMin <= pointB && lMin >= pointB - allowance)
                            {
                                bExtrema ??= new HashSet<BarPoint>();
                                bExtrema.Add(new BarPoint(lMin, i, m_BarsProvider));
                                // Got good B point
                            }
                        }
                        else if (lMax >= bMax && lMax >= pointB && lMin <= pointB + allowance)
                        {
                            bExtrema ??= new HashSet<BarPoint>();
                            bExtrema.Add(new BarPoint(lMax, i, m_BarsProvider));
                            // Got good B point
                        }
                    }
                    
                    bMax = Math.Max(bMax, lMax);
                    bMin = Math.Min(bMin, lMin);

                    if (bExtrema == null)
                    {
                        // No B points were found
                        continue;
                    }

                    double maxAPossible = pointsA.Max();
                    double minAPossible = pointsA.Min();

                    foreach (BarPoint pointB in bExtrema)
                    {
                        int nextBIndex = pointB.BarIndex - 1;
                        double aMax = m_BarsProvider.GetHighPrice(nextBIndex);
                        double aMin = m_BarsProvider.GetLowPrice(nextBIndex);
                        for (int j = nextBIndex; j >= 0; j--)
                        {
                            HashSet<BarPoint> aExtrema = null;
                            lMax = m_BarsProvider.GetHighPrice(j);
                            lMin = m_BarsProvider.GetLowPrice(j);

                            isBullBreak = isBull && 
                                          (lMax > maxAPossible + allowance || lMin < pointB);
                            isBearishBreak = !isBull && 
                                             (lMin < minAPossible - allowance || lMax > pointB);
                            if (isBullBreak || isBearishBreak)
                            {
                                // No A points are possible beyond this point
                                break;
                            }

                            foreach (double pointA in pointsA)
                            {
                                if (isBull)
                                {
                                    if (lMax >= aMax && lMax >= pointA && lMax <= pointA + allowance)
                                    {
                                        aExtrema ??= new HashSet<BarPoint>();
                                        aExtrema.Add(new BarPoint(lMax, j, m_BarsProvider));
                                        // Got good A point
                                    }
                                }
                                else if (lMin <= aMin && lMin <= pointA && lMin >= pointA - allowance)
                                {
                                    aExtrema ??= new HashSet<BarPoint>();
                                    aExtrema.Add(new BarPoint(lMin, j, m_BarsProvider));
                                    // Got good A point
                                }
                            }
                            
                            aMax = Math.Max(aMax, lMax);
                            aMin = Math.Min(aMin, lMin);

                            if (aExtrema == null)
                            {
                                // No A points were found
                                continue;
                            }

                            double maxXPossible = pointsX.Max();
                            double minXPossible = pointsX.Min();

                            foreach (BarPoint pointA in aExtrema)
                            {
                                int nextXIndex = pointA.BarIndex - 1;
                                double xMax = m_BarsProvider.GetHighPrice(nextXIndex);
                                double xMin = m_BarsProvider.GetLowPrice(nextXIndex);
                                for (int k = nextXIndex; k >= startIndex; k--)
                                {
                                    HashSet<BarPoint> xExtrema = null;
                                    lMax = m_BarsProvider.GetHighPrice(k);
                                    lMin = m_BarsProvider.GetLowPrice(k);

                                    isBullBreak = isBull &&
                                                  (lMin < minXPossible - allowance ||
                                                   lMax > pointA);
                                    isBearishBreak = !isBull &&
                                                     (lMax > maxXPossible + allowance ||
                                                      lMin < pointA);
                                    if (isBullBreak || isBearishBreak)
                                    {
                                        // No X points are possible beyond this point
                                        break;
                                    }

                                    foreach (double pointX in pointsX)
                                    {
                                        if (isBull)
                                        {
                                            if (lMin <= xMin && lMin <= pointX && lMin >= pointX - allowance)
                                            {
                                                xExtrema ??= new HashSet<BarPoint>();
                                                xExtrema.Add(new BarPoint(lMin, k, m_BarsProvider));
                                                // Got good X point
                                            }
                                        }
                                        else if (lMax >= xMax && lMax >= pointX && lMax <= pointX + allowance)
                                        {
                                            xExtrema ??= new HashSet<BarPoint>();
                                            xExtrema.Add(new BarPoint(lMax, k, m_BarsProvider));
                                            // Got good X point
                                        }
                                    }
                                    
                                    xMax = Math.Max(xMax, lMax);
                                    xMin = Math.Min(xMin, lMin);

                                    if (xExtrema == null)
                                    {
                                        // No X points were found
                                        continue;
                                    }
                                    
                                    foreach (BarPoint pointX in xExtrema)
                                    {
                                        bool xNotExtrema = false;
                                        for (int l = k;
                                             l >= Math.Max(k - PRE_X_EXTREMA_BARS_COUNT, 0);
                                             l--)
                                        {
                                            if (isBull && m_BarsProvider.GetLowPrice(l) < pointX ||
                                                !isBull && m_BarsProvider.GetHighPrice(l) > pointX)
                                            {
                                                xNotExtrema = true;
                                                break;
                                            }
                                        }
                                        
                                        if (xNotExtrema)
                                            continue;

                                        GartleyItem patternFound = CreatePattern(
                                            pattern, pointX, pointA, pointB, pointC, pointD);

                                        if (patternFound == null)
                                            continue;

                                        res ??= new List<GartleyItem>();
                                        res.Add(patternFound);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return res;
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

            double actualSize = Math.Max(cD, aD);

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
