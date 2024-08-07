﻿using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Json;
using HelperEw = TradeKit.Core.AlgoBase.ElliottWavePatternHelper;

namespace TradeKit.Core.PatternGeneration
{
    public class PatternGenerator
    {
        private readonly bool m_GenerateExtraInfo;

        #region Fields & consts

        private readonly Random m_Random;
        private const int SIMPLE_BARS_THRESHOLD = 100;
        private const byte MAX_DEEP_LEVEL = 10;
        private const double MAIN_ALLOWANCE_MAX_RATIO = 0.05;
        private const double MAIN_ALLOWANCE_MAX_RATIO_INVERT = 1 - MAIN_ALLOWANCE_MAX_RATIO;
        private const double MAIN_ALLOWANCE_MAX_RATIO_ONE_PLUS = 1 + MAIN_ALLOWANCE_MAX_RATIO;

        public static Dictionary<ElliottModelType, ModelRules> ModelRules => HelperEw.ModelRules;

        private Dictionary<ElliottModelType, Func<PatternArgsItem, ModelPattern>> m_ModelGeneratorsMap;

        private HashSet<ElliottModelType> m_ShallowCorrections;
        private HashSet<ElliottModelType> m_DeepCorrections;
        private HashSet<ElliottModelType> m_DiagonalImpulses;
        private HashSet<ElliottModelType> m_TruncatedImpulses;

        private HashSet<ElliottModelType> m_Wave1Impulse;
        private HashSet<ElliottModelType> m_Wave2Impulse;
        private HashSet<ElliottModelType> m_Wave4Impulse;
        private HashSet<ElliottModelType> m_Wave5Impulse;
        private Dictionary<TimeFrameInfo, Func<DateTime, DateTime>> m_TimeFrames;
        private ElliottModelType[] m_ImpulseOnly;
        
        #endregion
        
        public PatternGenerator(bool generateExtraInfo)
        {
            m_GenerateExtraInfo = generateExtraInfo;
            
            InitModelRules();
            m_Random = new Random();
        }

        #region Models init

        private void InitModelRules()
        {
            m_ModelGeneratorsMap = new Dictionary<ElliottModelType, 
                Func<PatternArgsItem, ModelPattern>>
            {
                {ElliottModelType.IMPULSE, GetImpulse},
                {ElliottModelType.SIMPLE_IMPULSE, GetSimpleImpulse},
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

            m_ShallowCorrections = HelperEw.ShallowCorrections;
            m_DeepCorrections = HelperEw.DeepCorrections;
            m_DiagonalImpulses = HelperEw.DiagonalImpulses;
            m_TruncatedImpulses = HelperEw.TruncatedImpulses;

            ModelRules impulse = ModelRules[ElliottModelType.IMPULSE];
            m_Wave1Impulse = impulse.Models[HelperEw.IMPULSE_ONE].ToHashSet();
            m_Wave2Impulse = impulse.Models[HelperEw.IMPULSE_TWO].ToHashSet();
            m_Wave4Impulse = impulse.Models[HelperEw.IMPULSE_FOUR].ToHashSet();
            m_Wave5Impulse = impulse.Models[HelperEw.IMPULSE_FIVE].ToHashSet();

            m_ImpulseOnly = new[] { ElliottModelType.IMPULSE };
            m_TimeFrames = new Dictionary<TimeFrameInfo, Func<DateTime, DateTime>>()
            {
                {
                    TimeFrameHelper.TimeFrames[TimeFrameHelper.Minute1.Name],
                    a => new DateTime(a.Year, a.Month, a.Day, a.Hour, a.Minute, 0)
                },
                {
                    TimeFrameHelper.TimeFrames[TimeFrameHelper.Minute5.Name],
                    a => new DateTime(
                        a.Year, a.Month, a.Day, a.Hour, a.Minute - a.Minute % 5, 0)
                },
                {
                    TimeFrameHelper.TimeFrames[TimeFrameHelper.Minute15.Name],
                    a => new DateTime(
                        a.Year, a.Month, a.Day, a.Hour, a.Minute - a.Minute % 15, 0)
                },
                {
                    TimeFrameHelper.TimeFrames[TimeFrameHelper.Hour1.Name],
                    a => new DateTime(a.Year, a.Month, a.Day, a.Hour, 0, 0)
                },
                {
                    TimeFrameHelper.TimeFrames[TimeFrameHelper.Hour4.Name],
                    a => new DateTime(a.Year, a.Month, a.Day, a.Hour - a.Hour % 4, 0, 0)
                },
                {
                    TimeFrameHelper.TimeFrames[TimeFrameHelper.Day1.Name],
                    a => new DateTime(a.Year, a.Month, a.Day)
                }
            };
        }

        #endregion

        /// <summary>
        /// Gets the pattern according to the passed data.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <param name="model">The model type.</param>
        /// <param name="useScaleFrom1M">If true, the candles will be scaled from 1m time frame to the requested one. This can increase the emulation quality.</param>
        /// <exception cref="ArgumentException">BarsCount</exception>
        /// <exception cref="NotSupportedException">Not supported model {model}</exception>
        public ModelPattern GetPattern(
            PatternArgsItem args, ElliottModelType model, bool useScaleFrom1M = false)
        {
            TimeFrameInfo tfInfo = TimeFrameHelper.TimeFrames[args.TimeFrame.Name];
            if (!m_TimeFrames.ContainsKey(tfInfo))
                throw new ArgumentException(nameof(args.TimeFrame));

            ITimeFrame originalTimeFrame = args.TimeFrame;
            args.RecalculateDates(m_TimeFrames[tfInfo](args.DateStart),
                m_TimeFrames[tfInfo](args.DateEnd),
                useScaleFrom1M ? TimeFrameHelper.Minute1 : args.TimeFrame);

            ModelPattern modelPattern = GetPatternInner(args, model);
            ValidateAndCorrectCandles(modelPattern, args.Accuracy);
            if (useScaleFrom1M)
            {
                TimeSpan ts = tfInfo.TimeSpan;
                DateTime currentDate = args.DateStart;
                DateTime nextDate = currentDate.Add(ts);

                double? open = null;
                double? close = null;
                double? high = null;
                double? low = null;

                DateTime highDateTime = DateTime.MinValue;
                DateTime lowDateTime = DateTime.MinValue;
                List<PatternKeyPoint> patternKeys = null;
                var keysMap = new Dictionary<DateTime, List<PatternKeyPoint>>();

                List<JsonCandleExport> scaledCandles = new List<JsonCandleExport>();
                void Add()
                {
                    var jsCandle = new JsonCandleExport
                    {
                        O = open.GetValueOrDefault(),
                        H = high.GetValueOrDefault(),
                        L = low.GetValueOrDefault(),
                        C = close.GetValueOrDefault(),
                        OpenDate = currentDate,
                        IsHighFirst = highDateTime < lowDateTime
                    };

                    scaledCandles.Add(jsCandle);
                    if (patternKeys != null) keysMap[jsCandle.OpenDate] = patternKeys;
                }

                foreach (JsonCandleExport candle in
                         modelPattern.Candles.OrderBy(a => a.OpenDate))
                {
                    if (candle.OpenDate > nextDate)
                    {
                        Add();
                        currentDate = nextDate;
                        nextDate = nextDate.Add(ts);
                        open = null;
                        high = null;
                        low = null;
                        highDateTime = DateTime.MinValue;
                        lowDateTime = DateTime.MinValue;
                        patternKeys = null;
                    }

                    if (modelPattern.PatternKeyPoints.ContainsKey(candle.OpenDate))
                    {
                        List<PatternKeyPoint> levels =
                            modelPattern.PatternKeyPoints[candle.OpenDate]
                                .Where(a => a.Notation.Level == modelPattern.Level)
                                .ToList();

                        if (levels.Count > 0)
                        {
                            patternKeys ??= new List<PatternKeyPoint>();
                            patternKeys.AddRange(levels);
                        }
                    }

                    open ??= candle.O;
                    close = candle.C;

                    if (!high.HasValue || candle.H > high)
                    {
                        high = candle.H;
                        highDateTime = candle.OpenDate;
                    }

                    if (!low.HasValue || candle.L < low)
                    {
                        low = candle.L;
                        lowDateTime = candle.OpenDate;
                    }
                }

                Add();
                modelPattern.Candles = scaledCandles;
                modelPattern.PatternKeyPoints.Clear();

                foreach (KeyValuePair<DateTime, List<PatternKeyPoint>> map in keysMap)
                    modelPattern.PatternKeyPoints.Add(map.Key, map.Value);
            }

            args.RecalculateDates(args.DateStart, args.DateEnd, originalTimeFrame);
            return modelPattern;
        }

        #region Main patterns

        private ModelPattern GetPatternInner(PatternArgsItem args, ElliottModelType model)
        {
            if (args.BarsCount <= 0)
                throw new ArgumentException(nameof(args.BarsCount));
            
            if (m_ModelGeneratorsMap.TryGetValue(model,
                    out Func<PatternArgsItem, ModelPattern> actionGen))
            {
                ModelPattern modelResult = actionGen(args);
                return modelResult;
            }

            throw new NotSupportedException($"Not supported model {model}");
        }

        private ModelPattern GetSimpleImpulse(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.SIMPLE_IMPULSE, arg.Candles);
            GetImpulseRandomSet(arg);
            return modelPattern;
        }

