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

        private List<ICandle> GetImpulseSet(
            double startValue, double endValue, int barsCount)
        {
            ModelRules rules = ModelRules[ElliottModelType.IMPULSE];
            List<ICandle> candles = new List<ICandle>();

            double range = Math.Abs(endValue - startValue);
            bool isUp = startValue > endValue;
            int isUpK = isUp ? 1 : -1;
            double extendedWave = m_Random.NextDouble();
            bool is1StExtended = extendedWave < 0.1;
            bool is3rdExtended = extendedWave is >= 0.1 and <= 0.7;
            bool is5thExtended = extendedWave > 0.7;

            double firstEndLevelLimit = startValue + isUpK * range *
                (is1StExtended ? 0.6 : 0.25);
            double firstEndLevel = RandomWithinRange(startValue, firstEndLevelLimit);

            return candles;
        }
        
        private static readonly
            SortedDictionary<byte, double> MAP_EX_FLAT_WAVE_A_TO_C =
                new() { { 0, 0 }, { 20, 1.618 }, { 80, 2.618 }, { 95, 3.618 } };

        public List<ICandle> GetExtendedFlat(PatternArgsItem args, double bLimit)
        {
            if (args.IsUp && bLimit >= args.StartValue ||
                !args.IsUp && bLimit <= args.StartValue)
                throw new ArgumentException(nameof(bLimit));

            if (args.BarsCount <= 0)
                throw new ArgumentException(nameof(args.BarsCount));

            List<ICandle> candles = args.Candles;

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
                return candles;
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

                candles.Add(new JsonCandleExport
                {
                    O = c1.C,
                    L = args.IsUp ? midItem2 : args.EndValue,
                    H = args.IsUp ? args.EndValue : midItem2,
                    C = args.EndValue
                });
                return candles;
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

            List<ICandle> candlesWaveA = GetRandomSet(
                new PatternArgsItem(args.StartValue, waveA, bars4Gen[0]));

            List<ICandle> candlesWaveB = GetRandomSet(
                new PatternArgsItem(candlesWaveA[^1].C, waveB, bars4Gen[1], waveA));

            List<ICandle> candlesWaveC = GetRandomSet(
                new PatternArgsItem(candlesWaveB[^1].C, args.EndValue, bars4Gen[2], waveB));

            candles.AddRange(candlesWaveA);
            candles.AddRange(candlesWaveB);
            candles.AddRange(candlesWaveC);

            return candles;
        }

        public List<ICandle> GetRandomSet(PatternArgsItem args)
        {
            List<ICandle> candles = args.Candles;
            if (args.BarsCount <= 0)
                return candles;
            
            double previousClose = args.StartValue;
            double stepLinear = args.Range / args.BarsCount;

            for (int i = 0; i < args.BarsCount; i++)
            {
                double open = previousClose;
                double stepVal = args.StartValue + args.IsUpK * stepLinear * (i + 1);

                double peakRnd = m_Random.NextDouble();
                int peakDiv; 
                switch (peakRnd)
                {
                    case >= 0.995:// throwout
                        peakDiv = m_Random.Next(1, args.BarsCount);
                        break;
                    default:
                        peakDiv = 2 * i + 1;
                        break;
                }

                double maxAllowanceLength = Math.Abs(stepVal - args.Max) * m_Random.NextDouble() / peakDiv;
                double minAllowanceLength = Math.Abs(stepVal - args.Min) * m_Random.NextDouble() / peakDiv;

                double high = Math.Max(open, stepVal) + maxAllowanceLength;
                double low = Math.Min(open, stepVal) - minAllowanceLength;
                double close = RandomWithinRange(high, low);

                if (low> high) Logger.Write("low bigger then the high check");

                candles.Add(new JsonCandleExport
                {
                    C = Math.Round(close, args.Accuracy),
                    H = Math.Round(high, args.Accuracy),
                    O = Math.Round(open, args.Accuracy),
                    L = Math.Round(low, args.Accuracy)
                });

                previousClose = close;
            }

            var startItem = (JsonCandleExport)candles[0];
            var endItem = (JsonCandleExport)candles[^1];

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
                    .SkipWhile(a => a.Value > min)
                    .TakeWhile(a => a.Value >= max))
                .ToArray();

            if (selectedItems.Length == 0)
                return AddExtra(min, max);
            
            if (selectedItems[0].Key == 0)
                return AddExtra(min, max);

            byte randomNext = (byte)m_Random.Next(0, 100);
            KeyValuePair<byte, double>[] rndFoundItems = selectedItems
                .TakeWhile(a => a.Key <= randomNext)
                .ToArray();
            if (rndFoundItems.Length == 0)
                return AddExtra(min, max);

            double foundLevel = rndFoundItems[0].Value;
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
