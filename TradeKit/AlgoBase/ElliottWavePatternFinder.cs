using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using TradeKit.Core;
using TradeKit.Impulse;
using static Plotly.NET.StyleParam;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Contains pattern-finding logic for the Elliott Waves structures.
    /// </summary>
    public class ElliottWavePatternFinder
    {
        private readonly IBarsProvider m_BarsProvider;

        private Dictionary<ElliottModelType, ModelRules> m_ModelRules;
        private readonly ExactExtremumFinder m_ExactExtremumFinder;

        /// <summary>
        /// Initializes a new instance of the <see cref="ElliottWavePatternFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="barsFactory">The factory for the bar providers.</param>
        public ElliottWavePatternFinder(
            IBarsProvider barsProvider, BarProvidersFactory barsFactory)
        {
            m_BarsProvider = barsProvider;
                barsFactory.GetBarsProvider(
                    TimeFrameHelper.GetPreviousTimeFrameInfo(barsProvider.TimeFrame).TimeFrame);

            m_ExactExtremumFinder = new ExactExtremumFinder(barsProvider, barsFactory);
            //InitModelRules();
        }

        #region EA model rules

        private bool CheckImpulseRulesPoints(List<BarPoint> barPoints)
        {
            // We should have all impulse points
            if (barPoints.Count != 6)
            {
                return false;
            }

            BarPoint wave0 = barPoints[0];
            BarPoint wave5 = barPoints[5];

            bool isUp = wave0 < wave5;
            int k = isUp ? 1 : -1;
            BarPoint wave1 = barPoints[1];
            BarPoint wave2 = barPoints[2];
            BarPoint wave3 = barPoints[3];
            BarPoint wave4 = barPoints[4];

            TimeSpan wave1Dur = wave1.OpenTime - wave0.OpenTime;
            TimeSpan wave2Dur = wave2.OpenTime - wave1.OpenTime;
            TimeSpan wave3Dur = wave3.OpenTime - wave2.OpenTime;
            TimeSpan wave4Dur = wave4.OpenTime - wave3.OpenTime;
            TimeSpan wave5Dur = wave5.OpenTime - wave4.OpenTime;

            if (wave1Dur<= TimeSpan.Zero ||
                wave2Dur <= TimeSpan.Zero ||
                wave3Dur <= TimeSpan.Zero ||
                wave4Dur <= TimeSpan.Zero ||
                wave5Dur <= TimeSpan.Zero)
                return false;

            double durationRatio = wave4Dur.TotalSeconds / wave2Dur.TotalSeconds;
            if (durationRatio < 0.75 || durationRatio > 5)
                return false;

            double wave1Len = k * (wave1 - wave0);
            double wave3Len = k * (wave3 - wave2);
            double wave5Len = k * (wave5 - wave4);

            if (wave1Len <= 0 || wave3Len <= 0 || wave5Len <= 0)
                return false;

            bool orderRule = k * (wave4 - wave1) > 0;
            bool lengthRule = wave3Len > wave1Len || wave1Len > wave5Len;

            return orderRule && lengthRule;
        }

        private bool CheckSmoothImpulse(
            Dictionary<int, ValueTuple<int, double>> points, double fullLength)
        {
            double[] pullbacks = points.Select(a => a.Value.Item2).ToArray();
            //double pullbackAvg = pullbacks.Average();
            double pullbackMax = pullbacks.Max();
            //double pullbackSum = pullbacks.Sum(a => Math.Pow(a - pullbackAvg, 2));
            //double standardDeviation = Math.Sqrt(pullbackSum / (pullbacks.Length - 1));
            if (pullbackMax / fullLength <= 0.3/* && standardDeviation >= 0.9*/)
                return true;

            return false;
        }

        private ElliottModelResult CheckImpulseRules(List<BarPoint> barPoints)
        {
            if (barPoints.Count < 2)
                return null;

            if (barPoints.Count == 2 || CheckImpulseRulesPoints(barPoints))
                return new ElliottModelResult(
                    ElliottModelType.IMPULSE, barPoints.ToArray(), null);

            BarPoint wave0 = barPoints[0];
            BarPoint wave5 = barPoints[^1];

            double lengthImpulse = Math.Abs(wave5 - wave0);
            if (lengthImpulse < double.Epsilon)
                return null;

            bool isUp = wave0 < wave5;
            int bpCount = barPoints.Count;
            var overlaps = new Dictionary<int, ValueTuple<int, double>>();

            for (int i = 0; i < bpCount; i++)
            {
                var localOverlap = new List<ValueTuple<int, double>>();
                for (int j = i + 1; j < bpCount; j++)
                {
                    double diff = barPoints[i].Value - barPoints[j].Value;
                    diff = isUp ? diff : -diff;

                    if (diff > 0)
                        localOverlap.Add(
                            new ValueTuple<int, double>(j, diff));
                }

                if (localOverlap.Count == 0)
                    overlaps[i] = new ValueTuple<int, double>(0, 0);
                else
                    overlaps[i] = localOverlap.MaxBy(a => a.Item2);
            }

            var sortedOverlapse = new SortedDictionary<int, double>();
            foreach (KeyValuePair<int, (int, double)> pair in overlaps)
                sortedOverlapse.Add(pair.Key, pair.Value.Item2);
            
            //if (CheckSmoothImpulse(overlaps, lengthImpulse))
            //{
            //    return new ElliottModelResult(
            //        ElliottModelType.IMPULSE, new[] {wave0, wave5}, null);
            //}
            
            int[] maxKeys = Helper.FindGroups(sortedOverlapse)
                .Select(a => a.MaxBy(b => overlaps[b].Item2))
                .OrderByDescending(a => overlaps[a].Item2)
                .ToArray();

            BarPoint[] barPointsArray = barPoints.ToArray();
            foreach (int maxKey in maxKeys)
            {
               (int, double) overlap = overlaps[maxKey];

               int thirdWaveEndIndex = maxKey;
               BarPoint thirdWaveEnd = barPointsArray[thirdWaveEndIndex];
                // This is either the 3rd wave end index (main scenario) or
                // end of wave B of a flat/trangle or wave X of a combination
                // of the 4th wave.

                int forthWaveEndIndex = overlap.Item1;
                BarPoint forthWaveEnd = barPointsArray[forthWaveEndIndex];
                // We should consider 4th wave as zigzag and this will be the end index,
                // and a triangle or combination - in this case this will be the lowest (the highest) point index inside the 4th wave.

                foreach (int leftKey in maxKeys.Where(a => a < thirdWaveEndIndex))
                {
                    // Either the 1st wave end index, or wave B of a flat or wave X of a combination of the 2nd wave
                    int firstWaveEndIndex = leftKey;
                    BarPoint firstWaveEnd = barPointsArray[firstWaveEndIndex];

                    BarPoint[] wave2NdKeys = 
                        barPointsArray[firstWaveEndIndex..thirdWaveEndIndex];

                    // A running flat or a combination with a triangle in the wave Y won't be
                    // covered by it
                    BarPoint wave2End = isUp
                        ? wave2NdKeys.MinBy(a => a.Value)
                        : wave2NdKeys.MaxBy(a => a.Value);
                    
                    BarPoint[] wave1StKeys =
                        barPointsArray[..(firstWaveEndIndex-1)];
                    if (wave1StKeys.Length == 0)
                        continue;
                    
                    if (isUp && wave1StKeys.MaxBy(a => a.Value)?.Value > firstWaveEnd.Value || !isUp && wave1StKeys.MinBy(a => a.Value)?.Value < firstWaveEnd.Value)
                        continue; // TODO do the same for the wave 3

                    var impulseCandidate = new List<BarPoint>
                    {
                        wave0, firstWaveEnd, wave2End, thirdWaveEnd, forthWaveEnd, wave5
                    };

                    if (CheckImpulseRulesPoints(impulseCandidate))
                    {
                        return new ElliottModelResult(
                            ElliottModelType.IMPULSE, impulseCandidate.ToArray(), null);
                    }

                    // Add recursive call here
                    //// Check the inner structures
                    //Dictionary<string, ElliottModelType[]> rules =
                    //    m_ModelRules[ElliottModelType.IMPULSE].Models;
                    //foreach (KeyValuePair<string, ElliottModelType[]> rule in rules)
                    //{

                    //}

                }
            }

            return null;
        }

        private ElliottModelResult CheckInitialDiagonalRules(List<BarPoint> barPoints)
        {
            return null;
        }

        private ElliottModelResult CheckEndingDiagonalRules(List<BarPoint> barPoints)
        {
            return null;
        }

        private ElliottModelResult CheckZigzagRules(List<BarPoint> barPoints)
        {
            return null;
        }

        private ElliottModelResult CheckDoubleZigzagRules(List<BarPoint> barPoints)
        {
            return null;
        }

        private ElliottModelResult CheckCombinationRules(List<BarPoint> barPoints)
        {
            return null;
        }

        private ElliottModelResult CheckTriangleRules(List<BarPoint> barPoints)
        {
            return null;
        }

        private ElliottModelResult CheckRunningTriangleRules(List<BarPoint> barPoints)
        {
            return null;
        }

        private ElliottModelResult CheckExtendedFlatRules(List<BarPoint> barPoints)
        {
            return null;
        }

        private ElliottModelResult CheckRunningFlatRules(List<BarPoint> barPoints)
        {
            return null;
        }
        private void InitModelRules()
        {
            m_ModelRules = new Dictionary<ElliottModelType, ModelRules>
            {
                {
                    ElliottModelType.IMPULSE, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                "1", new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_INITIAL
                                }
                            },
                            {
                                "2", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING
                                }
                            },
                            {
                                "3", new[] {ElliottModelType.IMPULSE}
                            },
                            {
                                "4", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING
                                }
                            },
                            {
                                "5", new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_ENDING
                                }
                            },
                        },
                        CheckImpulseRules)
                },
                {
                    ElliottModelType.DIAGONAL_INITIAL, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                "1", new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_INITIAL,
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                            {
                                "2", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "3", new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                            {
                                "4", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "5", new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_ENDING,
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                        },
                        CheckInitialDiagonalRules)
                },
                {
                    ElliottModelType.DIAGONAL_ENDING, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                "1", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                            {
                                "2", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "3", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                            {
                                "4", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "5", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                        },
                        CheckEndingDiagonalRules)
                },
                {
                    ElliottModelType.ZIGZAG, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                "a", new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_INITIAL,
                                }
                            },
                            {
                                "b", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING
                                }
                            },
                            {
                                "c", new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_ENDING,
                                }
                            },
                        },
                        CheckZigzagRules)
                },
                {
                    ElliottModelType.DOUBLE_ZIGZAG, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                "w", new[]
                                {
                                    ElliottModelType.ZIGZAG
                                }
                            },
                            {
                                "x", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING
                                }
                            },
                            {
                                "y", new[]
                                {
                                    ElliottModelType.ZIGZAG
                                }
                            },
                        },
                        CheckDoubleZigzagRules)
                },
                {
                    ElliottModelType.COMBINATION, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                "w", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                }
                            },
                            {
                                "x", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING
                                }
                            },
                            {
                                "y", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING
                                }
                            },
                        },
                        CheckCombinationRules)
                },
                {
                    ElliottModelType.TRIANGLE_CONTRACTING, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                "a", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "b", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "c", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "d", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "e", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            }
                        },
                        CheckTriangleRules)
                },
                {
                    ElliottModelType.FLAT_EXTENDED, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                "a", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "b", new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                "c", new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_ENDING
                                }
                            }
                        },
                        CheckExtendedFlatRules)
                }
            };

            m_ModelRules[ElliottModelType.TRIANGLE_RUNNING] = m_ModelRules[ElliottModelType.TRIANGLE_CONTRACTING] with
            {
                GetElliottModelResult = CheckRunningTriangleRules
            };

            m_ModelRules[ElliottModelType.FLAT_RUNNING] = m_ModelRules[ElliottModelType.FLAT_EXTENDED] with
            {
                GetElliottModelResult = CheckRunningFlatRules
            };
        }

        #endregion

        private List<BarPoint> GetKeyBarPoints(List<Candle> candles, bool isUp)
        {
            if (candles.Count == 0)
                return null;

            if (candles[0].O < candles[^1].C != isUp)
                return null;

            var bars = new List<BarPoint>();
            void AddBp(BarPoint bp)
            {
                bars.Add(bp);
            }

            for (int i = 0; i < candles.Count; i++)
            {
                Candle c = candles[i];

                if (!c.Index.HasValue)
                    return null;
                int chartIndex = c.Index.Value;

                BarPoint Bp(double v) => new(v, chartIndex, m_BarsProvider);

                bool isNotFirst = i > 0;
                bool isNotLast = i < candles.Count - 1;
                if (isNotFirst) AddBp(Bp(c.O));

                if (isNotFirst || !isUp) AddBp(Bp(c.H));
                if (isNotLast || !isUp) AddBp(Bp(c.L));

                if (isNotLast) AddBp(Bp(c.C));
            }

            return bars;
        }

        /// <summary>
        /// Determines whether the interval between the dates is an impulse.
        /// </summary>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        /// <param name="result">The impulse waves found or null if not found.</param>
        /// <returns>
        ///   <c>true</c> if the interval is impulse; otherwise, <c>false</c>.
        /// </returns>
        public bool IsImpulse(BarPoint start, BarPoint end, out ElliottModelResult result)
        {
            result = null;
            var barsCount = end.BarIndex - start.BarIndex;
            var period = barsCount;
            if (period < Helper.PIVOT_PERIOD_MIN)
            {
                result = null;
                return false;
            }
            
            bool isImpulseUp = start.Value < end.Value;
            m_ExactExtremumFinder.Reset();
            m_ExactExtremumFinder.Calculate(start.BarIndex, end.BarIndex);
            SortedDictionary<DateTime, BarPoint> extremaDict = m_ExactExtremumFinder.Extrema;

            if (isImpulseUp && (!extremaDict.ContainsKey(start.OpenTime) ||
                                !extremaDict.ContainsKey(end.OpenTime)) ||
                !isImpulseUp && (!extremaDict.ContainsKey(start.OpenTime) ||
                                 !extremaDict.ContainsKey(end.OpenTime)))
            {
                return false;
            }

            List<BarPoint> extremaList = m_ExactExtremumFinder.ToExtremaList();
            if (extremaList.Count > 0)
            {
                BarPoint lastExtremum = extremaList[^1];
                if (lastExtremum.BarIndex == end.BarIndex &&
                    Math.Abs(lastExtremum.Value - end.Value) > double.Epsilon)
                {
                    extremaList.Remove(lastExtremum);
                    extremaList.Add(new BarPoint(end.Value, end.BarIndex, m_BarsProvider));
                }
            }


            result = CheckImpulseRules(extremaList);
            return result != null;
        }
    }
}