        private ModelPattern GetImpulse(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.IMPULSE, arg.Candles);
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
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

            double wave4To1Len = Math.Abs(wave4 - wave1);

            double wave4Limit = arg.IsUp ? arg.Max : arg.Min;
            FillPattern(arg, modelPattern, bars4Gen,
                new[] {wave1, wave2, wave3, wave4, wave5},
                new[] {arg.StartValue, arg.StartValue, wave2, wave4Limit, wave4},
                new[]
                {
                    wave1,
                    wave1 + arg.IsUpK * wave4To1Len / 2, // don't want the running part of the 2nd to exceed the 4th.
                    wave3, // pass the truncation limit
                    wave4 - arg.IsUpK * wave4To1Len / 2, // can we allow the running part of the 4th wave exceed
                    // the 3rd wave?
                    wave5
                });

            if (!m_GenerateExtraInfo)
                return modelPattern;

            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.IMPULSE_THREE, HelperEw.IMPULSE_ONE, 
                        wave3Len / wave1Len),
                    new LengthRatio(HelperEw.IMPULSE_FIVE, HelperEw.IMPULSE_ONE, 
                        wave5Len / wave1Len),
                    new LengthRatio(HelperEw.IMPULSE_TWO, HelperEw.IMPULSE_ONE,
                        Math.Abs(wave1 - wave2) / wave1Len),
                    new LengthRatio(HelperEw.IMPULSE_FOUR, HelperEw.IMPULSE_THREE,
                        Math.Abs(wave3 - wave4) / wave3Len),
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.IMPULSE_FOUR, HelperEw.IMPULSE_TWO, wave4Dur / wave2Dur));
            return modelPattern;
        }

        private ModelPattern GetExtendedFlat(PatternArgsItem arg)
        {
            double bLimit = arg.IsUp ? arg.Min : arg.Max;
            if (arg.IsUp && bLimit >= arg.StartValue ||
                !arg.IsUp && bLimit <= arg.StartValue)
            {
                // unable to do the running part
               return GetZigzag(arg);
            };

            List<JsonCandleExport> candles = arg.Candles;
            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_EXTENDED, candles);

            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            // TODO use logic from the running flat?
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

            int[] bars4Gen = SplitByTree(arg.BarsCount);
            double waveC = arg.EndValue;

            FillPattern(arg, modelPattern, bars4Gen,
                new[] {waveA, waveB, waveC},
                new[] {waveC, waveB, arg.IsUp ? arg.Max : arg.Min },
                new[] {waveB, waveC, waveB });
            
            if (!m_GenerateExtraInfo)
                return modelPattern;

            double waveBLength = Math.Abs(waveA - waveB);
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.CORRECTION_C, HelperEw.CORRECTION_A, 
                        waveCLength / waveALength),
                    new LengthRatio(HelperEw.CORRECTION_B, HelperEw.CORRECTION_A, 
                        waveBLength / waveALength)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.CORRECTION_B, HelperEw.CORRECTION_A,
                    (double)bars4Gen[1] / bars4Gen[0]));

            return modelPattern;
        }

        private ModelPattern GetRegularFlat(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_REGULAR, arg.Candles);
            
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            double bToA = RandomWithinRange(0.9, 1);
            double cToA = SelectRandomly(MAP_REG_FLAT_WAVE_C_TO_A, bToA);
            double waveALen = arg.Range / (1 - bToA + cToA);
            double waveBLen = bToA * waveALen;

            double waveA = arg.StartValue + arg.IsUpK * waveALen;
            double waveB = waveA - arg.IsUpK * waveBLen;
            double waveC = arg.EndValue;

            int[] bars4Gen = SplitByTree(arg.BarsCount);

            FillPattern(arg, modelPattern, bars4Gen,
                new[] {waveA, waveB, waveC},
                new[] {arg.StartValue, waveC, arg.IsUp ? arg.Max : arg.Min},
                new[] {waveC, arg.StartValue, waveB});

            if (!m_GenerateExtraInfo)
                return modelPattern;

            double waveCLen = cToA * waveALen;
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.CORRECTION_C, HelperEw.CORRECTION_A,
                        waveCLen / waveALen)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.CORRECTION_B, HelperEw.CORRECTION_A,
                    (double)bars4Gen[1] / bars4Gen[0]));

            return modelPattern;
        }

        private ModelPattern GetTripleZigzag(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.TRIPLE_ZIGZAG, arg.Candles);
            
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            Dictionary<string, ElliottModelType[]> models
                = ModelRules[modelPattern.Model].Models;
            ElliottModelType theModelX =
                WeightedRandomlySelectModel(models[HelperEw.CORRECTION_X]);
            ElliottModelType theModelXx =
                WeightedRandomlySelectModel(models[HelperEw.CORRECTION_XX]);

            double waveZ = arg.EndValue;
            double xzToW = SelectRandomly(ZIGZAG_X_Z_TO_W);
            double xToW = SelectRandomly(m_ShallowCorrections.Contains(theModelX)
                ? MAP_SHALLOW_CORRECTION
                : MAP_DEEP_CORRECTION, double.NaN, Math.Min(xzToW, 1));

            double waveWLen = arg.Range / (1 - xToW + xzToW);
            double waveXLen = waveWLen * xToW;

            double waveW = arg.StartValue + arg.IsUpK * waveWLen;
            double waveX = waveW - arg.IsUpK * waveXLen;
            
            double xxToY = SelectRandomly(m_ShallowCorrections.Contains(theModelX)
                ? MAP_SHALLOW_CORRECTION
                : MAP_DEEP_CORRECTION);
            
            double restWzLen = Math.Abs(waveW - waveZ);
            double waveYLen = RandomWithinRange(
                waveXLen + restWzLen * MAIN_ALLOWANCE_MAX_RATIO,
                waveXLen + restWzLen * MAIN_ALLOWANCE_MAX_RATIO_INVERT);

            double waveXxLen = waveYLen * xxToY;
            double waveY = waveX + arg.IsUpK * waveYLen;
            double waveXx = waveY - arg.IsUpK * waveXxLen;

            int[] bars4Gen = SplitByN(arg.BarsCount, 5);

            FillPattern(arg, modelPattern, bars4Gen,
                new[] {waveW, waveX, waveY, waveXx, waveZ},
                new[] {waveW, arg.StartValue, waveY, waveX, waveXx},
                new[] {arg.StartValue, waveY, waveX, waveZ, waveZ},
                new[]
                {
                    WeightedRandomlySelectModel(models[HelperEw.CORRECTION_W]), 
                    theModelX,
                    WeightedRandomlySelectModel(models[HelperEw.CORRECTION_Y]),
                    theModelXx,
                    WeightedRandomlySelectModel(models[HelperEw.CORRECTION_Z])
                });

            if (!m_GenerateExtraInfo)
                return modelPattern;

            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.CORRECTION_X, HelperEw.CORRECTION_W, xToW)
                });

            modelPattern.DurationRatios.AddRange(
                new[]
                {
                    new DurationRatio(HelperEw.CORRECTION_X, HelperEw.CORRECTION_W,
                        (double) bars4Gen[1] / bars4Gen[0])
                });

            return modelPattern;
        }

        private ModelPattern GetExpandingTriangle(PatternArgsItem arg)
        {
            double dLimit = arg.IsUp ? arg.Min : arg.Max;
            if (arg.IsUp && dLimit >= arg.StartValue ||
                !arg.IsUp && dLimit <= arg.StartValue)
            {
                // We cannot perform the running part, so we replace it with ZZ.
                return GetZigzag(arg);
            };

            var modelPattern = new ModelPattern(
                ElliottModelType.TRIANGLE_EXPANDING, arg.Candles);
            
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            double waveALen = arg.Range * RandomWithinRange(0.2, 0.7);
            double waveA = arg.StartValue + arg.IsUpK * waveALen;
            double waveE = arg.EndValue;

            double waveDMaxAdd = Math.Abs(arg.StartValue - dLimit);
            double bToA = SelectRandomly(MAP_EXPANDING_TRIANGLE_WAVE_NEXT_TO_PREV,
                (waveALen + waveDMaxAdd * MAIN_ALLOWANCE_MAX_RATIO) / waveALen,
                (waveALen + waveDMaxAdd / 2) / waveALen);
            double waveBLen = waveALen * bToA;
            double waveB = waveA - arg.IsUpK * waveBLen;

            double waveAToE = Math.Abs(waveA - waveE);
            double cToB = SelectRandomly(MAP_EXPANDING_TRIANGLE_WAVE_NEXT_TO_PREV,
                (waveBLen + waveAToE * MAIN_ALLOWANCE_MAX_RATIO) / waveBLen,
                (waveBLen + waveAToE*MAIN_ALLOWANCE_MAX_RATIO_INVERT) / waveBLen);
            double waveCLen = waveBLen * cToB;
            
            double waveBMaxAdd = Math.Abs(waveB - dLimit);
            double waveC = waveB + arg.IsUpK * waveCLen;
            double dToC = SelectRandomly(MAP_EXPANDING_TRIANGLE_WAVE_NEXT_TO_PREV,
                (waveCLen + waveBMaxAdd * MAIN_ALLOWANCE_MAX_RATIO) / waveCLen,
                (waveCLen + waveBMaxAdd * MAIN_ALLOWANCE_MAX_RATIO_INVERT) / waveCLen);

            double waveDLen = waveCLen * dToC;
            double waveD = waveC - arg.IsUpK * waveDLen;

            int[] bars4Gen = PatternGenKit.SplitNumber(
                arg.BarsCount, new[]
                {
                    0.10 * RandomBigRatio(),
                    0.15 * RandomBigRatio(),
                    0.15 * RandomBigRatio(),
                    0.25 * RandomBigRatio(),
                    0.35 * RandomBigRatio()
                });

            FillPattern(arg, modelPattern, bars4Gen,
                new[] {waveA, waveB, waveC, waveD, waveE},
                new[] {waveA, waveB, waveC, waveD, waveE},
                new[] {arg.StartValue, waveA, waveB, waveC, waveD });

            if (!m_GenerateExtraInfo)
                return modelPattern;

            double eWaveLen = Math.Abs(waveD - waveE);
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.CORRECTION_E, HelperEw.CORRECTION_A,
                        eWaveLen / waveALen)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.CORRECTION_E, HelperEw.CORRECTION_A,
                    (double)bars4Gen[4] / bars4Gen[0]));

            return modelPattern;
        }

        private ModelPattern GetCombination(PatternArgsItem arg)
        {
            if (arg.IsUp && arg.Min >= arg.StartValue ||
                !arg.IsUp && arg.Max <= arg.StartValue || 
                arg.IsUp && arg.Max <= arg.EndValue ||
                !arg.IsUp && arg.Min >= arg.EndValue)
            {
                // We cannot perform the running part, so we replace it with DZZ.
               return GetDoubleZigzag(arg);
            };

            var modelPattern = new ModelPattern(ElliottModelType.COMBINATION, arg.Candles);
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            double max = arg.Max;
            double min = arg.Min;

            double localMax = Math.Max(arg.EndValue, arg.StartValue);
            double localMin = Math.Min(arg.EndValue, arg.StartValue);
            if (Math.Abs(localMax - arg.Max) > arg.Range) max = localMax + arg.Range;
            if (Math.Abs(localMin - arg.Min) > arg.Range) min = localMin - arg.Range;
            
            double maxWaveWLen = Math.Abs(arg.StartValue - (arg.IsUp ? max : min));
            double waveWLen = maxWaveWLen * RandomWithinRange(
                0.3, MAIN_ALLOWANCE_MAX_RATIO_INVERT);

            double waveW = arg.StartValue + arg.IsUpK * waveWLen;
            double maxWaveXLen = Math.Abs(waveW - (arg.IsUp ? min : max));
            double waveXLen = maxWaveXLen * RandomWithinRange(
                0.3, MAIN_ALLOWANCE_MAX_RATIO_INVERT);

            double waveX = waveW - arg.IsUpK * waveXLen;
            double waveY = arg.EndValue;
            Dictionary<string, ElliottModelType[]> models =
                ModelRules[modelPattern.Model].Models;

            ElliottModelType modelW = WeightedRandomlySelectModel(
                models[HelperEw.CORRECTION_W]);
            ElliottModelType modelX = WeightedRandomlySelectModel(
                models[HelperEw.CORRECTION_X]);
            ElliottModelType modelY = WeightedRandomlySelectModel(
                models[HelperEw.CORRECTION_Y]);

            int[] bars4Gen = PatternGenKit.SplitNumber(
                arg.BarsCount, new[]
                {
                    (m_ShallowCorrections.Contains(modelW) ? 0.35 : 0.2) * RandomBigRatio(),
                    (m_ShallowCorrections.Contains(modelX) ? 0.35 : 0.2) * RandomBigRatio(),
                    (m_ShallowCorrections.Contains(modelY) ? 0.35 : 0.2) * RandomBigRatio(),
                });

            FillPattern(arg, modelPattern, bars4Gen,
                new[] {waveW, waveX, waveY},
                new[] {max, max, max},
                new[] {min, min, min},
                new[] {modelW, modelX, modelY});

            if (!m_GenerateExtraInfo)
                return modelPattern;

            double waveYLen = Math.Abs(waveY - waveX);
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.CORRECTION_Y, 
                        HelperEw.CORRECTION_W, waveYLen / waveWLen),
                    new LengthRatio(HelperEw.CORRECTION_X, 
                        HelperEw.CORRECTION_W, waveXLen / waveWLen)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.CORRECTION_Y, HelperEw.CORRECTION_W,
                    (double) bars4Gen[2] / bars4Gen[0]));

            return modelPattern;
        }

        private ModelPattern GetDoubleZigzag(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DOUBLE_ZIGZAG, arg.Candles);
            
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }
            
            // TODO o-x line?
            Dictionary<string, ElliottModelType[]> models
                = ModelRules[modelPattern.Model].Models;
            ElliottModelType theModelX = WeightedRandomlySelectModel(
                models[HelperEw.CORRECTION_X]);

            // use the same ratios from zz
            double yToW = SelectRandomly(ZIGZAG_C_TO_A);

            bool isShallow = m_ShallowCorrections.Contains(theModelX);
            double xToW = SelectRandomly(isShallow
                ? MAP_SHALLOW_CORRECTION
                : MAP_DEEP_CORRECTION, double.NaN, Math.Min(yToW, 1));

            double waveWLen = arg.Range / (1 - xToW + yToW);

            double waveW = arg.StartValue + arg.IsUpK * waveWLen;
            double waveXLen = waveWLen * xToW;
            double waveX = waveW - arg.IsUpK * waveXLen;
            double waveY = arg.EndValue;

            int[] bars4Gen = SplitByTree(arg.BarsCount);
            FillPattern(arg, modelPattern, bars4Gen,
                new[] {waveW, waveX, waveY},
                new[] {waveW, arg.StartValue, waveX},
                new[] {arg.StartValue, waveY, waveY}, new[]
                {
                    WeightedRandomlySelectModel(models[HelperEw.CORRECTION_W]),
                    theModelX,
                    WeightedRandomlySelectModel(models[HelperEw.CORRECTION_Y])
                });

            if (!m_GenerateExtraInfo)
                return modelPattern;

            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.CORRECTION_Y, HelperEw.CORRECTION_W, yToW),
                    new LengthRatio(HelperEw.CORRECTION_X, HelperEw.CORRECTION_W, xToW)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.CORRECTION_Y, HelperEw.CORRECTION_W,
                    (double)bars4Gen[2] / bars4Gen[0]));

            return modelPattern;
        }

        private ModelPattern GetZigzag(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.ZIGZAG, arg.Candles);

            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            Dictionary<string, ElliottModelType[]> models 
                = ModelRules[modelPattern.Model].Models;
            ElliottModelType theModelB = WeightedRandomlySelectModel(
                models[HelperEw.CORRECTION_B]);

            double cToA = SelectRandomly(ZIGZAG_C_TO_A);

            bool isShallow = m_ShallowCorrections.Contains(theModelB);
            double bToA = SelectRandomly(isShallow
                ? MAP_SHALLOW_CORRECTION
                : MAP_DEEP_CORRECTION, double.NaN, Math.Min(cToA, 1));

            double waveALen = arg.Range / (1 - bToA + cToA);

            double waveA = arg.StartValue + arg.IsUpK * waveALen;
            double waveBLen = waveALen * bToA;
            double waveB = waveA - arg.IsUpK * waveBLen;
            double waveC = arg.EndValue;
            
            int[] bars4Gen = SplitByTree(arg.BarsCount);

            ElliottModelType theAModel =
                WeightedRandomlySelectModel(models[HelperEw.CORRECTION_A]);

            ElliottModelType[] modelsForC;
            if (m_Random.NextDouble() > 0.2)// impulse/diagonal change in 80% cases
            {
                modelsForC = (theAModel == ElliottModelType.IMPULSE
                        ? models[HelperEw.CORRECTION_C].Except(m_ImpulseOnly)
                        : m_ImpulseOnly)
                    .ToArray();
            }
            else
            {
                modelsForC = models[HelperEw.CORRECTION_C];
            }

            ElliottModelType theCModel = WeightedRandomlySelectModel(modelsForC);

            ElliottModelType[] definedModels = {theAModel, theModelB, theCModel};
            FillPattern(arg, modelPattern, bars4Gen,
                new[] {waveA, waveB, waveC},
                new[] {waveA, arg.StartValue, arg.IsUp ? arg.Max : arg.Min},
                new[] {arg.StartValue, waveC, waveB}, 
                definedModels);

            if (!m_GenerateExtraInfo)
                return modelPattern;

            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.CORRECTION_C, HelperEw.CORRECTION_A, cToA),
                    new LengthRatio(HelperEw.CORRECTION_B, HelperEw.CORRECTION_A, bToA)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.CORRECTION_B, HelperEw.CORRECTION_A,
                    (double)bars4Gen[1] / bars4Gen[0]));

            return modelPattern;
        }

        private ModelPattern GetRunningTriangle(PatternArgsItem arg)
        {
            double bLimit = arg.IsUp ? arg.Min : arg.Max;

            var pattern = ElliottModelType.TRIANGLE_RUNNING;
            var useRunning = true;
            if (arg.IsUp && bLimit >= arg.StartValue ||
                !arg.IsUp && bLimit <= arg.StartValue)
            {
                // replace with common triangle
                pattern = ElliottModelType.TRIANGLE_CONTRACTING;
                useRunning = false;
            };

            var modelPattern = new ModelPattern(pattern, arg.Candles);
            
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            modelPattern = GetContractingTriangle(arg, useRunning, pattern);
            return modelPattern;
        }

        private ModelPattern GetContractingTriangle(PatternArgsItem arg)
        {
            return GetContractingTriangle(arg, false, 
                ElliottModelType.TRIANGLE_CONTRACTING);
        }

        private ModelPattern GetContractingTriangle(
            PatternArgsItem arg, bool useRunning, ElliottModelType model)
        {
            double aLimit = arg.IsUp ? arg.Max : arg.Min;

            if (arg.IsUp && aLimit <= arg.EndValue ||
                !arg.IsUp && aLimit >= arg.EndValue)
            {
                // We cannot perform the running part, so we replace it with ZZ.
                return GetZigzag(arg);
            };

            var modelPattern = new ModelPattern(model, arg.Candles);
            
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            double waveE = arg.EndValue;
            
            double restRange = Math.Abs(waveE - aLimit);
            double startAddRange = arg.Range + MAIN_ALLOWANCE_MAX_RATIO * restRange;
            double endAddRange = arg.Range + 
                                 restRange * MAIN_ALLOWANCE_MAX_RATIO_INVERT;

            double doubleRange = arg.Range * 2;
            double aWaveLen = doubleRange < endAddRange && doubleRange > startAddRange
                ? PatternGenKit.GetNormalDistributionNumber(m_Random, startAddRange, endAddRange, doubleRange)
                : RandomWithinRange(startAddRange, endAddRange);

            double waveA = arg.StartValue + arg.IsUpK * aWaveLen;
            double waveADiffE = Math.Abs(waveA - waveE);

            double runningPoint = arg.IsUp ? arg.Min : arg.Max;
            double rangeBLen = Math.Abs(arg.EndValue - runningPoint);
            double rangeBLenMin = useRunning 
                ? aWaveLen
                : rangeBLen * MAIN_ALLOWANCE_MAX_RATIO;

            double rangeBLenMax = useRunning
                ? aWaveLen +
                  Math.Abs(arg.StartValue - runningPoint) * MAIN_ALLOWANCE_MAX_RATIO_INVERT
                : rangeBLen * MAIN_ALLOWANCE_MAX_RATIO_INVERT;

            double bToA = SelectRandomly(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, 
                (waveADiffE + rangeBLenMin) / aWaveLen,
                (waveADiffE + rangeBLenMax) / aWaveLen);

            double bWaveLen = bToA * aWaveLen;
            double waveB = waveA - arg.IsUpK * bWaveLen;
            double waveBDiffE = Math.Abs(waveB - waveE);
            
            double cToB = SelectRandomly(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV,
                (waveBDiffE + waveADiffE * MAIN_ALLOWANCE_MAX_RATIO) / bWaveLen,
                1);

            double cWaveLen = cToB * bWaveLen;
            double waveC = waveB + arg.IsUpK * cWaveLen;
            double waveCDiffE = Math.Abs(waveC - waveE);

            double dToC = SelectRandomly(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV,
                (waveCDiffE + waveBDiffE * MAIN_ALLOWANCE_MAX_RATIO) / cWaveLen,
                1);
            double dWaveLen = dToC * cWaveLen;
            double waveD = waveC - arg.IsUpK * dWaveLen;
            
            int[] bars4Gen = PatternGenKit.SplitNumber(
                arg.BarsCount, new[]
                {
                    0.35 * RandomBigRatio(),
                    0.25 * RandomBigRatio(),
                    0.15 * RandomBigRatio(),
                    0.15 * RandomBigRatio(),
                    0.10 * RandomBigRatio()
                });
            
            Dictionary<string, ElliottModelType[]> models = 
                ModelRules[modelPattern.Model].Models;

            bool allowTriangleInE = m_Random.NextDouble() < 0.01;
            ElliottModelType[] patterns = 
            {
                WeightedRandomlySelectModel(models[HelperEw.CORRECTION_A]),
                WeightedRandomlySelectModel(models[HelperEw.CORRECTION_B]),
                WeightedRandomlySelectModel(models[HelperEw.CORRECTION_C]),
                WeightedRandomlySelectModel(models[HelperEw.CORRECTION_D]),
                allowTriangleInE
                    ? ElliottModelType.TRIANGLE_CONTRACTING
                    : WeightedRandomlySelectModel(models[HelperEw.CORRECTION_E]),
            }; // We can allow only one dzz here, but looks like we don't need to.

            FillPattern(arg, modelPattern, bars4Gen,
                new[] {waveA, waveB, waveC, waveD, waveE},
                new[] {waveA, waveB, waveC, waveD, waveC },
                new[] {arg.StartValue, waveA, waveB, waveC, waveD},
                patterns);

            if (!m_GenerateExtraInfo)
                return modelPattern;

            double eWaveLen = Math.Abs(waveD - waveE);
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.CORRECTION_E, HelperEw.CORRECTION_A,
                        eWaveLen / aWaveLen)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.CORRECTION_E, HelperEw.CORRECTION_A,
                    (double) bars4Gen[4] / bars4Gen[0]));

            return modelPattern;
        }

        private ModelPattern GetRunningFlat(PatternArgsItem arg)
        {
            double bLimit = arg.IsUp ? arg.Min : arg.Max;
            double aLimit = arg.IsUp ? arg.Max : arg.Min;
            if (arg.IsUp && bLimit >= arg.StartValue ||
                !arg.IsUp && bLimit <= arg.StartValue ||
                arg.IsUp && aLimit <= arg.EndValue ||
                !arg.IsUp && aLimit >= arg.EndValue)
            {
                // We cannot perform the running part, so we replace it with ZZ.
                return GetZigzag(arg);
            }

            var modelPattern = new ModelPattern(
                ElliottModelType.FLAT_RUNNING, arg.Candles);
            
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }
            
            double waveALenAdd =  Math.Abs(aLimit - arg.EndValue);
            double waveALenAddMin = waveALenAdd * MAIN_ALLOWANCE_MAX_RATIO;
            double waveALenAddMax = waveALenAdd * MAIN_ALLOWANCE_MAX_RATIO_INVERT;

            double waveALen = PatternGenKit.GetNormalDistributionNumber(
                m_Random, arg.Range + waveALenAddMin,
                arg.Range + waveALenAddMax,
                arg.Range + waveALenAdd * 0.2
            );

            double waveCLenAddMax = Math.Abs(bLimit - arg.StartValue);
            double cToA = SelectRandomly(MAP_RUNNING_FLAT_WAVE_C_TO_A, 
                (arg.Range + waveCLenAddMax * MAIN_ALLOWANCE_MAX_RATIO)/waveALen,
                (arg.Range + waveCLenAddMax * MAIN_ALLOWANCE_MAX_RATIO_INVERT) / waveALen);
            double waveCLen = waveALen * cToA;

            double waveA = arg.StartValue + arg.IsUpK * waveALen;
            double waveC = arg.EndValue;
            double waveB = waveC - arg.IsUpK * waveCLen;

            int[] bars4Gen = SplitByTree(arg.BarsCount);
            FillPattern(arg, modelPattern, bars4Gen,
                new[] {waveA, waveB, waveC},
                new[] {arg.IsUp ? arg.Max : arg.Min, arg.Min, waveB},
                new[] {waveB, arg.Max, arg.IsUp ? arg.Max : arg.Min});

            if (!m_GenerateExtraInfo)
                return modelPattern;

            double waveBLen = Math.Abs(waveC - waveB);
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.CORRECTION_C, HelperEw.CORRECTION_A,
                        waveCLen / waveALen),
                    new LengthRatio(HelperEw.CORRECTION_B, HelperEw.CORRECTION_A,
                        waveBLen / waveALen)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.CORRECTION_B, HelperEw.CORRECTION_A,
                    (double)bars4Gen[1] / bars4Gen[0]));

            return modelPattern;
        }

        private ModelPattern GetEndingDiagonal(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DIAGONAL_CONTRACTING_ENDING, arg.Candles);

            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
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

            double wave5 = arg.EndValue;
            FillPattern(arg, modelPattern, bars4Gen,
                new[] {wave1, wave2, wave3, wave4, wave5},
                new[] {wave1, wave2, wave5, wave4, arg.IsUp ? arg.Max : arg.Min},
                new[] {arg.StartValue, wave1, wave2, wave3, wave3});

            if (!m_GenerateExtraInfo)
                return modelPattern;

            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.IMPULSE_THREE, HelperEw.IMPULSE_ONE,
                        wave3Len / wave1Len)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.IMPULSE_THREE, HelperEw.IMPULSE_ONE,
                    (double) bars4Gen[2] / bars4Gen[1]));

            return modelPattern;
        }

        private ModelPattern GetInitialDiagonal(PatternArgsItem arg)
        {
            var modelPattern = new ModelPattern(
                ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, arg.Candles);

            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetImpulseRandomSet(arg);
                return modelPattern;
            }

            return GetDiagonal(arg, modelPattern);
        }

        private ModelPattern GetExpandingDiagonal(
            PatternArgsItem arg, ElliottModelType model)
        {
            var modelPattern = new ModelPattern(model, arg.Candles);
            if (arg.BarsCount < SIMPLE_BARS_THRESHOLD || arg.LevelDeep >= MAX_DEEP_LEVEL)
            {
                GetCorrectiveRandomSet(arg);
                return modelPattern;
            }

            double wave1Len = arg.Range * RandomWithinRange(0.1, 0.4);
            double wave1 = arg.StartValue + arg.IsUpK * wave1Len;
            double wave5 = arg.EndValue;

            double wave2Len = wave1Len * SelectRandomly(MAP_DEEP_CORRECTION);
            double wave2 = wave1 - arg.IsUpK * wave2Len;

            double wave1RestLen = Math.Abs(wave1 - wave5);

            // we use ratios for the expanded triangle
            double wave3Len = wave1Len * SelectRandomly(MAP_EXPANDING_TRIANGLE_WAVE_NEXT_TO_PREV,
                (wave1Len + wave1RestLen * MAIN_ALLOWANCE_MAX_RATIO) / wave1Len,
                (wave1Len + wave1RestLen * MAIN_ALLOWANCE_MAX_RATIO_INVERT) / wave1Len);

            double wave3 = wave2 + arg.IsUpK * wave3Len;

            double wave3Minus2 = Math.Abs(wave3Len - wave2Len);
            double wave4Len = wave2Len * SelectRandomly(MAP_EXPANDING_TRIANGLE_WAVE_NEXT_TO_PREV,
                (wave2Len + wave3Minus2 * MAIN_ALLOWANCE_MAX_RATIO) / wave2Len,
                (wave2Len + wave3Minus2 * MAIN_ALLOWANCE_MAX_RATIO_INVERT) / wave2Len);

            double wave4 = wave3 - arg.IsUpK * wave4Len;
            int[] bars4Gen = PatternGenKit.SplitNumber(
                arg.BarsCount, new[]
                {
                    0.1 * RandomBigRatio(),
                    0.15 * RandomBigRatio(),
                    0.15 * RandomBigRatio(),
                    0.25 * RandomBigRatio(),
                    0.35 * RandomBigRatio(),
                });

            FillPattern(arg, modelPattern, bars4Gen,
                new[] {wave1, wave2, wave3, wave4, wave5},
                new[] {wave1, wave2, wave3, wave4, wave5},
                new[] {arg.StartValue, wave1, wave2, wave3, wave4 });

            if (!m_GenerateExtraInfo)
                return modelPattern;

            double wave5Len = Math.Abs(wave5 - wave4);
            modelPattern.LengthRatios.AddRange(
                new[]
                {
                    new LengthRatio(HelperEw.IMPULSE_FIVE, HelperEw.IMPULSE_ONE,
                        wave5Len / wave1Len)
                });

            modelPattern.DurationRatios.Add(
                new DurationRatio(HelperEw.IMPULSE_FIVE, HelperEw.IMPULSE_ONE,
                    (double)bars4Gen[4] / bars4Gen[0]));
            return modelPattern;
        }

        private ModelPattern GetInitialExpandingDiagonal(PatternArgsItem arg)
        {
            return GetExpandingDiagonal(arg, ElliottModelType.DIAGONAL_EXPANDING_INITIAL);
        }

        private ModelPattern GetEndingExpandingDiagonal(PatternArgsItem arg)
        {
            return GetExpandingDiagonal(arg, ElliottModelType.DIAGONAL_EXPANDING_ENDING);
        }

        #endregion

        #region Simple sets

        public void GetImpulseRandomSet(PatternArgsItem args)
        {
            GetRandomSet(args, 0.2);
        }

        private void GetCorrectiveRandomSet(PatternArgsItem args)
        {
            GetImpulseRandomSet(args);
            //GetRandomSet(args, m_Random.Next(2, 5), true);
        }

        private void GetRandomSet(
            PatternArgsItem args, double variance = 1, bool useFullRange = false)
        {
            List<JsonCandleExport> candles = args.Candles;
            if (args.BarsCount <= 0) return;

            if (args.BarsCount == 1)
            {
                var cdl = new JsonCandleExport
                {
                    H = args.Max,
                    L = args.Min,
                    OpenDate = args.DateStart
                };

                FillBorderCandlesStart(args, cdl);
                FillBorderCandlesEnd(args, cdl);
                candles.Add(cdl);
                return;
            }

            if (variance <= 0)
                variance = 1;

            double previousClose = args.StartValue;
            double stepLinear = args.Range / args.BarsCount;
            TimeFrameInfo tfInfo = TimeFrameHelper.TimeFrames[args.TimeFrame.Name];
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
                        low = Math.Min(startExtremum, args.StartValue);
                    else
                        high = Math.Max(startExtremum, args.StartValue);

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
                    MAIN_ALLOWANCE_MAX_RATIO_INVERT) // throwout
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
                    C = close,
                    H = high.Value,
                    O = open,
                    L = low.Value,
                    OpenDate = args.DateStart.Add(tfInfo.TimeSpan * i)
                };

                candles.Add(cdl);
                previousClose = cdl.C;
            }

            JsonCandleExport endItem = candles[^1];
            FillBorderCandlesEnd(args, endItem);
        }

        #endregion

        #region Fibonacci ratios

        private static readonly
            SortedDictionary<byte, double> ZIGZAG_X_Z_TO_W =
                new() { { 0, 0 }, { 5, 1 }, { 25, 1.618 }, { 50, 2.618 }, { 80, 3.618 }, { 90, 4.236 } };

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
            SortedDictionary<byte, double> MAP_RUNNING_FLAT_WAVE_C_TO_A =
                new() { { 0, 0 }, { 5, 0.5 }, { 20, 0.618 }, { 80, 1 }, { 90, 1.272 }, { 95, 1.618 } };

        private static readonly
            SortedDictionary<byte, double> MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV =
                new() { { 0, 0 }, { 5, 0.5 }, { 20, 0.618 }, { 80, 0.786 }, { 90, 0.9 }, { 95, 0.95 } };

        private static readonly
            SortedDictionary<byte, double> MAP_EXPANDING_TRIANGLE_WAVE_NEXT_TO_PREV =
                new() { { 0, 0 }, { 5, 1.272 }, { 30, 1.618 }, { 80, 2.618 }, { 95, 3.618 } };

        #endregion

        #region Helpers

        private void ValidateAndCorrectCandles(
            ModelPattern modelPattern, int accuracy)
        {
            List<JsonCandleExport> candles = modelPattern.Candles;
            foreach (JsonCandleExport candle in candles)
            {
                candle.H = Math.Round(candle.H, accuracy);
                candle.L = Math.Round(candle.L, accuracy);
                candle.O = Math.Round(candle.O, accuracy);
                candle.C = Math.Round(candle.C, accuracy);
            }

            foreach (List<PatternKeyPoint> keyPointValues in modelPattern.PatternKeyPoints.Values)
            {
                foreach (PatternKeyPoint keyPointValue in keyPointValues)
                    keyPointValue.Value = Math.Round(keyPointValue.Value, accuracy);
            }

            if (candles.Count < 2) return;

            for (int i = 1; i < candles.Count; i++)
            {
                JsonCandleExport prevCandle = candles[i - 1];
                JsonCandleExport currentCandle = candles[i];

                if (Math.Abs(currentCandle.O - prevCandle.C) >= double.Epsilon)
                {
                    if (!(currentCandle.C < prevCandle.H) || 
                        !(currentCandle.C > prevCandle.L))
                    {
                        if (currentCandle.L < prevCandle.C && currentCandle.H > prevCandle.C)
                            currentCandle.O = prevCandle.C;
                    }
                    else
                    {
                        prevCandle.C = currentCandle.O;
                    }
                }

                if (currentCandle.H < currentCandle.O) 
                    currentCandle.H = currentCandle.O;

                if (currentCandle.H < currentCandle.C)
                    currentCandle.H = currentCandle.C;

                if (currentCandle.L > currentCandle.O)
                    currentCandle.L = currentCandle.O;

                if (currentCandle.L > currentCandle.C)
                    currentCandle.L = currentCandle.C;
            }
        }

        private void FillPattern(
            PatternArgsItem arg,
            ModelPattern pattern,
            int[] bars4Gen,
            double[] values,
            double[] directLimits,// middle points in the wave direction cannot be beyond this values
            double[] backLimits,// middle points in the opposite of the wave direction  cannot be beyond this values
            ElliottModelType[] definedModels = null)
        {
            Dictionary<string, ElliottModelType[]> models =
                ModelRules[pattern.Model].Models;
            string[] keys = models.Keys.ToArray();

            if (bars4Gen.Length == 0 ||
                bars4Gen.Length != models.Count ||
                bars4Gen.Length != keys.Length)
                throw new ArgumentException(nameof(pattern));

            if (arg.LevelDeep > 10)
            {

            }

            void AddKeyPoint(DateTime dt, PatternKeyPoint point)
            {
                if (pattern.PatternKeyPoints.ContainsKey(dt))
                    pattern.PatternKeyPoints[dt].Add(point);
                else
                    pattern.PatternKeyPoints[dt] =
                        new List<PatternKeyPoint> { point };
            }

            ModelPattern modelCurrent = null;
            bool isUp = arg.IsUp; 
            TimeFrameInfo tfInfo = TimeFrameHelper.TimeFrames[arg.TimeFrame.Name];
            for (int i = 0; i < values.Length; i++)
            {
                DateTime dateStart = modelCurrent == null
                    ? arg.DateStart
                    : arg.DateStart.Add(tfInfo.TimeSpan *
                                        bars4Gen.Take(i).Sum());

                DateTime dateEnd = arg.DateStart.Add(tfInfo.TimeSpan *
                                                     bars4Gen.Take(i + 1).Sum());

                var max = Math.Max(directLimits[i], backLimits[i]);
                var min = Math.Min(directLimits[i], backLimits[i]);

                var waveArg = new PatternArgsItem(modelCurrent == null ? arg.StartValue : values[i - 1],
                    values[i],
                    dateStart,
                    dateEnd,
                    arg.TimeFrame)
                {
                    Max = max,
                    Min = min,
                    LevelDeep = (byte) (arg.LevelDeep + 1)
                };

                ElliottModelType model = definedModels == null
                    ? WeightedRandomlySelectModel(models[keys[i]])
                    : definedModels[i];

                ModelPattern modelWave = GetPatternInner(waveArg, model);

                foreach (KeyValuePair<DateTime, List<PatternKeyPoint>> keyPoint
                         in modelWave.PatternKeyPoints)
                {
                    foreach (PatternKeyPoint kPointVal in keyPoint.Value)
                    {
                        AddKeyPoint(keyPoint.Key, kPointVal);
                    }
                }

                pattern.Candles.AddRange(modelWave.Candles);
                modelCurrent = modelWave;
                isUp = !isUp;
            }
            
            pattern.Level = (byte) (pattern.PatternKeyPoints.Count > 0
                ? pattern.PatternKeyPoints.Max(
                    a => a.Value.Max(b => b.Notation.Level)) + 1
                : 0);
            
            NotationItem[] notation = 
                NotationHelper.GetNotation(pattern.Model, pattern.Level);

            for (int i = 0; i < values.Length; i++)
            {
                int barIndex = bars4Gen.Take(i + 1).Sum() - 1;
                DateTime dt = arg.DateStart.Add(tfInfo.TimeSpan * barIndex);
                
                PatternKeyPoint patternKeyPoint = new PatternKeyPoint(
                    barIndex, values[i], notation[i]);

                AddKeyPoint(dt, patternKeyPoint);
            }
        }

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

            int highLimit = 200;

            if (is4ThShallow == is2NdShallow) highLimit = 150;
            if (is4ThShallow) highLimit = 250;

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
                    startItem.O = RandomWithinRange(startItem.L, startItem.H);
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
                    startItem.O = RandomWithinRange(startItem.L, startItem.H);
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
            }
            else
            {
                endItem.L = args.EndValue;
            }

            endItem.C = RandomWithinRange(endItem.L, endItem.H);
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
            {
                if (double.IsNaN(max))
                    min = valuesMap.Where(a => a.Key > 0).Min(a => a.Value);
                else
                    min = RandomWithinRange(
                        max * MAIN_ALLOWANCE_MAX_RATIO,
                        max * MAIN_ALLOWANCE_MAX_RATIO_INVERT);
            }

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

        private double RandomBigRatio()
        {
            return RandomWithinRange(0.7, 1.4);
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
        
        /// <summary>
        /// Splits the bars for the N-wave pattern.
        /// </summary>
        /// <param name="barsCount">The bars count.</param>
        /// <param name="parts">N count</param>
        /// <returns>Split values</returns>
        private int[] SplitByN(int barsCount, int parts)
        {
            double Fraction() => RandomRatio() / parts;
            double[] dbl = new double[parts];

            for (int i = 0; i < parts; i++)
            {
                dbl[i] = Fraction();
            }

            int[] bars4Gen = PatternGenKit.SplitNumber(
                barsCount, dbl);

            return bars4Gen;
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
