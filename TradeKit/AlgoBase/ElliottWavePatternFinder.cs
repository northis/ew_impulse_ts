using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using cAlgo.API;
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
        private readonly int m_ZoomMin;
        private readonly double m_CorrectionAllowancePercent;
        private readonly IBarsProvider m_BarsProvider;
        private readonly IBarsProvider m_BarsProviderMinor;
        private readonly BarProvidersFactory m_BarsFactory;

        private Dictionary<ElliottModelType, ModelRules> m_ModelRules;
        private readonly PivotPointsFinder m_PivotPointsFinder;

        /// <summary>
        /// Initializes a new instance of the <see cref="ElliottWavePatternFinder"/> class.
        /// </summary>
        /// <param name="correctionAllowancePercent">The correction allowance percent.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="barsFactory">The factory for the bar providers.</param>
        /// <param name="zoomMin">The zoom minimum.</param>
        public ElliottWavePatternFinder(
            double correctionAllowancePercent, 
            IBarsProvider barsProvider,
            BarProvidersFactory barsFactory, 
            int zoomMin)//InitIsHighFirst
        {
            m_ZoomMin = zoomMin;
            m_CorrectionAllowancePercent = correctionAllowancePercent;
            m_BarsProvider = barsProvider;
            m_BarsFactory = barsFactory;
            m_BarsProviderMinor =
                barsFactory.GetBarsProvider(
                    TimeFrameHelper.GetPreviousTimeFrameInfo(barsProvider.TimeFrame).TimeFrame);

            m_PivotPointsFinder = new PivotPointsFinder(Helper.PIVOT_PERIOD, barsProvider);
            //InitModelRules();
        }

        #region EA model rules, may be I will use this one day

        private ElliottModelResult CheckImpulseRules2(List<BarPoint> barPoints)
        {
            if (barPoints.Count <= 2)
                return new ElliottModelResult(ElliottModelType.IMPULSE, barPoints.ToArray(), null);

            return null;
        }


        private ElliottModelResult CheckImpulseRules(List<BarPoint> barPoints)
        {
            if (barPoints.Count <= 2)
                return new ElliottModelResult(ElliottModelType.IMPULSE, barPoints.ToArray(), null);
            
            bool isUp = barPoints[0] < barPoints[^1];
            //Logger.Write($"Is Up {isUp}");
            int bpCount = barPoints.Count;
            var overlaps = new Dictionary<int, ValueTuple<int, double>>();

            for (int i = 0; i < bpCount; i++)
            {
                var localOverlap = new List<ValueTuple<int, double>>();
                for (int j = i + 1; j < bpCount; j++)
                    localOverlap.Add(new ValueTuple<int,double>(j,barPoints[i].Value - barPoints[j].Value));

                if (localOverlap.Count == 0)
                    overlaps[i] = new ValueTuple<int, double>(0, 0);
                else
                    overlaps[i] = isUp? localOverlap.MaxBy(a => a.Item2): localOverlap.MinBy(a => a.Item2);

                //Logger.Write($"{i:D3}\t{barPoints[i].OpenTime:s}\t{barPoints[i].Value.ToString(CultureInfo.InvariantCulture)}\t{overlaps[i].ToString("F5", CultureInfo.InvariantCulture)}");
            }

            var firstIndices = new List<int>();
            var secondIndices = new List<int>();
            var thirdIndices = new List<int>();
            var fourIndices = new List<int>();

            int FindExtrema(IEnumerable<KeyValuePair<int, (int, double)>> o)
            {
                KeyValuePair<int, (int, double)> m = o.MaxBy(a => a.Value.Item2);
                int[] extremumIndices1 = { m.Value.Item1, m.Key };

                int extremumImpulseIndex = isUp
                    ? extremumIndices1.MaxBy(a => barPoints[a].Value)
                    : extremumIndices1.MinBy(a => barPoints[a].Value);

                int extremumRetraceIndex = isUp
                    ? extremumIndices1.MinBy(a => barPoints[a].Value)
                    : extremumIndices1.MaxBy(a => barPoints[a].Value);

                fourIndices.Add(extremumRetraceIndex);
                secondIndices.Add(extremumRetraceIndex);
                firstIndices.Add(extremumImpulseIndex);
                thirdIndices.Add(extremumImpulseIndex);

                return extremumImpulseIndex;
            }

            int extremumImpulseIndex1 = FindExtrema(overlaps);
            FindExtrema(overlaps.Take(extremumImpulseIndex1));

            if (overlaps.Count > extremumImpulseIndex1 + 1)
                FindExtrema(overlaps.Skip(extremumImpulseIndex1 + 1));

            var impulseCandidates = new List<(int, int, int, int)>();

            BarPoint bpStart = barPoints[0];
            BarPoint bpEnd = barPoints[^1];
            void CheckImpulse(ValueTuple<int, int, int, int> val)
            {
                int k = isUp ? 1 : -1;
                double l1 = (barPoints[val.Item1] - bpStart) * k;
                double l3 = (barPoints[val.Item3] - barPoints[val.Item2]) * k;
                double l5 = (bpEnd.Value - barPoints[val.Item4]) * k;

                if (l1 <= 0 || l3 <= 0 || l5 <= 0)
                    return;

                if (l3 < l1 && l3 < l5)
                    return;

                // We don't handle triangle/flat case or reduced impulses yet.
                if (k * (barPoints[val.Item4] - barPoints[val.Item1]) <= 0)
                    return;

                // Check the inner structures
                Dictionary<string, ElliottModelType[]> rules =
                    m_ModelRules[ElliottModelType.IMPULSE].Models;
                foreach (KeyValuePair<string, ElliottModelType[]> rule in rules)
                {

                }

                impulseCandidates.Add(val);
            }

            foreach (int thirdIndex in thirdIndices)
            {
                foreach (int secondIndex in secondIndices)
                {
                    if(secondIndex>= thirdIndex)
                        continue;

                    foreach (int firstIndex in firstIndices)
                    {
                        if (firstIndex >= secondIndex)
                            continue;

                        foreach (int fourIndex in fourIndices)
                        {
                            if (fourIndex <= thirdIndex)
                                continue;

                            CheckImpulse(new ValueTuple<int, int, int, int>(
                                firstIndex, secondIndex, thirdIndex, fourIndex));
                        }
                    }
                }
            }

            //Debugger.Launch();
            foreach ((int, int, int, int) impulseCandidate in impulseCandidates)
            {
                var res = new ElliottModelResult(ElliottModelType.IMPULSE, new[]
                {
                    bpStart,
                    barPoints[impulseCandidate.Item1],
                    barPoints[impulseCandidate.Item2],
                    barPoints[impulseCandidate.Item3],
                    barPoints[impulseCandidate.Item4],
                    bpEnd
                }, null);
                // We somehow should select from more then one option.
                return res;
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

            result = new ElliottModelResult(ElliottModelType.IMPULSE, new[] {start, end}, null);
            return true;

            bool isImpulseUp = start.Value < end.Value;
            int p = Helper.PIVOT_PERIOD_MIN;
            m_PivotPointsFinder.Reset(p);
            m_PivotPointsFinder.Calculate(start.BarIndex - p, end.BarIndex + p);

            if (isImpulseUp && (m_PivotPointsFinder.GetLowValue(start.OpenTime) == null ||
                                m_PivotPointsFinder.GetHighValue(end.OpenTime) == null) ||
                !isImpulseUp && (m_PivotPointsFinder.GetHighValue(start.OpenTime) == null ||
                                 m_PivotPointsFinder.GetLowValue(end.OpenTime) == null))
            {
                return false;
            }

            Dictionary<DateTime, double> highValues = m_PivotPointsFinder.HighValues
                .Where(a => a.Key >= start.OpenTime && 
                            a.Key <= end.OpenTime && !double.IsNaN(a.Value))
                .ToDictionary(a => a.Key, a => a.Value);
            Dictionary<DateTime, double> lowValues = m_PivotPointsFinder.LowValues
                .Where(a => a.Key >= start.OpenTime && 
                            a.Key <= end.OpenTime && !double.IsNaN(a.Value))
                .ToDictionary(a => a.Key, a => a.Value);

            var mergedDates = new HashSet<DateTime>();
            foreach (KeyValuePair<DateTime, double> highValue in highValues)
            {
                mergedDates.Add(highValue.Key);
            }

            var collisionLows = new Dictionary<DateTime, double>();
            foreach (KeyValuePair<DateTime, double> lowValue in lowValues)
            {
                if (!mergedDates.Contains(lowValue.Key))
                {
                    mergedDates.Add(lowValue.Key);
                    continue;
                }

                //a collision
                DateTime startKey = lowValue.Key;
                DateTime endKey = startKey.Add(
                    TimeFrameHelper.GetTimeFrameInfo(m_BarsProvider.TimeFrame).TimeSpan);

                highValues.Remove(lowValue.Key);
                mergedDates.Remove(lowValue.Key);
                KeyValuePair<DateTime, double> low =
                    m_BarsProviderMinor.GetLowPrice(startKey, endKey);
                KeyValuePair<DateTime, double> high =
                    m_BarsProviderMinor.GetHighPrice(startKey, endKey);
                highValues.Add(high.Key, high.Value);
                collisionLows.Add(low.Key, low.Value);
                mergedDates.Add(high.Key);

                if (low.Key != high.Key)
                    mergedDates.Add(low.Key);
            }

            foreach (KeyValuePair<DateTime, double> colLow in collisionLows)
            {
                if (lowValues.ContainsKey(colLow.Key))
                    continue;

                lowValues.Add(colLow.Key, colLow.Value);
            }

            bool direction = !isImpulseUp;
            // We want to remove the same direction pivot points in a row
            List<BarPoint> extrema = new List<BarPoint>();
            DateTime[] dates = mergedDates.OrderBy(a => a).ToArray();

            foreach (DateTime mergedDate in dates)
            {
                if (direction) // We expect next high
                {
                    if (!highValues.ContainsKey(mergedDate))
                    {
                        // we should restore the missed low
                        if (!lowValues.ContainsKey(mergedDate))
                        {
                            Logger.Write("Low is not found, rewrite this");
                            return false;
                        }

                        extrema.Add(new BarPoint(
                            lowValues[mergedDate], mergedDate,
                            m_BarsProvider));
                        continue;
                    }

                    extrema.Add(new BarPoint(
                        highValues[mergedDate], mergedDate,
                        m_BarsProvider));
                    direction = false;
                    continue;
                }

                // We expect next low
                if (!lowValues.ContainsKey(mergedDate))
                {
                    // we should restore the missed high
                    if (!highValues.ContainsKey(mergedDate))
                    {
                        Logger.Write("High is not found, rewrite this");
                        return false;
                    }

                    // TODO Refactor this loop
                    extrema.Add(new BarPoint(
                        highValues[mergedDate], mergedDate,
                        m_BarsProvider));
                    continue;
                }

                extrema.Add(new BarPoint(
                    lowValues[mergedDate], mergedDate,
                    m_BarsProvider));
                direction = true;
            }
            
            result = CheckImpulseRules2(extrema);
            return result != null;
        }
    }
}
