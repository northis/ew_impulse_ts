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
        private const int SIMPLE_BARS_THRESHOLD = 10;
        private const double MAIN_ALLOWANCE_MAX_RATIO = 0.05;

        public const string IMPULSE_ONE = "1";
        public const string IMPULSE_TWO = "2";
        public const string IMPULSE_THREE = "3";
        public const string IMPULSE_FOUR = "4";
        public const string IMPULSE_FIVE = "5";
        
        public const string CORRECTION_A = "a";
        public const string CORRECTION_B = "b";
        public const string CORRECTION_C = "c";
        public const string CORRECTION_D = "d";
        public const string CORRECTION_E = "e";
        public const string CORRECTION_W = "w";
        public const string CORRECTION_X = "x";
        public const string CORRECTION_Y = "y";
        public const string CORRECTION_XX = "xx";
        public const string CORRECTION_Z = "z";

        public Dictionary<ElliottModelType, ModelRules> ModelRules { get; private set; }

        private Dictionary<ElliottModelType, Func<PatternArgsItem, ModelPattern>> m_ModelGeneratorsMap;

        private HashSet<ElliottModelType> m_ShallowCorrections;
        private HashSet<ElliottModelType> m_DeepCorrections;
        private HashSet<ElliottModelType> m_DiagonalImpulses;
        private HashSet<ElliottModelType> m_RunningCorrections;

        private HashSet<ElliottModelType> m_Wave1Impulse;
        private HashSet<ElliottModelType> m_Wave2Impulse;
        private HashSet<ElliottModelType> m_Wave4Impulse;
        private HashSet<ElliottModelType> m_Wave5Impulse;

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
                                IMPULSE_ONE, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_INITIAL
                                }
                            },
                            {
                                IMPULSE_TWO, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING
                                }
                            },
                            {
                                IMPULSE_THREE, new[] {ElliottModelType.IMPULSE}
                            },
                            {
                                IMPULSE_FOUR, new[]
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
                                IMPULSE_FIVE, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_ENDING
                                }
                            },
                        })
                },
                {
                    ElliottModelType.DIAGONAL_INITIAL, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                IMPULSE_ONE, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_INITIAL,
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                            {
                                IMPULSE_TWO, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                IMPULSE_THREE, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                            {
                                IMPULSE_FOUR, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                IMPULSE_FIVE, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_ENDING,
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                        })
                },
                {
                    ElliottModelType.DIAGONAL_ENDING, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                IMPULSE_ONE, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                            {
                                IMPULSE_TWO, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                IMPULSE_THREE, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                            {
                                IMPULSE_FOUR, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                IMPULSE_FIVE, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                        })
                },
                {
                    ElliottModelType.ZIGZAG, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                CORRECTION_A, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_INITIAL,
                                }
                            },
                            {
                                CORRECTION_B, new[]
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
                                CORRECTION_C, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_ENDING,
                                }
                            },
                        })
                },
                {
                    ElliottModelType.DOUBLE_ZIGZAG, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                CORRECTION_W, new[]
                                {
                                    ElliottModelType.ZIGZAG
                                }
                            },
                            {
                                CORRECTION_X, new[]
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
                                CORRECTION_Y, new[]
                                {
                                    ElliottModelType.ZIGZAG
                                }
                            },
                        })
                },
                {
                    ElliottModelType.COMBINATION, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                CORRECTION_W, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                }
                            },
                            {
                                CORRECTION_X, new[]
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
                                CORRECTION_Y, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING
                                }
                            },
                        })
                },
                {
                    ElliottModelType.TRIANGLE_CONTRACTING, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                CORRECTION_A, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                CORRECTION_B, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                CORRECTION_C, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                CORRECTION_D, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                CORRECTION_E, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            }
                        })
                },
                {
                    ElliottModelType.FLAT_EXTENDED, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                CORRECTION_A, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                CORRECTION_B, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG
                                }
                            },
                            {
                                CORRECTION_C, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_ENDING
                                }
                            }
                        })
                }
            };

            m_ModelGeneratorsMap = new Dictionary<ElliottModelType, 
                Func<PatternArgsItem, ModelPattern>>
            {
                {ElliottModelType.IMPULSE, GetImpulse},
                {ElliottModelType.DIAGONAL_INITIAL, GetInitialDiagonal},
                {ElliottModelType.DIAGONAL_ENDING, GetEndingDiagonal},
                {ElliottModelType.DIAGONAL_EXPANDING, GetExpandingDiagonal},
                {ElliottModelType.TRIANGLE_CONTRACTING, GetContractingTriangle},
                {ElliottModelType.TRIANGLE_EXPANDING, GetExpandingTriangle},
                {ElliottModelType.TRIANGLE_RUNNING, GetRunningTriangle},
                {ElliottModelType.ZIGZAG, GetZigzag},
                {ElliottModelType.DOUBLE_ZIGZAG, GetDoubleZigzag},
                {ElliottModelType.TRIPLE_ZIGZAG, GetTripleZigzag},
                {ElliottModelType.FLAT_REGULAR, GetRegularFlat},
                {ElliottModelType.FLAT_RUNNING, GetRunningFlat},
                {ElliottModelType.FLAT_EXTENDED, GetExtendedFlat},
                {ElliottModelType.COMBINATION, GetCombination}
            };

            m_ShallowCorrections = new HashSet<ElliottModelType>
            {
                ElliottModelType.COMBINATION, 
                ElliottModelType.FLAT_EXTENDED, 
                ElliottModelType.FLAT_RUNNING,
                ElliottModelType.TRIANGLE_CONTRACTING,
                ElliottModelType.TRIANGLE_RUNNING
            };

            m_DeepCorrections = new HashSet<ElliottModelType>
            {
                ElliottModelType.ZIGZAG,
                ElliottModelType.DOUBLE_ZIGZAG
            };

            m_DiagonalImpulses = new HashSet<ElliottModelType>
            {
                ElliottModelType.DIAGONAL_INITIAL,
                ElliottModelType.DIAGONAL_ENDING
            };

            m_RunningCorrections = new HashSet<ElliottModelType>
            {
                ElliottModelType.FLAT_EXTENDED,
                ElliottModelType.FLAT_RUNNING,
                ElliottModelType.COMBINATION,
                ElliottModelType.TRIANGLE_RUNNING,
                ElliottModelType.TRIANGLE_EXPANDING
            };

            ModelRules impulse = ModelRules[ElliottModelType.IMPULSE];
            m_Wave1Impulse = impulse.Models[IMPULSE_ONE].ToHashSet();
            m_Wave2Impulse = impulse.Models[IMPULSE_TWO].ToHashSet();
            m_Wave4Impulse = impulse.Models[IMPULSE_FOUR].ToHashSet();
            m_Wave5Impulse = impulse.Models[IMPULSE_FIVE].ToHashSet();
        }

        public ModelPattern GetPattern(PatternArgsItem args, ElliottModelType model)
        {
            if (m_ModelGeneratorsMap.TryGetValue(model,
                    out Func<PatternArgsItem, ModelPattern> actionGen))
            {
                return actionGen(args);
            }

            throw new NotSupportedException($"Not supported model {model}");
        }
        
        #region Main patterns

        private ModelPattern GetImpulse(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.IMPULSE, arg.Candles);
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            {
                GetImpulseRandomSet(arg);
                return modelPattern;
            }

            GetCorrectiveRatios(out double the2NdRatio,
                out double the4ThRatio,
                out ElliottModelType the2NdModel,
                out ElliottModelType the4ThModel);

            double extendedRatio = SelectRandomly(IMPULSE_EXTENDED);
            double ratioPart;

            double wave1Len;
            double wave3Len;

            ElliottModelType the1StModel;
            ElliottModelType the5ThModel;

            byte extendedWaveNumber;
            switch (m_Random.NextDouble())
            {
                //is 3rd extended
                case >= 0.1 and <= 0.7:
                {
                    ratioPart = -the2NdRatio - the4ThRatio * extendedRatio;
                    wave1Len = NotExtendedWaveFormula(arg.Range, extendedRatio, ratioPart);
                    wave3Len = wave1Len * extendedRatio;

                    double modelRandom = m_Random.NextDouble();
                    the1StModel = modelRandom <= 0.5
                        ? ElliottModelType.DIAGONAL_INITIAL
                        : ElliottModelType.IMPULSE;
                    the5ThModel = modelRandom is <= 0.1 or >= 0.9
                        ? the1StModel == ElliottModelType.IMPULSE
                            ? ElliottModelType.IMPULSE
                            : ElliottModelType.DIAGONAL_ENDING
                        : the1StModel == ElliottModelType.IMPULSE
                            ? ElliottModelType.DIAGONAL_ENDING
                            : ElliottModelType.IMPULSE;
                    extendedWaveNumber = 3;
                    break;
                }
                //is 5th extended
                case > 0.7:
                    ratioPart = -the2NdRatio - the4ThRatio;
                    wave1Len = NotExtendedWaveFormula(arg.Range, extendedRatio, ratioPart);
                    wave3Len = wave1Len * (1 + MAIN_ALLOWANCE_MAX_RATIO * m_Random.NextDouble());// the 3rd can't be the shortest
                    the1StModel = m_Random.NextDouble() < 0.6
                        ? ElliottModelType.DIAGONAL_INITIAL
                        : ElliottModelType.IMPULSE;
                    the5ThModel = ElliottModelType.IMPULSE;
                    extendedWaveNumber = 5;
                    break;
                //is 1st extended
                default:
                    ratioPart = -the2NdRatio * extendedRatio - the4ThRatio;
                    wave3Len = NotExtendedWaveFormula(arg.Range, extendedRatio, ratioPart);
                    wave1Len = wave3Len * extendedRatio;
                    wave3Len += wave3Len * (1 + MAIN_ALLOWANCE_MAX_RATIO * m_Random.NextDouble());// the 3rd can't be the shortest

                    the1StModel = ElliottModelType.IMPULSE;
                    the5ThModel = m_Random.NextDouble() < 0.6
                        ? ElliottModelType.DIAGONAL_ENDING
                        : ElliottModelType.IMPULSE;
                    extendedWaveNumber = 1;
                    break;
            }

            double wave1Dur = m_DiagonalImpulses.Contains(the1StModel)
                              || extendedWaveNumber == 1
                ? 0.2 : 0.1;

            double wave2Dur = m_ShallowCorrections.Contains(the2NdModel) ? 0.2 : 0.1;
            double wave3Dur = extendedWaveNumber == 3 ? 0.2 : 0.1;
            double wave4Dur = m_ShallowCorrections.Contains(the4ThModel) ? 0.2 : 0.1;
            double wave5Dur = m_DiagonalImpulses.Contains(the5ThModel) || extendedWaveNumber == 5 ? 0.2 : 0.1;

            // we want to fit the 4 to 2 ratio to the whole correction duration in the
            // impulse.
            double correctionsDur = wave2Dur * (1 + wave2Dur * Get4To2DurationRatio(the2NdModel, the4ThModel));
            double correctionsRatiosFix = correctionsDur / (wave2Dur + wave4Dur);

            wave1Dur *= RandomRatio();
            wave2Dur *= RandomRatio() * correctionsRatiosFix;
            wave3Dur *= RandomRatio();
            wave4Dur *= RandomRatio() * correctionsRatiosFix;
            wave5Dur *= RandomRatio();

            double totalSumFix = 1 /
                                 (wave1Dur + wave2Dur + wave3Dur + wave4Dur + wave5Dur);
            int[] bars4Gen = PatternGenKit.SplitNumber(
                arg.BarsCount, new[]
                {
                    wave1Dur*totalSumFix,
                    wave2Dur*totalSumFix,
                    wave3Dur*totalSumFix,
                    wave4Dur*totalSumFix,
                    wave5Dur*totalSumFix
                });

            var wave1 = arg.StartValue + arg.IsUpK * wave1Len;
            var wave2 = wave1 - arg.IsUpK * the2NdRatio * wave1Len;
            var wave3 = wave2 + arg.IsUpK * wave3Len;
            var wave4 = wave3 - arg.IsUpK * wave3Len * the4ThRatio;
            var wave5 = arg.EndValue;
            
            ModelPattern modelWave1 = GetPattern(
                new PatternArgsItem(arg.StartValue, wave1, bars4Gen[0]), the1StModel);
            modelPattern.ChildModelPatterns.Add(modelWave1);
            
            var wave2Arg = new PatternArgsItem(
                modelWave1.Candles[^1].C, wave2, bars4Gen[1], wave1);
            if (m_RunningCorrections.Contains(the2NdModel))
            {
                // running part usually don't reach the 4th wave,
                // so we won't make it to happen.
                if (arg.IsUp)
                    wave2Arg.Min = wave4;
                else
                    wave2Arg.Max = wave4;
            }

            ModelPattern modelWave2 = GetPattern(wave2Arg, the2NdModel);
            modelPattern.ChildModelPatterns.Add(modelWave2);

            ModelPattern modelWave3 = GetPattern(
                new PatternArgsItem(modelWave2.Candles[^1].C, wave3, bars4Gen[2]), ElliottModelType.IMPULSE);
            modelPattern.ChildModelPatterns.Add(modelWave3);

            var wave4Arg = new PatternArgsItem(
                modelWave3.Candles[^1].C, wave4, bars4Gen[3], wave3);
            if (m_RunningCorrections.Contains(the4ThModel))
            {
                // running part usually don't reach the end of the impulse,
                // so we won't make it to happen.
                if (arg.IsUp)
                    wave4Arg.Min = wave5;
                else
                    wave4Arg.Max = wave5;
            }

            ModelPattern modelWave4 = GetPattern(wave4Arg, the4ThModel);
            modelPattern.ChildModelPatterns.Add(modelWave4);

            ModelPattern modelWave5 = GetPattern(
                new PatternArgsItem(modelWave4.Candles[^1].C, wave5, bars4Gen[4]), the5ThModel);
            modelPattern.ChildModelPatterns.Add(modelWave5);

            modelPattern.Candles.AddRange(modelWave1.Candles);
            modelPattern.Candles.AddRange(modelWave2.Candles);
            modelPattern.Candles.AddRange(modelWave3.Candles);
            modelPattern.Candles.AddRange(modelWave4.Candles);
            modelPattern.Candles.AddRange(modelWave5.Candles);

            modelPattern.PatternKeyPoints = new List<KeyValuePair<int, double>>
            {
                new(bars4Gen[0] - 1, wave1),
                new(bars4Gen.Take(2).Sum() - 1, wave2),
                new(bars4Gen.Take(3).Sum() - 1, wave3),
                new(bars4Gen.Take(4).Sum() - 1, wave4),
                new(bars4Gen.Sum() - 1, wave5),
            };

            return modelPattern;
        }

        private ModelPattern GetExtendedFlat(PatternArgsItem arg)
        {
            double bLimit = arg.IsUp ? arg.Min : arg.Max;
            if (arg.IsUp && bLimit >= arg.StartValue ||
                !arg.IsUp && bLimit <= arg.StartValue)
                throw new ArgumentException(nameof(bLimit));

            if (arg.BarsCount <= 0)
                throw new ArgumentException(nameof(arg.BarsCount));

            List<ICandle> candles = arg.Candles;
            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_EXTENDED, candles);

            double waveALength = RandomWithinRange(
                arg.Range * 0.3, arg.Range * 0.95);
            // extended flat, wave A should make less progress that the wave C

            double waveA = arg.StartValue + arg.IsUpK * waveALength;
            double waveCLengthMax = Math.Abs(bLimit - arg.EndValue);
            double waveCLengthMin = Math.Abs(arg.StartValue - arg.EndValue);
            double cMaxToA = waveCLengthMax / waveALength;
            double cMinToA = waveCLengthMin / waveALength;

            double waveCLength = SelectRandomly(
                MAP_EX_FLAT_WAVE_A_TO_C, cMinToA, cMaxToA) * waveALength;

            double waveB = arg.EndValue - arg.IsUpK * waveCLength;

            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            {
                GetSideRandomSet(arg, waveB, arg.EndValue);
                return modelPattern;
            }

            double rndSplitPart = m_Random.NextDouble() * 0.2 - 0.1;

            double barsA = 0.25 - rndSplitPart;
            double barsB = 0.5 + rndSplitPart;
            int[] bars4Gen = PatternGenKit.SplitNumber(
                arg.BarsCount, new[]
                {
                    barsA,
                    barsB,
                    1 - barsA - barsB
                });

            List<ICandle> candlesWaveA = GetCorrectiveRandomSet(
                new PatternArgsItem(arg.StartValue, waveA, bars4Gen[0]));

            List<ICandle> candlesWaveB = GetCorrectiveRandomSet(
                new PatternArgsItem(candlesWaveA[^1].C, waveB, bars4Gen[1], waveA));

            List<ICandle> candlesWaveC = GetImpulseRandomSet(
                new PatternArgsItem(candlesWaveB[^1].C, arg.EndValue, bars4Gen[2], waveB));

            candles.AddRange(candlesWaveA);
            candles.AddRange(candlesWaveB);
            candles.AddRange(candlesWaveC);

            modelPattern.PatternKeyPoints = new List<KeyValuePair<int, double>>
            {
                new(candlesWaveA.Count - 1, waveA),
                new(candlesWaveA.Count - 1 + candlesWaveB.Count - 1, waveB),
                new(arg.BarsCount - 1, arg.EndValue)
            };

            return modelPattern;
        }

        private ModelPattern GetRegularFlat(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_REGULAR, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            //}
        }

        private ModelPattern GetTripleZigzag(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.TRIPLE_ZIGZAG, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetExpandingDiagonal(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DIAGONAL_EXPANDING, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetExpandingTriangle(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.TRIANGLE_EXPANDING, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetCombination(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.COMBINATION, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetDoubleZigzag(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DOUBLE_ZIGZAG, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetZigzag(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.ZIGZAG, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetRunningTriangle(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.TRIANGLE_RUNNING, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetContractingTriangle(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.TRIANGLE_CONTRACTING, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetRunningFlat(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_RUNNING, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetEndingDiagonal(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DIAGONAL_ENDING, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetInitialDiagonal(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DIAGONAL_INITIAL, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetImpulseRandomSet(arg);
            return modelPattern;
            //}
        }

        #endregion

        #region Simple sets

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
            if (args.BarsCount <= 0)
                return candles;

            if (args.BarsCount == 1)
            {
                var cdl = new JsonCandleExport
                {
                    H = args.Max,
                    L = args.Min,
                };

                FillBorderCandlesStart(args, cdl);
                candles.Add(cdl);
                return candles;
            }

            if (variance <= 0)
                variance = 1;

            double previousClose = args.StartValue;
            double stepLinear = args.Range / args.BarsCount;

            for (int i = 0; i < args.BarsCount; i++)
            {
                double open;
                double meanPrice = args.StartValue +
                                   args.IsUpK * stepLinear * (i + 1);

                double? high = null;
                double? low = null;
                if (i == 0)
                {
                    double startExtremum = args.PrevCandleExtremum.HasValue
                        ? RandomWithinRange(args.StartValue, args.PrevCandleExtremum.Value)
                        : args.StartValue;

                    if (args.IsUp)
                        low = startExtremum;
                    else
                        high = startExtremum;

                    open = args.PrevCandleExtremum.HasValue
                        ? args.StartValue
                        : RandomWithinRange(meanPrice, args.StartValue);
                }
                else
                {
                    open = previousClose;
                }

                double varianceK = variance * stepLinear;
                double stepValMax;
                double stepValMin;

                if (useFullRange ||
                    variance > 1 && m_Random.NextDouble() >= 0.95) // throwout
                {
                    stepValMax = args.Max;
                    stepValMin = args.Min;
                }
                else
                {
                    stepValMax = Math.Max(open, Math.Min(Math.Max(meanPrice, open) + varianceK, args.Max));
                    stepValMin = Math.Min(open, Math.Max(Math.Min(meanPrice, open) - varianceK, args.Min));
                }

                high ??= RandomWithinRange(open, stepValMax);
                low ??= RandomWithinRange(open, stepValMin);
                double close = RandomWithinRange(high.Value, low.Value);

                if (low > high) Logger.Write("low bigger then the high check");

                var cdl = new JsonCandleExport
                {
                    C = Math.Round(close, args.Accuracy),
                    H = Math.Round(high.Value, args.Accuracy),
                    O = Math.Round(open, args.Accuracy),
                    L = Math.Round(low.Value, args.Accuracy)
                };
                candles.Add(cdl);
                previousClose = cdl.C;
            }

            var endItem = (JsonCandleExport)candles[^1];
            FillBorderCandlesEnd(args, endItem);

            return candles;
        }

        #endregion

        #region Fibonacci ratios

        private static readonly
            SortedDictionary<byte, double> IMPULSE_EXTENDED =
                new() {{0, 0}, {5, 1}, {20, 1.618}, {70, 2.618}, {85, 3.618}, {95, 4.236}};

        private static readonly
            SortedDictionary<byte, double> MAP_DEEP_CORRECTION =
                new() { { 0, 0 }, { 5, 0.5 }, { 25, 0.618 }, { 70, 0.786 }, { 98, 0.95 } };

        private static readonly
            SortedDictionary<byte, double> MAP_SHALLOW_CORRECTION =
                new() { { 0, 0 }, { 5, 0.236 }, { 35, 0.382 }, { 85, 0.5 } };

        private static readonly
            SortedDictionary<byte, double> MAP_EX_FLAT_WAVE_A_TO_C =
                new() { { 0, 0 }, { 20, 1.618 }, { 80, 2.618 }, { 95, 3.618 } };

        #endregion

        #region Helpers
        
        private void GetCorrectiveRatios(
            out double the2NdRatio,
            out double the4ThRatio,
            out ElliottModelType the2NdModel,
            out ElliottModelType the4ThModel)
        {
            bool is2NdDeep = false;
            bool is4ThDeep = false;

            switch (m_Random.NextDouble())
            {
                case <= 0.45:
                    is2NdDeep = true;
                    break;

                case > 0.45 and <= 0.9:
                    is4ThDeep = true;
                    break;

                case > 0.5 and <= 0.95:
                    is2NdDeep = true;
                    is4ThDeep = true;
                    break;
            }

            the2NdRatio = SelectRandomly(is2NdDeep
                ? MAP_DEEP_CORRECTION
                : MAP_SHALLOW_CORRECTION);
            the4ThRatio = SelectRandomly(is4ThDeep
                ? MAP_DEEP_CORRECTION
                : MAP_SHALLOW_CORRECTION);

            the2NdModel = m_Wave2Impulse
                .Intersect(is2NdDeep ? m_DeepCorrections : m_ShallowCorrections)
                .First();
            the4ThModel = m_Wave4Impulse
                .Intersect(is4ThDeep ? m_DeepCorrections : m_ShallowCorrections)
                .First();
        }

        private double NotExtendedWaveFormula(
            double range, double extendedRatio, double ratioPart) =>
            range / (2 + extendedRatio + ratioPart);

        private double Get4To2DurationRatio(
            ElliottModelType the2NdModel,
            ElliottModelType the4ThModel)
        {
            bool is4ThShallow = m_ShallowCorrections.Contains(the4ThModel);
            bool is2NdShallow = m_ShallowCorrections.Contains(the2NdModel);

            int highLimit = 300;

            if (is4ThShallow == is2NdShallow) highLimit = 200;
            if (is4ThShallow) highLimit = 500;

            return m_Random.Next(70, highLimit) / 100d;
        }

        private void FillBorderCandlesStart(
            PatternArgsItem args, 
            JsonCandleExport startItem)
        {
            if (args.IsUp)
            {
                if (args.PrevCandleExtremum.HasValue)
                {
                    startItem.L = RandomWithinRange(
                        args.PrevCandleExtremum.Value, args.StartValue);
                    startItem.O = args.StartValue;
                }
                else
                {
                    startItem.L = args.StartValue;
                    startItem.O = RandomWithinRange(args.StartValue, startItem.H);
                }
            }
            else
            {
                if (args.PrevCandleExtremum.HasValue)
                {
                    startItem.H = RandomWithinRange(
                        args.PrevCandleExtremum.Value, args.StartValue);
                    startItem.O = args.StartValue;
                }
                else
                {
                    startItem.H = args.StartValue;
                    startItem.O = RandomWithinRange(startItem.L, args.StartValue);
                }
            }

            startItem.C = RandomWithinRange(startItem.L, startItem.H);
        }

        private void FillBorderCandlesEnd(
            PatternArgsItem args,
            JsonCandleExport endItem)
        {
            if (args.IsUp)
            {
                endItem.H = args.EndValue;
                endItem.C = RandomWithinRange(endItem.L, endItem.H);
            }
            else
            {
                endItem.L = args.EndValue;
                endItem.C = RandomWithinRange(endItem.L, endItem.H);
            }
        }


        private double AddExtra(double value, double max)
        {
            if (value > max)
                throw new ArgumentException(nameof(max));

            // The real waves don't end exactly on fibonacci levels,
            // so we want to emulate this
            double rndExtra = value + m_Random.NextDouble() * value * MAIN_ALLOWANCE_MAX_RATIO;

            if (rndExtra <= max)
                return rndExtra;

            return value + m_Random.NextDouble() * (max - value);
        }

        private double SelectRandomly(
            SortedDictionary<byte, double> valuesMap, 
            double min = double.NaN, 
            double max = double.NaN)
        {
            if (double.IsNaN(min)) 
                min = valuesMap.Where(a => a.Key > 0).Min(a => a.Value);
            if (double.IsNaN(max))
                max = valuesMap.Where(a => a.Key > 0).Max(a => a.Value);

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

            byte randomNext = (byte) m_Random.Next(0, 100);
            KeyValuePair<byte, double>[] rndFoundItems = selectedItems
                .TakeWhile(a => a.Key <= randomNext)
                .ToArray();
            if (rndFoundItems.Length == 0)
                return AddExtra(min, max);

            double foundLevel = rndFoundItems[^1].Value;
            if (foundLevel == 0)
                return AddExtra(min, max);

            return AddExtra(foundLevel, max);
        }

        private double RandomRatio()
        {
            return RandomWithinRange(
                1 - MAIN_ALLOWANCE_MAX_RATIO,
                1 + MAIN_ALLOWANCE_MAX_RATIO);
        }

        private double RandomWithinRange(double one, double two)
        {
            double min = Math.Min(one, two);
            double max = Math.Max(one, two);

            return min + m_Random.NextDouble() * (max - min);
        }

        #endregion
    }
}
