using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core;
using TradeKit.Impulse;
using TradeKit.Json;

namespace TradeKit.PatternGeneration
{
    public class PatternGenerator
    {
        private readonly Random m_Random;

        public Dictionary<ElliottModelType, ModelRules> ModelRules
        { get; private set; }

        public PatternGenerator()
        {
            InitModelRules();
            m_Random = new Random();
        }

        private void InitModelRules()
        {
            ModelRules = new Dictionary<ElliottModelType, ModelRules>
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

            ModelRules[ElliottModelType.TRIANGLE_RUNNING] = ModelRules[ElliottModelType.TRIANGLE_CONTRACTING] with
            {
                GetElliottModelResult = CheckRunningTriangleRules
            };

            ModelRules[ElliottModelType.FLAT_RUNNING] = ModelRules[ElliottModelType.FLAT_EXTENDED] with
            {
                GetElliottModelResult = CheckRunningFlatRules
            };
        }

        private List<ICandle> GetImpulseSet(PatternArgsItem args)
        {
            double extendedWave = m_Random.NextDouble();
            bool is1StExtended = extendedWave < 0.1;
            //bool is3rdExtended = extendedWave is >= 0.1 and <= 0.7;
            //bool is5thExtended = extendedWave > 0.7;

            //double firstEndLevelLimit = startValue + isUpK * range *
            //    (is1StExtended ? 0.6 : 0.25);
            //double firstEndLevel = RandomWithinRange(startValue, firstEndLevelLimit);

            return args.Candles;
        }
        
        private static readonly
            SortedDictionary<byte, double> MAP_EX_FLAT_WAVE_A_TO_C =
                new() { { 0, 0 }, { 20, 1.618 }, { 80, 2.618 }, { 95, 3.618 } };

        public ModelPattern GetExtendedFlat(PatternArgsItem args, double bLimit)
        {
            if (args.IsUp && bLimit >= args.StartValue ||
                !args.IsUp && bLimit <= args.StartValue)
                throw new ArgumentException(nameof(bLimit));

            if (args.BarsCount <= 0)
                throw new ArgumentException(nameof(args.BarsCount));

            List<ICandle> candles = args.Candles;
            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_EXTENDED, candles);

            double waveALength = RandomWithinRange(
                args.Range * 0.3, args.Range * 0.95);
            // extended flat, wave A should make less progress that the wave C

            double waveA = args.StartValue + args.IsUpK * waveALength;
            double waveCLengthMax = Math.Abs(bLimit - args.EndValue);
            double waveCLengthMin = Math.Abs(args.StartValue - args.EndValue);
            double cMaxToA = waveCLengthMax / waveALength;
            double cMinToA = waveCLengthMin / waveALength;

            double waveCLength = SelectRandomly(
                MAP_EX_FLAT_WAVE_A_TO_C, cMinToA, cMaxToA) * waveALength;

            double waveB = args.EndValue - args.IsUpK * waveCLength;
            
            if (args.BarsCount == 1)
            {
                candles.Add(new JsonCandleExport
                {
                    O = args.StartValue,
                    L = args.IsUp ? waveB : args.EndValue,
                    H = args.IsUp ? args.EndValue : waveB,
                    C = args.EndValue,
                });

                modelPattern.PatternKeyPoints = new List<KeyValuePair<int, double>>
                {
                    new(0, waveA),
                    new(0, waveB),
                    new(0, args.EndValue),
                };

                return modelPattern;
            }
            
            if(args.BarsCount == 2)
            {
                var c1 = new JsonCandleExport
                {
                    O = args.StartValue
                };

                double midItem = RandomWithinRange(waveB, args.EndValue);

                c1.L = args.IsUp ? waveB : midItem;
                c1.H = args.IsUp ? midItem : waveB;
                c1.C = RandomWithinRange(c1.L, c1.H);
                double midItem2 = RandomWithinRange(midItem, args.EndValue);

                candles.Add(c1);
                candles.Add(new JsonCandleExport
                {
                    O = c1.C,
                    L = args.IsUp ? midItem2 : args.EndValue,
                    H = args.IsUp ? args.EndValue : midItem2,
                    C = args.EndValue
                });

                modelPattern.PatternKeyPoints = new List<KeyValuePair<int, double>>
                {
                    new(0, waveA),
                    new(0, waveB),
                    new(1, args.EndValue),
                };

                return modelPattern;
            }

            double rndSplitPart = m_Random.NextDouble() * 0.2 - 0.1;

            double barsA = 0.25 - rndSplitPart;
            double barsB = 0.5 + rndSplitPart;
            int[] bars4Gen = PatternGenKit.SplitNumber(
                args.BarsCount, new[]
                {
                    barsA,
                    barsB,
                    1 - barsA - barsB
                });

            List<ICandle> candlesWaveA = GetCorrectiveRandomSet(
                new PatternArgsItem(args.StartValue, waveA, bars4Gen[0]));

            List<ICandle> candlesWaveB = GetCorrectiveRandomSet(
                new PatternArgsItem(candlesWaveA[^1].C, waveB, bars4Gen[1], waveA));

            List<ICandle> candlesWaveC = GetImpulseRandomSet(
                new PatternArgsItem(candlesWaveB[^1].C, args.EndValue, bars4Gen[2], waveB));

            candles.AddRange(candlesWaveA);
            candles.AddRange(candlesWaveB);
            candles.AddRange(candlesWaveC);

