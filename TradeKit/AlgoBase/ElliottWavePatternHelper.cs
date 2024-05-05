using System.Collections.Generic;
using TradeKit.Impulse;

namespace TradeKit.AlgoBase
{
    public static class ElliottWavePatternHelper
    {
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

        public static HashSet<ElliottModelType> ShallowCorrections { get; }
        public static HashSet<ElliottModelType> DeepCorrections { get; }
        public static HashSet<ElliottModelType> DiagonalImpulses { get; }
        public static HashSet<ElliottModelType> TruncatedImpulses { get; }

        static ElliottWavePatternHelper()
        {
            InitModelRulesStatic();

            ShallowCorrections = new HashSet<ElliottModelType>
            {
                ElliottModelType.COMBINATION,
                ElliottModelType.FLAT_EXTENDED,
                ElliottModelType.FLAT_RUNNING,
                ElliottModelType.TRIANGLE_CONTRACTING,
                ElliottModelType.TRIANGLE_RUNNING
            };

            DeepCorrections = new HashSet<ElliottModelType>
            {
                ElliottModelType.ZIGZAG,
                ElliottModelType.DOUBLE_ZIGZAG
            };

            DiagonalImpulses = new HashSet<ElliottModelType>
            {
                ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
                ElliottModelType.DIAGONAL_CONTRACTING_ENDING
            };

            TruncatedImpulses = new HashSet<ElliottModelType>
            {
                ElliottModelType.IMPULSE,
                ElliottModelType.DIAGONAL_CONTRACTING_ENDING
            };
        }

        

        public static Dictionary<ElliottModelType, ModelRules> ModelRules
        { get; private set; }

        private static void InitModelRulesStatic()
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
                                CORRECTION_Y, new[]
                                {
                                    ElliottModelType.ZIGZAG
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

            ModelRules[ElliottModelType.SIMPLE_IMPULSE] =
                ModelRules[ElliottModelType.IMPULSE] with
                { ProbabilityCoefficient = 0.25 };

            ModelRules[ElliottModelType.FLAT_REGULAR] =
                ModelRules[ElliottModelType.FLAT_EXTENDED] with
                { ProbabilityCoefficient = 0.0005 };

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
        }
    }
}
