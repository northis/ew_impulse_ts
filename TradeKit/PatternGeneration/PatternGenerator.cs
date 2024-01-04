using Plotly.NET;
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
        private const double MAIN_ALLOWANCE_MAX_RATIO_INVERT = 1- MAIN_ALLOWANCE_MAX_RATIO;
        private const double MAIN_ALLOWANCE_MAX_RATIO_ONE_PLUS = 1 + MAIN_ALLOWANCE_MAX_RATIO;

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
        private HashSet<ElliottModelType> m_TruncatedImpulses;
        private HashSet<ElliottModelType> m_RunningCorrections;

        private HashSet<ElliottModelType> m_Wave1Impulse;
        private HashSet<ElliottModelType> m_Wave2Impulse;
        private HashSet<ElliottModelType> m_Wave4Impulse;
        private HashSet<ElliottModelType> m_Wave5Impulse;
        private ElliottModelType[] m_ImpulseOnly;

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
                                    ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
                                    ElliottModelType.DIAGONAL_EXPANDING_INITIAL
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
                                    ElliottModelType.DIAGONAL_CONTRACTING_ENDING,
                                    ElliottModelType.DIAGONAL_EXPANDING_ENDING
                                }
                            },
                        })
                },
                {
                    ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, new ModelRules(
                        new Dictionary<string, ElliottModelType[]>
                        {
                            {
                                IMPULSE_ONE, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
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
                                    ElliottModelType.DIAGONAL_CONTRACTING_ENDING,
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                }
                            },
                        }, 0.03)
                },
                {
                    ElliottModelType.DIAGONAL_CONTRACTING_ENDING, new ModelRules(
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
                                    ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
                                    ElliottModelType.DIAGONAL_EXPANDING_INITIAL
                                }
                            },
                            {
                                CORRECTION_B, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                    ElliottModelType.TRIPLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.FLAT_REGULAR,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING,
                                    ElliottModelType.TRIANGLE_EXPANDING
                                }
                            },
                            {
                                CORRECTION_C, new[]
                                {
                                    ElliottModelType.IMPULSE,
                                    ElliottModelType.DIAGONAL_CONTRACTING_ENDING,
                                    ElliottModelType.DIAGONAL_EXPANDING_ENDING
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
                                    ElliottModelType.TRIPLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.FLAT_REGULAR,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING,
                                    ElliottModelType.TRIANGLE_EXPANDING
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
                    ElliottModelType.TRIPLE_ZIGZAG, new ModelRules(
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
                                    ElliottModelType.TRIPLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.FLAT_REGULAR
                                }
                            },
                            {
                                CORRECTION_XX, new[]
                                {
                                    ElliottModelType.ZIGZAG,
                                    ElliottModelType.DOUBLE_ZIGZAG,
                                    ElliottModelType.TRIPLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.FLAT_REGULAR,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING,
                                    ElliottModelType.TRIANGLE_EXPANDING
                                }
                            },
                            {
                                CORRECTION_Z, new[]
                                {
                                    ElliottModelType.ZIGZAG
                                }
                            }
                        }, 0.001)
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
                                    ElliottModelType.TRIPLE_ZIGZAG,
                                    ElliottModelType.FLAT_EXTENDED,
                                    ElliottModelType.FLAT_RUNNING,
                                    ElliottModelType.FLAT_REGULAR,
                                    ElliottModelType.TRIANGLE_CONTRACTING,
                                    ElliottModelType.TRIANGLE_RUNNING,
                                    ElliottModelType.TRIANGLE_EXPANDING
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
                                    ElliottModelType.DIAGONAL_CONTRACTING_ENDING,
                                    ElliottModelType.DIAGONAL_EXPANDING_ENDING
                                }
                            }
                        })
                }
            };

            ModelRules[ElliottModelType.FLAT_REGULAR] =
                ModelRules[ElliottModelType.FLAT_EXTENDED] with 
                    {ProbabilityCoefficient = 0.0005 };

            ModelRules[ElliottModelType.FLAT_RUNNING] =
                ModelRules[ElliottModelType.FLAT_EXTENDED];

            ModelRules[ElliottModelType.TRIANGLE_EXPANDING] =
                ModelRules[ElliottModelType.TRIANGLE_CONTRACTING] with
                    { ProbabilityCoefficient = 0.001 };

            ModelRules[ElliottModelType.DIAGONAL_EXPANDING_INITIAL] =
                ModelRules[ElliottModelType.DIAGONAL_CONTRACTING_INITIAL] with
                    { ProbabilityCoefficient = 0.01 };

            ModelRules[ElliottModelType.DIAGONAL_EXPANDING_ENDING] =
                ModelRules[ElliottModelType.DIAGONAL_CONTRACTING_ENDING] with
                    { ProbabilityCoefficient = 0.0001 };
            
            ModelRules[ElliottModelType.TRIANGLE_RUNNING] =
                ModelRules[ElliottModelType.TRIANGLE_CONTRACTING] with
                    { ProbabilityCoefficient = 0.1 };

            m_ModelGeneratorsMap = new Dictionary<ElliottModelType, 
                Func<PatternArgsItem, ModelPattern>>
            {
                {ElliottModelType.IMPULSE, GetImpulse},
                {ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, GetInitialDiagonal},
                {ElliottModelType.DIAGONAL_EXPANDING_INITIAL, GetInitialExpandingDiagonal},
                {ElliottModelType.DIAGONAL_CONTRACTING_ENDING, GetEndingDiagonal},
                {ElliottModelType.DIAGONAL_EXPANDING_ENDING, GetEndingExpandingDiagonal},
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
                ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
                ElliottModelType.DIAGONAL_CONTRACTING_ENDING
            };

            m_TruncatedImpulses = new HashSet<ElliottModelType>
            {
                ElliottModelType.IMPULSE,
                ElliottModelType.DIAGONAL_CONTRACTING_ENDING
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

            m_ImpulseOnly = new[] { ElliottModelType.IMPULSE };
        }

        public ModelPattern GetPattern(PatternArgsItem args, ElliottModelType model)
        {
            if (args.BarsCount <= 0)
                throw new ArgumentException(nameof(args.BarsCount));

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

            const double wave1MinProp = 0.05;
            const double wave1MaxProp = 0.75;
            const double wave1MeanProp = 0.25;
            double wave1LenProp = PatternGenKit.GetNormalDistributionNumber(
                m_Random, wave1MinProp, wave1MaxProp, wave1MeanProp);
            double wave1Len = wave1LenProp * arg.Range;

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

            double the2NdRatio = SelectRandomly(is2NdDeep
                ? MAP_DEEP_CORRECTION
                : MAP_SHALLOW_CORRECTION);

            // we want such min and max that don't broke the overlap rule between the
            // 4th wave and the 1st one.
            double wave5Len = wave1Len * SelectRandomly(IMPULSE_5_TO_1,
                min: double.NaN,
                max: (arg.Range - wave1Len) / wave1Len);

            // Add reduced 5th
            bool isTruncatedAllowed = IsTruncatedAllowed(modelPattern.Model);
            double wave3LenMax =
                (isTruncatedAllowed ? Math.Abs((arg.IsUp ? arg.Max : arg.Min) - arg.StartValue) : arg.Range) -
                wave1Len * (1 - the2NdRatio);

            // we shouldn't allow the shortest 3rd wave as well.
            double wave3Len = wave1Len * SelectRandomly(IMPULSE_3_TO_1,
                min: Math.Max(arg.Range - wave5Len - wave1Len * (1 - the2NdRatio),
                    Math.Min(wave1Len, wave5Len)) / wave1Len,
                max: wave3LenMax / wave1Len);

            if (wave3Len <= wave5Len && wave3Len <= wave1Len)
            {
                throw new ApplicationException("Wave 3 is the shortest");
            }

            ElliottModelType the2NdModel = m_Wave2Impulse
                .Intersect(is2NdDeep ? m_DeepCorrections : m_ShallowCorrections)
                .First();
            ElliottModelType the4ThModel = m_Wave4Impulse
                .Intersect(is4ThDeep ? m_DeepCorrections : m_ShallowCorrections)
                .First();

            ElliottModelType the1StModel;
            ElliottModelType the5ThModel;

            ElliottModelType DiagonalInitial() => WeightedRandomlySelectModel(new[]
                {ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, ElliottModelType.DIAGONAL_EXPANDING_INITIAL});

            ElliottModelType DiagonalEnding() => WeightedRandomlySelectModel(new[]
                {ElliottModelType.DIAGONAL_CONTRACTING_ENDING, ElliottModelType.DIAGONAL_EXPANDING_ENDING});

            byte extendedWaveNumber;
            if (wave5Len > wave3Len && wave5Len > wave1Len)
            {
                the1StModel = m_Random.NextDouble() < 0.6
                    ? DiagonalInitial()
                    : ElliottModelType.IMPULSE;
                the5ThModel = ElliottModelType.IMPULSE;
                extendedWaveNumber = 5;
            }
            else if (wave1Len > wave3Len)
            {
                the1StModel = ElliottModelType.IMPULSE;
                the5ThModel = m_Random.NextDouble() < 0.6
                    ? DiagonalEnding()
                    : ElliottModelType.IMPULSE;
                extendedWaveNumber = 1;
            }
            else
            {
                double modelRandom = m_Random.NextDouble();
                the1StModel = modelRandom <= 0.5
                    ? DiagonalInitial()
                    : ElliottModelType.IMPULSE;
                the5ThModel = modelRandom is <= 0.1 or >= 0.9
                    ? the1StModel == ElliottModelType.IMPULSE
                        ? ElliottModelType.IMPULSE
                        : DiagonalEnding()
                    : the1StModel == ElliottModelType.IMPULSE
                        ? DiagonalEnding()
                        : ElliottModelType.IMPULSE;
                extendedWaveNumber = 3;
            }

            if (!m_Wave1Impulse.Contains(the1StModel) ||
                !m_Wave5Impulse.Contains(the5ThModel))
            {
                throw new ApplicationException("Wrong impulse configuration");
            }
            
            double wave1Dur = m_DiagonalImpulses.Contains(the1StModel)
                              || extendedWaveNumber == 1
                ? 0.2 : 0.1;

            double wave2Dur = m_ShallowCorrections.Contains(the2NdModel) ? 0.2 : 0.1;
            double wave3Dur = extendedWaveNumber == 3 ? 0.2 : 0.1;
            double wave5Dur = m_DiagonalImpulses.Contains(the5ThModel) || extendedWaveNumber == 5 ? 0.2 : 0.1;
            
            wave1Dur *= RandomRatio();
            wave2Dur *= RandomRatio();
            wave3Dur *= RandomRatio();

            // we want to fit the 4 to 2 ratio to the whole correction duration in the
            // impulse.
            double wave4Dur = wave2Dur * Get4To2DurationRatio(the2NdModel, the4ThModel);
            wave5Dur *= RandomRatio();
            
            int[] bars4Gen = PatternGenKit.SplitNumber(
                arg.BarsCount, new[]
                {
                    wave1Dur,
                    wave2Dur,
                    wave3Dur,
                    wave4Dur,
                    wave5Dur
                });

            double wave1 = arg.StartValue + arg.IsUpK * wave1Len;
            double wave2 = wave1 - arg.IsUpK * the2NdRatio * wave1Len;
            double wave3 = wave2 + arg.IsUpK * wave3Len;
            double wave4 = arg.EndValue - arg.IsUpK * wave5Len;
            double wave5 = arg.EndValue;

            if (arg.IsUp && wave4 <= wave1 || !arg.IsUp && wave4 >= wave1)
                throw new ApplicationException("Wave 4/1 overlapse");

            double runningLimit3And5 = arg.IsUp ? arg.Max : arg.Min;
            FillPattern(arg, modelPattern, bars4Gen,
                new[] {wave1, wave2, wave3, wave4, wave5}, null,
                new[]
                {
                    double.NaN, 
                    wave4, // don't want the running part of the 2nd to exceed the 4th.
                    runningLimit3And5, // pass the truncation limit
                    wave3,// can we allow the running part of the 4th wave exceed
                    // the 3er wave?
                    runningLimit3And5
                });

            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(IMPULSE_THREE, IMPULSE_ONE, 
                        wave3Len / wave1Len),
                    new LengthRatio(IMPULSE_FIVE, IMPULSE_ONE, 
                        wave5Len / wave1Len),
                    new LengthRatio(IMPULSE_TWO, IMPULSE_ONE,
                        Math.Abs(wave1 - wave2) / wave1Len),
                    new LengthRatio(IMPULSE_FOUR, IMPULSE_THREE,
                        Math.Abs(wave3 - wave4) / wave3Len),
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(IMPULSE_FOUR, IMPULSE_TWO, wave4Dur / wave2Dur));

            return modelPattern;
        }

        private ModelPattern GetExtendedFlat(PatternArgsItem arg)
        {
            double bLimit = arg.IsUp ? arg.Min : arg.Max;
            if (arg.IsUp && bLimit >= arg.StartValue ||
                !arg.IsUp && bLimit <= arg.StartValue)
                throw new ArgumentException(nameof(bLimit));

            List<ICandle> candles = arg.Candles;
            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_EXTENDED, candles);

            double waveALength = RandomWithinRange(
                arg.Range * 0.3, arg.Range * MAIN_ALLOWANCE_MAX_RATIO_INVERT);
            // extended flat, wave A should make less progress that the wave C

            double waveA = arg.StartValue + arg.IsUpK * waveALength;
            double waveCLengthMax = Math.Abs(bLimit - arg.EndValue);
            double waveCLengthMin = Math.Abs(arg.StartValue - arg.EndValue);
            double cMaxToA = waveCLengthMax / waveALength;
            double cMinToA = waveCLengthMin / waveALength;

            double waveCLength = SelectRandomly(
                MAP_EX_FLAT_WAVE_C_TO_A, cMinToA, cMaxToA) * waveALength;

            double waveB = arg.EndValue - arg.IsUpK * waveCLength;

            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            {
                GetSideRandomSet(arg, waveB, arg.EndValue);
                return modelPattern;
            }

            int[] bars4Gen = SplitByTree(arg.BarsCount);

            FillPattern(arg, modelPattern, bars4Gen, 
                new[] {waveA, waveB, arg.EndValue});

            double waveBLength = Math.Abs(waveA - waveB);
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(CORRECTION_C, CORRECTION_A, 
                        waveCLength / waveALength),
                    new LengthRatio(CORRECTION_B, CORRECTION_A, 
                        waveBLength / waveALength)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(CORRECTION_B, CORRECTION_A,
                    modelPattern.PatternKeyPoints[1].Item2 / modelPattern.PatternKeyPoints[0].Item1));

            return modelPattern;
        }

        private ModelPattern GetRegularFlat(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_REGULAR, arg.Candles);
            
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            double bToA = RandomWithinRange(0.9, 1);
            double cToA = SelectRandomly(MAP_REG_FLAT_WAVE_C_TO_A);
            double waveALen = arg.Range / (1 - bToA + cToA);
            double waveBLen = bToA * waveALen;

            double waveA = arg.StartValue + arg.IsUpK * waveALen;
            double waveB = waveA - arg.IsUpK * waveBLen;
            double waveC = arg.EndValue;

            int[] bars4Gen = SplitByTree(arg.BarsCount);

            FillPattern(arg, modelPattern, bars4Gen, new []{ waveA, waveB, waveC });

            double waveCLen = cToA * waveALen;
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(CORRECTION_C, CORRECTION_A,
                        waveCLen / waveALen)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(CORRECTION_B, CORRECTION_A,
                    modelPattern.PatternKeyPoints[1].Item2 / modelPattern.PatternKeyPoints[0].Item1));

            return modelPattern;
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

            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            Dictionary<string, ElliottModelType[]> models 
                = ModelRules[modelPattern.Model].Models;
            ElliottModelType theModelB = WeightedRandomlySelectModel(models[CORRECTION_B]);

            double cToA = SelectRandomly(ZIGZAG_C_TO_A);

            bool isShallow = m_ShallowCorrections.Contains(theModelB);
            double bToA = SelectRandomly(isShallow
                ? MAP_SHALLOW_CORRECTION
                : MAP_DEEP_CORRECTION);

            double waveALen = arg.Range / (1 - bToA + cToA);

            double waveA = arg.StartValue + arg.IsUpK * waveALen;
            double waveBLen = waveALen * bToA;
            double waveB = waveA - arg.IsUpK * waveBLen;
            double waveC = arg.EndValue;
            
            int[] bars4Gen = SplitByTree(arg.BarsCount);

            ElliottModelType theAModel =
                WeightedRandomlySelectModel(models[CORRECTION_A]);

            ElliottModelType[] modelsForC;
            if (m_Random.NextDouble() > 0.2)// impulse/diagonal change in 80% cases
            {
                modelsForC = (theAModel == ElliottModelType.IMPULSE
                        ? models[CORRECTION_C].Except(m_ImpulseOnly)
                        : m_ImpulseOnly)
                    .ToArray();
            }
            else
            {
                modelsForC = models[CORRECTION_C];
            }

            ElliottModelType theCModel = WeightedRandomlySelectModel(modelsForC);

            ElliottModelType[] definedModels = {theAModel, theModelB, theCModel};
            FillPattern(arg, modelPattern, bars4Gen, 
                new[] {waveA, waveB, waveC}, definedModels);

            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(CORRECTION_C, CORRECTION_A,cToA),
                    new LengthRatio(CORRECTION_B, CORRECTION_A, bToA)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(CORRECTION_B, CORRECTION_A,
                    modelPattern.PatternKeyPoints[1].Item2 / modelPattern.PatternKeyPoints[0].Item1));

            return modelPattern;
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
            double bLimit = arg.IsUp ? arg.Min : arg.Max;
            if (arg.IsUp && bLimit >= arg.StartValue ||
                !arg.IsUp && bLimit <= arg.StartValue)
                throw new ArgumentException(nameof(bLimit));

            double aLimit = arg.IsUp ? arg.Max : arg.Min;
            if (arg.IsUp && aLimit <= arg.EndValue ||
                !arg.IsUp && aLimit >= arg.EndValue)
                throw new ArgumentException(nameof(aLimit));

            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_RUNNING, arg.Candles);
            
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            double bToA = SelectRandomly(MAP_FLAT_WAVE_B_TO_A, double.MinValue,
                Math.Abs((arg.Max - arg.Min) / (arg.StartValue - aLimit)));
            double cToA = SelectRandomly(MAP_RUNNING_FLAT_WAVE_C_TO_A, double.MinValue,
                Math.Abs((arg.EndValue - bLimit) / (arg.StartValue - aLimit)));

            double waveALen = arg.Range / (1 - bToA + cToA);
            double waveBLen = waveALen * bToA;
            double waveA = arg.StartValue + arg.IsUpK * waveALen;
            double waveB = waveA - arg.IsUpK * waveBLen;
            double waveC = arg.EndValue;

            int[] bars4Gen = SplitByTree(arg.BarsCount);
            FillPattern(arg, modelPattern, bars4Gen, new[] {waveA, waveB, waveC});

            double waveCLen = cToA * waveALen;
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(CORRECTION_C, CORRECTION_A,
                        waveCLen / waveALen)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(CORRECTION_B, CORRECTION_A,
                    modelPattern.PatternKeyPoints[1].Item2 / modelPattern.PatternKeyPoints[0].Item1));

            return modelPattern;
        }

        private ModelPattern GetEndingDiagonal(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DIAGONAL_CONTRACTING_ENDING, arg.Candles);

            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            // truncated 5th - running 3rd
            return GetDiagonal(arg, modelPattern);
        }

        private ModelPattern GetDiagonal(PatternArgsItem arg, ModelPattern modelPattern)
        {
            double wave1Len = arg.Range * PatternGenKit
                .GetNormalDistributionNumber(m_Random, 0.7, 0.9, 0.786);
            double wave2Len = wave1Len * PatternGenKit
                .GetNormalDistributionNumber(m_Random, 0.5, 0.75, 0.66);

            double wave1 = arg.StartValue + arg.IsUpK * wave1Len;
            double wave2 = wave1 - arg.IsUpK * wave2Len;

            bool isTruncatedAllowed = IsTruncatedAllowed(modelPattern.Model);
            double restOne = arg.Range - wave1Len;
            double wave3LenMax = isTruncatedAllowed
                ? Math.Min(wave1Len * MAIN_ALLOWANCE_MAX_RATIO_INVERT,
                    Math.Min(2 * wave2Len * MAIN_ALLOWANCE_MAX_RATIO_INVERT,
                        Math.Abs(wave2 - (arg.IsUp ? arg.Max : arg.Min))))
                : restOne * MAIN_ALLOWANCE_MAX_RATIO_INVERT + wave2Len;

            double wave3Len = wave1Len * SelectRandomly(
                CONTRACTING_DIAGONAL_3_TO_1,
                wave2Len * MAIN_ALLOWANCE_MAX_RATIO_ONE_PLUS / wave1Len,
                wave3LenMax / wave1Len);

            double wave3 = wave2 + arg.IsUpK * wave3Len;
            double restTree = Math.Abs(arg.EndValue - wave3);
            double diffOneTree = Math.Abs(wave1 - wave3)
                                 * MAIN_ALLOWANCE_MAX_RATIO_ONE_PLUS;

            double wave4Len = RandomWithinRange(diffOneTree, wave3Len - restTree);
            double wave4 = wave3 - arg.IsUpK * arg.IsUpK * wave4Len;
            
            double bars1Prop = PatternGenKit
                .GetNormalDistributionNumber(m_Random, 0.5, 0.8, 0.618);
            double bars2Prop = (1 - bars1Prop) * RandomWithinRange(0.2, 0.5);
            double bars3Prop = (1 - bars1Prop - bars2Prop) *
                               RandomWithinRange(0.2, 0.7);
            double bars4Prop = (1 - bars1Prop - bars2Prop - bars3Prop) *
                               RandomWithinRange(0.2, 0.7);

            int[] bars4Gen = PatternGenKit.SplitNumber(
                arg.BarsCount, new[]
                {
                    bars1Prop,
                    bars2Prop,
                    bars3Prop,
                    bars4Prop,
                    1 - bars1Prop - bars2Prop - bars3Prop - bars4Prop
                });

            FillPattern(arg, modelPattern, bars4Gen,
                new[] { wave1, wave2, wave3, wave4, arg.EndValue });


            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(IMPULSE_THREE, IMPULSE_ONE,
                        wave3Len / wave1Len)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(IMPULSE_THREE, IMPULSE_ONE,
                    (double) bars4Gen[2] / bars4Gen[1]));

            return modelPattern;
        }

        private ModelPattern GetInitialDiagonal(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, arg.Candles);

            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            {
                GetImpulseRandomSet(arg);
                return modelPattern;
            }

            return GetDiagonal(arg, modelPattern);
        }

        private void FillPattern(
            PatternArgsItem arg, 
            ModelPattern pattern, 
            int[] bars4Gen, 
            double[] values,
            ElliottModelType[] definedModels = null,
            double[] runningLimits = null)
        {
            Dictionary<string, ElliottModelType[]> models =
                ModelRules[pattern.Model].Models;
            string[] keys = models.Keys.ToArray();

            if (bars4Gen.Length == 0 ||
                bars4Gen.Length != models.Count ||
                bars4Gen.Length != keys.Length)
                throw new ArgumentException(nameof(pattern));
            
            ModelPattern modelCurrent = null;

            bool isUp = arg.IsUp;
            for (int i = 0; i < values.Length; i++)
            {
                PatternArgsItem waveArg = modelCurrent == null
                    ? new PatternArgsItem(arg.StartValue, values[i], bars4Gen[i])
                    : GetNext(isUp, bars4Gen[i],
                        modelCurrent.Candles[^1], values[i - 1], values[i]);

                ElliottModelType model = definedModels == null
                    ? WeightedRandomlySelectModel(models[keys[i]])
                    : definedModels[i];

                bool useRunningLimits = runningLimits != null &&
                                        !double.IsNaN(runningLimits[i]);
                if (i < values.Length - 1 && m_RunningCorrections.Contains(model))
                {
                    // running part usually don't reach the next wave,
                    // so we won't make it to happen.
                    if (waveArg.IsUp)
                        waveArg.Min = useRunningLimits
                            ? runningLimits[i]
                            : values[i + 1];
                    else
                        waveArg.Max = useRunningLimits
                            ? runningLimits[i]
                            : values[i + 1];
                }

                // We want to handle truncated limits
                if (useRunningLimits && m_TruncatedImpulses.Contains(model))
                {
                    if (waveArg.IsUp)
                        waveArg.Max = runningLimits[i];
                    else
                        waveArg.Min = runningLimits[i];
                }

                ModelPattern modelWave = GetPattern(waveArg, model);
                pattern.ChildModelPatterns.Add(modelWave);
                pattern.Candles.AddRange(modelWave.Candles);
                modelCurrent = modelWave;
                isUp = !isUp;
            }

            for (int i = 0; i < values.Length - 1; i++)
            {
                pattern.PatternKeyPoints.Add(
                    (bars4Gen.Take(i + 1).Sum() - 1, values[i]));
            }

            pattern.PatternKeyPoints.Add((arg.BarsCount - 1, arg.EndValue));
        }


        private ModelPattern GetInitialExpandingDiagonal(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DIAGONAL_EXPANDING_INITIAL, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        private ModelPattern GetEndingExpandingDiagonal(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DIAGONAL_EXPANDING_ENDING, arg.Candles);

            // TODO
            //if (arg.BarsCount < SIMPLE_BARS_THRESHOLD)
            //{
            GetCorrectiveRandomSet(arg);
            return modelPattern;
            //}
        }

        #endregion

        #region Simple sets

        private List<ICandle> GetSideRandomSet(
            PatternArgsItem args, double runningPrice, double correctivePrice)
        {
            args.Max = Math.Max(runningPrice, correctivePrice);
            args.Min = Math.Min(runningPrice, correctivePrice);

            return GetCorrectiveRandomSet(args);
        }

        private List<ICandle> GetImpulseRandomSet(PatternArgsItem args)
        {
            return GetRandomSet(args, 0.2);
        }

        private List<ICandle> GetCorrectiveRandomSet(PatternArgsItem args)
        {
            return GetRandomSet(args, m_Random.Next(2, 5), true);
        }

        private List<ICandle> GetRandomSet(
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
                FixCandle(cdl);
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
                    variance > 1 && m_Random.NextDouble() >= 
                    (1-MAIN_ALLOWANCE_MAX_RATIO)) // throwout
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

                // handle PrevCandleExtremum value, it can beyond the range
                FixCandle(cdl);

                candles.Add(cdl);
                previousClose = cdl.C;
            }

            var endItem = (JsonCandleExport)candles[^1];
            FillBorderCandlesEnd(args, endItem);

            return candles;
        }

        private void FixCandle(JsonCandleExport cdl)
        {
            if (cdl.H < cdl.C) (cdl.H, cdl.C) = (cdl.C, cdl.H);
            if (cdl.H < cdl.O) (cdl.H, cdl.O) = (cdl.O, cdl.H);
            if (cdl.L > cdl.C) (cdl.L, cdl.C) = (cdl.C, cdl.L);
            if (cdl.L > cdl.O) (cdl.L, cdl.O) = (cdl.O, cdl.L);
        }

        #endregion

        #region Fibonacci ratios

        private static readonly
            SortedDictionary<byte, double> ZIGZAG_C_TO_A =
                new() {{0, 0}, {5, 0.618}, {25, 0.786}, {35, 0.786}, {75, 1}, {85, 1.618}, {90, 2.618}, {95, 3.618}};

        private static readonly
            SortedDictionary<byte, double> CONTRACTING_DIAGONAL_3_TO_1 =
                new() { { 0, 0 }, { 5, 0.5 }, { 15, 0.618 }, { 20, 0.786 } };

        private static readonly
            SortedDictionary<byte, double> IMPULSE_3_TO_1 =
                new() {{0, 0}, {5, 0.618}, {10, 0.786}, {15, 1}, {25, 1.618}, {60, 2.618}, {75, 3.618}, {90, 4.236}};

        private static readonly
            SortedDictionary<byte, double> IMPULSE_5_TO_1 =
                new() { { 0, 0 }, { 5, 0.382 }, { 10, 0.618 }, { 20, 0.786 }, { 25, 1 }, { 75, 1.618 }, { 85, 2.618 }, { 95, 3.618 }, { 99, 4.236 }};

        private static readonly
            SortedDictionary<byte, double> MAP_DEEP_CORRECTION =
                new() { { 0, 0 }, { 5, 0.5 }, { 25, 0.618 }, { 70, 0.786 }, { 99, 0.95 } };

        private static readonly
            SortedDictionary<byte, double> MAP_SHALLOW_CORRECTION =
                new() { { 0, 0 }, { 5, 0.236 }, { 35, 0.382 }, { 85, 0.5 } };

        private static readonly
            SortedDictionary<byte, double> MAP_EX_FLAT_WAVE_C_TO_A =
                new() { { 0, 0 }, { 20, 1.618 }, { 80, 2.618 }, { 95, 3.618 } };

        private static readonly
            SortedDictionary<byte, double> MAP_REG_FLAT_WAVE_C_TO_A =
                new() { { 0, 0 }, { 5, 1 }, { 80, 1.272 }, { 95, 1.618 } };

        private static readonly
            SortedDictionary<byte, double> MAP_FLAT_WAVE_B_TO_A =
                new() { { 0, 0 }, { 5, 1 }, { 80, 1.272 }, { 95, 1.618 } };
        private static readonly
            SortedDictionary<byte, double> MAP_RUNNING_FLAT_WAVE_C_TO_A =
                new() { { 0, 0 }, { 5, 0.5 }, { 20, 0.618 }, { 80, 1 }, { 90, 1.272 }, { 95, 1.618 } };

        #endregion

        #region Helpers

        private T WeightedRandomlySelect<T>(List<(T, double)> toSelect)
        {
            double cumulativeProbability = 0.0;
            double sum = toSelect.Sum(a => a.Item2);
            var items = new List<(T, double)>();
            foreach ((T, double) toSelectItem in toSelect)
            {
                cumulativeProbability += toSelectItem.Item2 / sum;
                items.Add((toSelectItem.Item1, cumulativeProbability));
            }

            double rnd = m_Random.NextDouble();
            return items.First(a => a.Item2 >= rnd).Item1;
        }

        private ElliottModelType WeightedRandomlySelectModel(ElliottModelType[] models)
        {
            List<(ElliottModelType a, double ProbabilityCoefficient)> res = models
                .Select(a => (a, ModelRules[a].ProbabilityCoefficient))
                .ToList();
            return WeightedRandomlySelect(res);
        }

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

            List<(double, double)> rndFoundItems = selectedItems
                .Where(a => a.Key > 0)
                .Select(a => (a.Value, a.Key / 100d))
                .ToList();
            if (rndFoundItems.Count == 0)
                return AddExtra(min, max);

            double foundLevel = WeightedRandomlySelect(rndFoundItems);
            if (foundLevel == 0)
                return AddExtra(min, max);

            return AddExtra(foundLevel, max);
        }

        private double RandomRatio()
        {
            return RandomWithinRange(
                MAIN_ALLOWANCE_MAX_RATIO_INVERT,
                MAIN_ALLOWANCE_MAX_RATIO_ONE_PLUS);
        }

        private double RandomWithinRange(double one, double two)
        {
            double min = Math.Min(one, two);
            double max = Math.Max(one, two);

            return min + m_Random.NextDouble() * (max - min);
        }

        private bool IsTruncatedAllowed(ElliottModelType modelType)
        {
            return m_TruncatedImpulses.Contains(modelType) && 
                   m_Random.NextDouble() <= 0.05;
        }

        private PatternArgsItem GetNext(
            bool isNextUp,
            int barsCount,
            ICandle lastCandle,
            double currentWave,
            double nextWave)
        {
            PatternArgsItem waveNext;
            double lastClose = lastCandle.C;
            if (isNextUp && lastClose > nextWave ||
                !isNextUp && lastClose < nextWave)
            {
                double newValC = RandomWithinRange(isNextUp 
                    ? lastCandle.L 
                    : lastCandle.H, nextWave);

                if (lastCandle is JsonCandleExport jsonCandle)
                    jsonCandle.C = newValC;
                waveNext = new PatternArgsItem(newValC, nextWave, barsCount);
            }
            else
            {
                waveNext = new PatternArgsItem(
                    lastClose, nextWave, barsCount, currentWave);
            }

            return waveNext;
        }

        /// <summary>
        /// Splits the bars for the 3-wave pattern.
        /// </summary>
        /// <param name="barsCount">The bars count.</param>
        /// <returns>Split values</returns>
        private int[] SplitByTree(int barsCount)
        {
            double rndSplitPart = m_Random.NextDouble() * 0.2 - 0.1;

            double bars1 = 0.25 - rndSplitPart;
            double bars2 = 0.5 + rndSplitPart;
            int[] bars4Gen = PatternGenKit.SplitNumber(
                barsCount, new[]
                {
                    bars1,
                    bars2,
                    1 - bars1 - bars2
                });

            return bars4Gen;
        }

        #endregion
    }
}
