using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.SharpZipLib;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    internal class GartleyPatternFinder
    {
        private readonly double m_ShadowAllowanceRatio;
        private readonly IBarsProvider m_BarsProvider;
        private const int GARTLEY_EXTREMA_COUNT = 6;
        private const double SL_RATIO = 0.27;
        private const double TP1_RATIO = 0.382;
        private const double TP2_RATIO = 0.618;
        private const int GARTLEY_EXTREMA_X_B_LAST_INDEX = 2;// 0-X, 1-A, 2-B

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
            1.27,
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
        /// <param name="shadowAllowance">The correction allowance percent.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="patterns">Patterns supported.</param>
        public GartleyPatternFinder(
            double shadowAllowance, 
            IBarsProvider barsProvider, 
            HashSet<GartleyPatternType> patterns = null)
        {
            if (shadowAllowance is < 0 or > 100)
                throw new ValueOutOfRangeException(
                    $"{nameof(shadowAllowance)} should be between 0 and 100");

            m_ShadowAllowanceRatio = shadowAllowance / 100 + 1;
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
            for (int i = endIndex - 1; i >= startIndex; i--)
            {
                double lMax = m_BarsProvider.GetHighPrice(i);
                double lMin = m_BarsProvider.GetLowPrice(i);

                if (pointD == null)
                {
                    isBull = lMax > max;
                    bool isBear = lMin < min;

                    if (isBull && isBear)
                    {
                        Logger.Write("Candle is too big for this scale");
                        return null;
                    }

                    if (!isBull && !isBear)
                    {
                        continue;
                    }

                    pointD = new BarPoint(isBull ? min : max,
                        m_BarsProvider.GetOpenTime(endIndex), m_BarsProvider.TimeFrame, endIndex);
                }
                
                double cValue;
                if (isBull)
                {
                    if (lMin < pointD)
                    {
                        Logger.Write($"Min ({lMin}) < D ({pointD.Value})");
                        return null;
                    }

                    cValue = lMax;
                }
                else
                {
                    if (lMax > pointD)
                    {
                        Logger.Write($"Min ({lMax}) > D ({pointD.Value})");
                        return null;
                    }

                    cValue = lMin;
                }

                var pointC = new BarPoint(cValue, 
                    m_BarsProvider.GetOpenTime(i), m_BarsProvider.TimeFrame, i);
                List<GartleyItem> patternsIn = FindPatternAgainstC(pointD, pointC, isBull);
                if (patternsIn != null)
                {
                    //check shadow against level

                    patterns ??= new HashSet<GartleyItem>(new GartleyItemComparer());
                    foreach (GartleyItem patternIn in patternsIn)
                    {
                        if (patterns.Add(patternIn)) continue;
                        Logger.Write("Got the same Gartley pattern, ignore it");
                    }
                }
            }

            return patterns;
        }

        private List<GartleyItem> FindPatternAgainstC(
            BarPoint pointD, BarPoint pointC, bool isBull)
        {
            double valCtoD = Math.Abs(pointC.Value - pointD.Value);
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
                double[] pointsA = new double[pattern.XDValues.Length];
                for (int i = 0; i < pattern.XDValues.Length; i++)
                {
                    double varXtoD = pattern.XDValues[i];
                    double ratio = valCtoD / varXtoD;
                    double varX = varC + ratio * (isBull ? -1 : 1);
                    pointsX[i] = varX;
                    double varA = varX + ratio * (isBull ? 1 : -1);
                    pointsA[i] = varA;
                }
                
                for (int i = pointC.BarIndex - 1; i >= 0; i--)
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

                    List<BarPoint> bExtrema = null;
                    foreach (double pointB in pointsB)
                    {
                        if (isBull)
                        {
                            if (lMin <= pointB && lMin >= pointB / m_ShadowAllowanceRatio)
                            {
                                bExtrema ??= new List<BarPoint>();
                                bExtrema.Add(new BarPoint(lMin,
                                        m_BarsProvider.GetOpenTime(i),
                                        m_BarsProvider.TimeFrame, i));
                                // Got good B point
                            }
                        }
                        else
                        {
                            if (lMax >= pointB && lMin <= pointB * m_ShadowAllowanceRatio)
                            {
                                bExtrema ??= new List<BarPoint>();
                                bExtrema.Add(new BarPoint(lMax,
                                    m_BarsProvider.GetOpenTime(i),
                                    m_BarsProvider.TimeFrame, i));
                                // Got good B point
                            }
                        }
                    }

                    if (bExtrema == null)
                    {
                        // No B points were found
                        continue;
                    }
                    
                    foreach (BarPoint pointB in bExtrema)
                    {
                        for (int j = pointB.BarIndex - 1; j >= 0; j--)
                        {
                            List<BarPoint> aExtrema = null;
                            double aMax = m_BarsProvider.GetHighPrice(j);
                            double aMin = m_BarsProvider.GetLowPrice(j);

                            foreach (double pointA in pointsA)
                            {
                                if (isBull)
                                {
                                    if (aMax >= pointA && aMin <= pointA * m_ShadowAllowanceRatio)
                                    {
                                        aExtrema ??= new List<BarPoint>();
                                        aExtrema.Add(new BarPoint(aMax,
                                            m_BarsProvider.GetOpenTime(j),
                                            m_BarsProvider.TimeFrame, j));
                                        // Got good A point
                                    }

                                    continue;
                                }

                                if (aMin <= pointA && aMin >= pointA / m_ShadowAllowanceRatio)
                                {
                                    aExtrema ??= new List<BarPoint>();
                                    aExtrema.Add(new BarPoint(aMin,
                                        m_BarsProvider.GetOpenTime(j),
                                        m_BarsProvider.TimeFrame, j));
                                    // Got good A point
                                }
                            }

                            if (aExtrema == null)
                            {
                                // No A points were found
                                continue;
                            }

                            foreach (BarPoint pointA in aExtrema)
                            {
                                for (int k = pointA.BarIndex - 1; k >= 0; k--)
                                {
                                    List<BarPoint> xExtrema = null;
                                    double xMax = m_BarsProvider.GetHighPrice(k);
                                    double xMin = m_BarsProvider.GetLowPrice(k);

                                    foreach (double pointX in pointsX)
                                    {
                                        if (isBull)
                                        {
                                            if (xMin <= pointX && xMin >= pointX / m_ShadowAllowanceRatio)
                                            {
                                                xExtrema ??= new List<BarPoint>();
                                                xExtrema.Add(new BarPoint(xMin,
                                                    m_BarsProvider.GetOpenTime(k),
                                                    m_BarsProvider.TimeFrame, k));
                                                // Got good X point
                                            }

                                            continue;
                                        }

                                        if (xMax >= pointX && xMax <= pointX * m_ShadowAllowanceRatio)
                                        {
                                            xExtrema ??= new List<BarPoint>();
                                            xExtrema.Add(new BarPoint(xMax,
                                                m_BarsProvider.GetOpenTime(k),
                                                m_BarsProvider.TimeFrame, k));
                                            // Got good X point
                                        }
                                    }

                                    if (xExtrema == null)
                                    {
                                        // No X points were found
                                        continue;
                                    }

                                    foreach (BarPoint pointX in xExtrema)
                                    {
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

            if (xA > 0 || aB > 0 || cB > 0 || cD > 0)
                return null;

            double xB = xA / aB;
            double xD = cD / xA;
            double bD = cD / cB;
            double aC = xC / xA;

            double valAc = pattern.ACValues.FirstOrDefault(
                acVal => aC / acVal < m_ShadowAllowanceRatio);
            double valBd = pattern.BDValues.FirstOrDefault(
                bdVal => bD / bdVal < m_ShadowAllowanceRatio);
            double valXd = pattern.XDValues.FirstOrDefault(
                xdVal => xD / xdVal < m_ShadowAllowanceRatio);

            if (valAc == 0 || valBd == 0 || valXd == 0)
                return null;

            double valXb = 0;
            if (pattern.XBValues.Length > 0)
            {
                valXb = pattern.XBValues.FirstOrDefault(
                    xbVal => xB / xbVal < m_ShadowAllowanceRatio);

                if (valXb == 0)
                    return null;
            }

            double shadowD = m_BarsProvider.GetClosePrice(d.BarIndex);
            bool isBull = x < a;
            double dLevel = (isBull ? 1 : -1) * xA / xD + a;

            if (isBull && shadowD < dLevel || !isBull && shadowD > dLevel)
            {
                Logger.Write("Candle body doesn't fit."); // allowance?
                return null;
            }

            double slLen = cD * SL_RATIO;
            double tp1Len = cD * TP1_RATIO;
            double tp2Len = cD * TP2_RATIO;

            return new GartleyItem(
                pattern.PatternType,
                LevelItem.FromBarPoint(x),
                LevelItem.FromBarPoint(a),
                LevelItem.FromBarPoint(b),
                LevelItem.FromBarPoint(c),
                LevelItem.FromBarPoint(d),
                isBull ? -slLen + d : slLen + d,
                isBull ? tp1Len + d : -tp1Len + d,
                isBull ? tp2Len + d : -tp2Len + d,
                xD, valXd, aC, valAc, bD, valBd, xB, valXb);
        }
    }
}