            modelPattern.PatternKeyPoints = new List<KeyValuePair<int, double>>
            {
                new(candlesWaveA.Count - 1, waveA),
                new(candlesWaveA.Count - 1 + candlesWaveB.Count - 1, waveB),
                new(args.BarsCount - 1, args.EndValue)
            };

            return modelPattern;
        }

        public List<ICandle> GetSideRandomSet(
            PatternArgsItem args, double runningPrice, double correctivePrice)
        {
            args.Max = Math.Max(runningPrice, correctivePrice);
            args.Min = Math.Min(runningPrice, correctivePrice);

            return GetCorrectiveRandomSet(args);
        }

        public List<ICandle> GetImpulseRandomSet(PatternArgsItem args)
        {
            return GetRandomSet(args, 0.5);
        }

        public List<ICandle> GetCorrectiveRandomSet(PatternArgsItem args)
        {
            return GetRandomSet(args, m_Random.Next(2, 5), true);
        }

        public List<ICandle> GetRandomSet(
            PatternArgsItem args, double variance = 1, bool useFullRange = false)
        {
            List<ICandle> candles = args.Candles;
            if (args.BarsCount <= 0 && variance <= 0)
                return candles;

            double previousClose = args.StartValue;
            double stepLinear = args.Range / args.BarsCount;

            for (int i = 0; i < args.BarsCount; i++)
            {
                double open = previousClose;
                double meanPrice = args.StartValue +
                                   args.IsUpK * stepLinear * (i + 1);

                double varianceK = variance * stepLinear;
                double stepValMax;
                double stepValMin;

                if (useFullRange || variance > 1 && m_Random.NextDouble() >= 0.95) // throwout
                {
                    stepValMax = args.Max;
                    stepValMin = args.Min;
                }
                else
                {
                    stepValMax = Math.Min(Math.Max(meanPrice, open) + varianceK, args.Max);
                    stepValMin = Math.Max(Math.Min(meanPrice, open) - varianceK, args.Min);
                }

                double high = RandomWithinRange(open, stepValMax);
                double low = RandomWithinRange(open, stepValMin);
                double close = RandomWithinRange(high, low);

                if (low > high) Logger.Write("low bigger then the high check");

                candles.Add(new JsonCandleExport
                {
                    C = Math.Round(close, args.Accuracy),
                    H = Math.Round(high, args.Accuracy),
                    O = Math.Round(open, args.Accuracy),
                    L = Math.Round(low, args.Accuracy)
                });

                previousClose = close;
            }

            var startItem = (JsonCandleExport) candles[0];
            var endItem = (JsonCandleExport) candles[^1];

            if (args.IsUp)
            {
                startItem.L = args.PrevCandleExtremum.HasValue
                    ? RandomWithinRange(
                        args.PrevCandleExtremum.Value, args.StartValue)
                    : args.StartValue;
                startItem.O = args.PrevCandleExtremum.HasValue
                    ? args.StartValue
                    : RandomWithinRange(startItem.L, startItem.H);
                endItem.H = args.EndValue;
                endItem.C = RandomWithinRange(endItem.L, endItem.H);
            }
            else
            {
                startItem.H = args.PrevCandleExtremum.HasValue
                    ? RandomWithinRange(
                        args.PrevCandleExtremum.Value, args.StartValue)
                    : args.StartValue;
                startItem.O = args.PrevCandleExtremum.HasValue
                    ? args.StartValue
                    : RandomWithinRange(startItem.L, startItem.H);
                endItem.L = args.EndValue;
                endItem.C = RandomWithinRange(endItem.L, endItem.H);
            }

            return candles;
        }

        private double AddExtra(double value, double max)
        {
            if (value > max)
                throw new ArgumentException(nameof(max));

            // The real waves don't end exactly on fibo levels,
            // so we want to emulate this
            double rndExtra = value + m_Random.NextDouble() * value * 0.05;

            if (rndExtra <= max)
                return rndExtra;

            return value + m_Random.NextDouble() * (max - value);
        }

        private double SelectRandomly(
            SortedDictionary<byte, double> valuesMap, double min, double max)
        {
            KeyValuePair<byte, double>[] selectedItems = valuesMap
                .Where(a => a.Key == 0)
                .Concat(valuesMap.Where(a => a.Key > 0)
                    .SkipWhile(a => a.Value < min)
                    .TakeWhile(a => a.Value <= max))
                .ToArray();

            if (selectedItems.Length == 0)
                return AddExtra(min, max);
            
            if (selectedItems[^1].Key == 0)
                return AddExtra(min, max);

            byte randomNext = (byte)m_Random.Next(0, 100);
            KeyValuePair<byte, double>[] rndFoundItems = selectedItems
                .TakeWhile(a => a.Key <= randomNext)
                .ToArray();
            if (rndFoundItems.Length == 0)
                return AddExtra(min, max);

            double foundLevel = rndFoundItems[^1].Value;
            return AddExtra(foundLevel, max);
        }

        private double RandomWithinRange(double one, double two)
        {
            double min = Math.Min(one, two);
            double max = Math.Max(one, two);

            return min + m_Random.NextDouble() * (max - min);
        }

        #region CheckRules

        private ElliottModelResult CheckImpulseRules(List<BarPoint> barPoints)
        {
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

        #endregion
    }
}
