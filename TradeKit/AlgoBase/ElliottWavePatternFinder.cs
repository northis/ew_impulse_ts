using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.Impulse;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Contains pattern-finding logic for the Elliott Waves structures.
    /// </summary>
    public class ElliottWavePatternFinder
    {
        private readonly IBarsProvider m_BarsProviderMain;
        private readonly IBarsProvider m_BarsProviderMinor;
        private readonly IBarsProvider m_BarsProviderMinorX2;
        private readonly TimeFrameInfo m_MainFrameInfo;
        private readonly TimeSpan m_TimeSpanMinPeriod;
        private readonly PivotPointsFinder m_PivotPointsFinder;

        private const int MIN_PERIOD = 1;
        private const int SMOOTH_MIN_PERIOD = 2;

        private HashSet<ElliottModelType> m_Wave4Patterns = new List<ElliottModelType>
        {
            ElliottModelType.ZIGZAG,
            ElliottModelType.DOUBLE_ZIGZAG,
            ElliottModelType.TRIANGLE_RUNNING,
            ElliottModelType.TRIANGLE_CONTRACTING,
            ElliottModelType.FLAT_EXTENDED,
            ElliottModelType.FLAT_RUNNING,
        }.ToHashSet();

        private HashSet<ElliottModelType> m_Wave2Patterns = new List<ElliottModelType>
        {
            ElliottModelType.ZIGZAG,
            ElliottModelType.DOUBLE_ZIGZAG,
            ElliottModelType.FLAT_EXTENDED,
            ElliottModelType.FLAT_RUNNING,
        }.ToHashSet();

        private record CheckParams(
            bool IsUp,
            SortedDictionary<DateTime, (int, double)> Highs,
            SortedDictionary<DateTime, (int, double)> Lows,
            List<ImpulseElliottModelResult> WavesResults);

        /// <summary>
        /// Initializes a new instance of the <see cref="ElliottWavePatternFinder"/> class.
        /// </summary>
        /// <param name="mainTimeFrame">The main TF</param>
        /// <param name="barProvidersFactory">The bar provider factory (to get moe info).</param>
        public ElliottWavePatternFinder(
            TimeFrame mainTimeFrame, 
            BarProvidersFactory barProvidersFactory)
        {
            m_MainFrameInfo = TimeFrameHelper.TimeFrames[mainTimeFrame];
            m_BarsProviderMain = barProvidersFactory.GetBarsProvider(mainTimeFrame);
            var tfInfo = TimeFrameHelper.GetPreviousTimeFrameInfo(mainTimeFrame);

            m_BarsProviderMinor = barProvidersFactory.GetBarsProvider(tfInfo.TimeFrame);
            m_BarsProviderMinorX2 = tfInfo.TimeFrame == tfInfo.PrevTimeFrame
                ? null
                : barProvidersFactory.GetBarsProvider(tfInfo.PrevTimeFrame);

            m_PivotPointsFinder = new PivotPointsFinder(
                MIN_PERIOD, m_BarsProviderMain);//min val

            m_TimeSpanMinPeriod = m_MainFrameInfo.TimeSpan;
        }

        private SortedDictionary<DateTime, (int, double)> GetExtremaScored(
            SortedDictionary<DateTime, double> values,
            DateTime startDate,
            DateTime endDate,
            Func<DateTime, double, bool> isStopPrice)
        {
            var scores = new SortedDictionary<DateTime, (int, double)>();
            foreach (DateTime dt in values.Keys)
            {
                if (dt <= startDate || dt >= endDate || double.IsNaN(values[dt]))
                    continue;

                double currentPrice = values[dt];
                int rewindScore = 0;

                //rewind
                DateTime currentDateTime = dt.Subtract(m_TimeSpanMinPeriod);
                for (;;)
                {
                    if (currentDateTime < startDate ||
                        isStopPrice(currentDateTime, currentPrice))
                    {
                        break;
                    }

                    rewindScore++;
                    currentDateTime = currentDateTime.Subtract(m_MainFrameInfo.TimeSpan);
                }

                int fastForwardScore = 0;

                //fast-forward
                currentDateTime = dt.Add(m_TimeSpanMinPeriod);
                for (;;)
                {
                    if (currentDateTime > endDate ||
                        isStopPrice(currentDateTime, currentPrice) ||
                        fastForwardScore > rewindScore)
                    {
                        break;
                    }

                    fastForwardScore++;
                    currentDateTime = currentDateTime.Add(m_MainFrameInfo.TimeSpan);
                }

                int res = Math.Min(fastForwardScore, rewindScore);
                if (res > 0)
                    scores[dt] = (res, currentPrice);
            }

            return scores;
        }

        private bool ValidateImpulse(
            BarPoint wave0, 
            BarPoint wave1, 
            BarPoint wave2, 
            BarPoint wave3, 
            BarPoint wave4, 
            BarPoint wave5)
        {
            bool isUp = wave5 > wave0;
            int k = isUp ? 1 : -1;
            double wave3Len = k * (wave3 - wave2);
            if (wave3Len <= 0)
                return false;
            double wave1Len = k * (wave1 - wave0);
            if (wave1Len <= 0)
                return false;
            double wave5Len = k * (wave5 - wave4);
            if (wave5Len <= 0)
                return false;

            if(wave3Len<= wave1Len && wave3Len<= wave5Len)
                return false;

            if (isUp && wave4 <= wave1 ||
                !isUp && wave4 >= wave1)
                return false;

            TimeSpan wave2Len = wave2.OpenTime - wave1.OpenTime;
            TimeSpan wave4Len = wave4.OpenTime - wave3.OpenTime;

            if(wave2Len == TimeSpan.Zero)
                return false;

            double ratio = wave4Len / wave2Len;
            if (ratio is > 3.5 or < 0.75)
                return false;

            return true;
        }

        private List<BarPoint> SelectPeaks(
            List<KeyValuePair<DateTime, (int, double)>> dateScorePairs, 
            bool isUp)
        {
            List<KeyValuePair<DateTime, (int, double)>> sorted = dateScorePairs
                .OrderByDescending(a => a.Value.Item1)
                .ToList();

            var result = new List<BarPoint>();
            if (sorted.Count == 0)
                return result;

            KeyValuePair<DateTime, (int, double)> first = sorted.First();
            if (sorted.Count == 1)
            {
                result.Add(new BarPoint(
                    first.Value.Item2, first.Key, m_BarsProviderMain));
                return result;
            }

            double topThreshold = first.Value.Item1 * 0.85; // To separate big peaks and the rest ones.
            IOrderedEnumerable<KeyValuePair<DateTime, (int, double)>> tops = sorted
                .Where(a => a.Value.Item1 >= topThreshold)
                .OrderByDescending(a => a.Value);

            result = (isUp
                ? tops.ThenByDescending(a => a.Value.Item2)
                : tops.ThenBy(a => a.Value.Item2))
                .Take(2)
                .Select(a => new BarPoint(a.Value.Item2, a.Key, m_BarsProviderMain))
                .ToList();
            return result;
        }

        private bool IsImpulseByPivots(
            BarPoint start, BarPoint end, out ImpulseElliottModelResult result)
        {
            var waveResults = new List<ImpulseElliottModelResult>();
            result = new ImpulseElliottModelResult {Wave0 = start, Wave5 = end};

            m_PivotPointsFinder.Reset();
            m_PivotPointsFinder.Calculate(
                start.OpenTime.Subtract(m_TimeSpanMinPeriod),
                end.OpenTime.Add(m_TimeSpanMinPeriod));

            SortedDictionary<DateTime, (int, double)> highs = GetExtremaScored(m_PivotPointsFinder.HighValues, start.OpenTime,
                end.OpenTime,
                (d, p) => m_BarsProviderMain.GetHighPrice(d) >= p);
            SortedDictionary<DateTime, (int, double)> lows = GetExtremaScored(
                m_PivotPointsFinder.LowValues, start.OpenTime, end.OpenTime,
                (d, p) => m_BarsProviderMain.GetLowPrice(d) <= p);
            
            if (highs.Count == 0 && lows.Count == 0)
                return true;//really smooth movement

            bool isUp = end > start;

            TimeSpan halfLength = (end.OpenTime - start.OpenTime) / 2.5;
            DateTime middleDate = start.OpenTime.Add(halfLength);

            //Let's find the end of the 3rd wave in the second part of the movement
            List<KeyValuePair<DateTime, (int, double)>> wave3And5 = (isUp ? highs : lows)
                .SkipWhile(a => a.Key < middleDate)
                .ToList();

            List<BarPoint> peaks3To4To5 = SelectPeaks(wave3And5, isUp);
            if (peaks3To4To5.Count == 0)
                return false;

            var checkParams = new CheckParams(isUp, highs, lows, waveResults);
            if (peaks3To4To5.Count > 1)
            {
                CheckWith3RdWave(checkParams,
                    result with { Wave3 = peaks3To4To5[0] }, peaks3To4To5[1]);
                CheckWith3RdWave(checkParams,
                    result with { Wave3 = peaks3To4To5[1] }, peaks3To4To5[0]);
            }

            CheckWith3RdWave(checkParams, result with { Wave3 = peaks3To4To5[0] });
            return checkParams.WavesResults.Any();
        }

        private void CheckWith1stWave(
            CheckParams checkParams, ImpulseElliottModelResult result, BarPoint bOf2 = null)
        {
            if (result.Wave1 == null)
                return;
            
            List<KeyValuePair<DateTime, (int, double)>> wave2And3 = (checkParams.IsUp
                    ? checkParams.Lows
                    : checkParams.Highs)
                .SkipWhile(a => a.Key <= (bOf2?.OpenTime ?? result.Wave1.OpenTime))
                .TakeWhile(a => a.Key < result.Wave3.OpenTime &&
                                checkParams.IsUp
                    ? a.Value.Item2 < result.Wave1.Value
                    : a.Value.Item2 > result.Wave1.Value)
                .ToList();

            if (wave2And3.Count == 0)
                return;

            // This is the end of wave 2 (zz or dzz or fl)
            KeyValuePair<DateTime, (int, double)> wave2End = checkParams.IsUp
                ? wave2And3.MinBy(a => a.Value.Item2)
                : wave2And3.MaxBy(a => a.Value.Item2);

            result.Wave2 = new BarPoint(wave2End.Value.Item2, wave2End.Key, m_BarsProviderMain);
            if (bOf2 == null)
            {
                result.ExtremaWave2 = new List<BarPoint> { result.Wave1, result.Wave2 };
                result.Wave2Type = ElliottModelType.ZIGZAG;
                // TODO detect dzz?
            }
            else
            {
                if (result.Wave1.OpenTime >= bOf2.OpenTime)
                    return;// foolproof

                if (checkParams.IsUp && bOf2 < result.Wave1 ||
                    !checkParams.IsUp && bOf2 > result.Wave1 ||
                    result.Wave1.OpenTime >= bOf2.OpenTime)
                    return;// exclude a regular flat

                BarPoint waveA = m_BarsProviderMain.GetExtremumBetween(result.Wave1.OpenTime, result.Wave2.OpenTime, !checkParams.IsUp);
                if (waveA == null)
                    return;

                result.ExtremaWave2 = new List<BarPoint> {result.Wave1, waveA, bOf2, result.Wave2};

                if (checkParams.IsUp && waveA < result.Wave2 ||
                    !checkParams.IsUp && waveA > result.Wave2)
                    result.Wave2Type = ElliottModelType.FLAT_RUNNING;
                else
                    result.Wave2Type = ElliottModelType.FLAT_EXTENDED;
            }
            
            bool isValid = ValidateImpulse(
                result.Wave0, result.Wave1,
                result.Wave2, result.Wave3,
                result.Wave4, result.Wave5);

            if (!isValid)
                return;

            checkParams.WavesResults.Add(result);
        }

        private void CheckWith1st2ndWave(
            CheckParams checkParams, ImpulseElliottModelResult result)
        {
            List<KeyValuePair<DateTime, (int, double)>> wave1And3 = (
                    checkParams.IsUp ? checkParams.Highs : checkParams.Lows)
                .TakeWhile(a => a.Key < result.Wave3.OpenTime)
                .ToList();

            List<BarPoint> peaks1To3 = SelectPeaks(wave1And3, checkParams.IsUp);
            if (peaks1To3.Count == 0)
                return;

            if (peaks1To3.Count > 1)
            {
                CheckWith1stWave(checkParams, result with { Wave1 = peaks1To3[0] }, peaks1To3[1]);
                CheckWith1stWave(checkParams, result with { Wave1 = peaks1To3[1] }, peaks1To3[0]);
            }

            CheckWith1stWave(checkParams, result with { Wave1 = peaks1To3[0] });
        }

        private void CheckWith3RdWave(
            CheckParams checkParams, ImpulseElliottModelResult result, BarPoint bOf4 = null)
        {
            bool useRunning4 = bOf4 != null;

            if (useRunning4)
            {
                if (bOf4.OpenTime <= result.Wave3.OpenTime)
                    return;

                if (checkParams.IsUp && bOf4 < result.Wave3 ||//not-running peak?
                    !checkParams.IsUp && bOf4 > result.Wave3)
                    return;

                BarPoint waveAof4 =
                    m_BarsProviderMain.GetExtremumBetween(
                        result.Wave3.OpenTime, bOf4.OpenTime, !checkParams.IsUp);
                if (waveAof4 == null)
                    return;
                
                BarPoint waveCof4 =
                    m_BarsProviderMain.GetExtremumBetween(
                        bOf4.OpenTime, result.Wave5.OpenTime, !checkParams.IsUp);

                if (waveCof4 == null)
                    return;

                // Now we got a flat or a triangle, lets check it out
                List<KeyValuePair<DateTime, (int, double)>> wave4DeAnd5 = (checkParams.IsUp ? checkParams.Lows : checkParams.Highs)
                    .SkipWhile(a => a.Key <= waveCof4.OpenTime)
                    .TakeWhile(a => checkParams.IsUp
                        ? a.Value.Item2 > waveCof4 && a.Value.Item2 < bOf4.Value
                        : a.Value.Item2 < waveCof4 && a.Value.Item2 > bOf4.Value)
                    .ToList();

                if (wave4DeAnd5.Any())
                {
                    result.Wave4Type = ElliottModelType.TRIANGLE_CONTRACTING;
                    //result.ExtremaWave4 = new List<BarPoint>{ result.Wave3 , waveAof4, bOf4, waveCof4; }
                }
                else
                {
                    result.Wave4Type = ElliottModelType.FLAT_EXTENDED;
                }

                CheckWith1st2ndWave(checkParams,
                    result with
                    {
                        Wave4 = waveCof4,
                        ExtremaWave4 = new List<BarPoint>
                        {
                            result.Wave3, waveAof4, bOf4, waveCof4
                        }
                    });
            }
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
            bool isImpulse = IsImpulseByPivots(start, end,
                out ImpulseElliottModelResult resultInner);//replace

            //List<BarPoint> waves = new List<BarPoint> { start, end };
            //result = new ElliottModelResult(ElliottModelType.IMPULSE, waves, null, null);

            //DateTime startDate = start.OpenTime;
            //DateTime endDate = end.OpenTime.Add(m_MainFrameInfo.TimeSpan);
            //ushort rank = Helper.ML_IMPULSE_VECTOR_RANK;

            //List<JsonCandleExport> candles = Helper.GetCandles(m_BarsProviderMinor, startDate, endDate);
            //if (candles.Count < Helper.ML_MIN_BARS_COUNT &&
            //    m_BarsProviderMinorX2 != null)
            //{
            //    candles = Helper.GetCandles(m_BarsProviderMinorX2, startDate, endDate);
            //}

            //var predictions =
            //    MachineLearning.Predict(candles, start.Value, end.Value, rank);

            //foreach (string predictionKey in predictions.Keys)
            //{
            //    ClassPrediction prediction = predictions[predictionKey];
            //    var modelMain = (ElliottModelType) prediction.PredictedIsFit;
            //    //(ElliottModelType, float) model = result.Models[0];
            //    //(ElliottModelType, float)[] topModels = result.Models.Take(2).ToArray();

            //    if (modelMain != ElliottModelType.IMPULSE &&
            //        modelMain != ElliottModelType.SIMPLE_IMPULSE)
            //    {
            //        continue;
            //    }

            //    result.Models = prediction.GetModelsMap();
            //    result.Type = modelMain;
            //    result.ModelType = ((int)modelMain).ToString();
            //    result.MaxScore = result.Models.First(a => a.Item1 == modelMain).Item2;
            //    return true;
            //}

            return isImpulse;
        }
    }
}
