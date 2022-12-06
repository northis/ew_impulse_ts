using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    internal class GartleyPatternFinder
    {
        private readonly double m_ShadowAllowance;
        private readonly IBarsProvider m_BarsProvider;
        private readonly int m_Scale;
        private const int GARTLEY_EXTREMA_COUNT = 6;
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
        /// <param name="scale">Scale for extrema</param>
        /// <param name="patterns">Patterns supported</param>
        public GartleyPatternFinder(
            double shadowAllowance, 
            IBarsProvider barsProvider, 
            int scale,
            HashSet<GartleyPatternType> patterns = null)
        {
            m_ShadowAllowance = shadowAllowance;
            m_BarsProvider = barsProvider;
            m_Scale = scale;
            m_RealPatterns = patterns == null
                ? PATTERNS
                : PATTERNS.Where(a => patterns.Contains(a.PatternType))
                    .ToArray();
        }

        /// <summary>
        /// Finds the gartley patterns or null if not found.
        /// </summary>
        /// <param name="start">The point we want to start the search from.</param>
        /// <param name="end">The point we want to end the search from.</param>
        /// <param name="scale">The deviation (scale) of extrema to use.</param>
        /// <returns>Gartley pattern or null</returns>
        public List<GartleyItem> FindGartleyPatterns(DateTime start, DateTime end, int scale)
        {
            ExtremumFinder extremaFinder = new (scale, m_BarsProvider);
            extremaFinder.Calculate(start, end);
            KeyValuePair<int, BarPoint>[] extrema = extremaFinder.Extrema.ToArray();
            if (extrema.Length < GARTLEY_EXTREMA_COUNT)
                return null;

            int lastIndex = m_BarsProvider.GetIndexByTime(end);
            double max = m_BarsProvider.GetHighPrice(lastIndex);
            double min = m_BarsProvider.GetLowPrice(lastIndex);

            int indexOffset = 1;

            //TODO check earlier extrema too, use depth to limit this search
            BarPoint lastExtremum = extrema[^indexOffset].Value;
            BarPoint pointD = null;
            if (lastExtremum.OpenTime == end)
            {
                indexOffset++;
                lastExtremum = extrema[^indexOffset].Value;
                pointD = lastExtremum;
            }

            bool isBull = max < lastExtremum.Value && min < lastExtremum.Value;
            bool isBear = max > lastExtremum.Value && min > lastExtremum.Value;

            if (isBull && isBear || !isBull && !isBear)
            {
                Logger.Write("Candle is too big for this scale");
                return null;
            }

            if (pointD == null)
            {
                pointD = new BarPoint
                {
                    BarTimeFrame = m_BarsProvider.TimeFrame,
                    OpenTime = end,
                    Value = isBull ? min : max
                };
            }

            HashSet<GartleyItem> patterns = null;
            for (int i = extrema.Length - indexOffset; i < GARTLEY_EXTREMA_X_B_LAST_INDEX; i--)
            {
                pointD = lastExtremum;
                double shadowD = m_BarsProvider.GetClosePrice(i);

                List<GartleyItem> patternsIn =
                    FindPatternAgainstC(extrema, indexOffset, i, isBull);
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

                lastExtremum = extrema[^i].Value;
            }


            return patterns?.ToList();
        }

        private List<GartleyItem> FindPatternAgainstC(
            KeyValuePair<int, BarPoint>[] extrema, int indexD, int indexC, bool isBull)
        {
            BarPoint pointC = extrema[indexC].Value;
            BarPoint pointD = extrema[indexD].Value;
            double valCtoD = Math.Abs(pointC.Value - pointD.Value);
            double varC = pointC.Value;

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

                double bcIntervalExtrema = pointC.Value;// B-C overlap

                for (int i = indexC - 1; i < 0; i--)
                {
                    BarPoint extremum = extrema[i].Value;
                    int barIndex = extrema[i].Key;
                    double extremumVal = extremum.Value;


                    List<double> bExtrema = null;
                    foreach (double pointB in pointsB)
                    {
                        if (isBull)
                        {
                            if (extremumVal < pointB)
                            {
                                continue;
                            }

                            double maxBody = m_BarsProvider.GetMaxBodyPrice(barIndex);
                            if (maxBody > pointB)
                            {
                                continue;
                            }
                            // check if the previous extrema overlap this one

                            bExtrema ??= new List<double>();
                            bExtrema.Add(pointB);
                            // Got good B point
                        }
                        else
                        {
                            if (extremumVal > pointB)
                            {
                                continue;
                            }

                            double minBody = m_BarsProvider.GetMinBodyPrice(barIndex);
                            if (minBody < pointB)
                            {
                                continue;
                            }

                            bExtrema ??= new List<double>();
                            bExtrema.Add(pointB);
                            // Got good B point
                        }
                    }


                }

                //if (isBull && varB >= varC || !isBull && varB <= varC)
                //{
                //    // check extrema until B exceeds CD range
                //    continue;
                //}
                
            }

            return null;
        }
    }
}
