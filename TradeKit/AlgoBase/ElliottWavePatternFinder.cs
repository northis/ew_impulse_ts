using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.Impulse;
using TradeKit.Json;
using TradeKit.ML;

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

            m_TimeSpanMinPeriod = m_MainFrameInfo.TimeSpan.Multiply(MIN_PERIOD + 1);
        }

        private SortedDictionary<DateTime, int> GetExtremaScored(
            SortedDictionary<DateTime, double> values,
            DateTime startDate,
            DateTime endDate,
            Func<DateTime, double, bool> isStopPrice)
        {
            SortedDictionary<DateTime, int> scores = new SortedDictionary<DateTime, int>();
            foreach (DateTime dt in values.Keys)
            {
                if (dt <= startDate || dt >= endDate || double.IsNaN(values[dt]))
                    continue;

                double currentPrice = values[dt];
                int rewindScore = MIN_PERIOD;

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

                scores[dt] = Math.Min(fastForwardScore, rewindScore);
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

            return true;
        }

        private bool IsImpulseByPivots(
            BarPoint start, BarPoint end, out ElliottModelResult result)
        {
            List<BarPoint> waves = new List<BarPoint> { start, end };
            result = new ElliottModelResult(ElliottModelType.IMPULSE, waves, null, null);
            
            m_PivotPointsFinder.Reset();
            m_PivotPointsFinder.Calculate(
                start.OpenTime.Subtract(m_TimeSpanMinPeriod),
                end.OpenTime.Add(m_TimeSpanMinPeriod));

            SortedDictionary<DateTime, int> highs = GetExtremaScored(m_PivotPointsFinder.HighValues, start.OpenTime, end.OpenTime,
                (d,p)=> m_BarsProviderMain.GetHighPrice(d) >= p);
            SortedDictionary<DateTime, int> lows = GetExtremaScored(
                m_PivotPointsFinder.LowValues, start.OpenTime, end.OpenTime,
                (d, p) => m_BarsProviderMain.GetHighPrice(d) <= p);

            if (highs.Count == 0 || lows.Count == 0)
                return false;

            bool isUp = end > start;
            TimeSpan halfLength = (end.OpenTime - start.OpenTime) / 2.5;
            DateTime middleDate = start.OpenTime.Add(halfLength);

            //Let's find the end of the 3rd wave in the second part of the movement
            List<KeyValuePair<DateTime, int>> wave3And5 = (isUp ? highs : lows)
                .SkipWhile(a => a.Key < middleDate)
                .ToList();
            if (wave3And5.Count == 0)
                return false;

            KeyValuePair<DateTime, int> wave3EndScore = wave3And5.MaxBy(a => a.Value);
            double wave3EndValue = isUp
                ? m_PivotPointsFinder.HighValues[wave3EndScore.Key]
                : m_PivotPointsFinder.LowValues[wave3EndScore.Key];

            // If this is real 3rd wave ending
            // Wave B (X) of 4 (check)
            // Find the opposite extremum after this one
            List<KeyValuePair<DateTime, double>> wave4And5 = (isUp
                    ? m_PivotPointsFinder.LowValues
                    : m_PivotPointsFinder.HighValues)
                .SkipWhile(a => a.Key <= wave3EndScore.Key)
                .Where(a => isUp ? lows.ContainsKey(a.Key) : highs.ContainsKey(a.Key))
                .ToList();
            if (wave4And5.Count == 0)
                return false;

            // This is the end of wave 4 (zz or dzz or fl)
            KeyValuePair<DateTime, double> wave4End = isUp 
                ? wave4And5.MinBy(a => a.Value) 
                : wave4And5.MaxBy(a => a.Value);
            // Or wave A of a triangle (check)

            // Find the end of the wave 1 using found 3rd one
            List<KeyValuePair<DateTime, int>> wave0To3 = (isUp ? highs : lows)
                .TakeWhile(a => a.Key < wave3EndScore.Key)
                .ToList();
            if (wave0To3.Count == 0)
                return false;

            KeyValuePair<DateTime, int> wave1EndScore = wave0To3.MaxBy(a => a.Value);
            double wave1EndValue = isUp
                ? m_PivotPointsFinder.HighValues[wave1EndScore.Key]
                : m_PivotPointsFinder.LowValues[wave1EndScore.Key];

            // This is real 1st wave
            // Or wave B (X) of wave 2 (check)
            List<KeyValuePair<DateTime, double>> wave2And3 = (isUp
                    ? m_PivotPointsFinder.LowValues
                    : m_PivotPointsFinder.HighValues)
                .SkipWhile(a => a.Key <= wave1EndScore.Key)
                .TakeWhile(a => a.Key < wave3EndScore.Key)
                .Where(a => isUp ? lows.ContainsKey(a.Key) : highs.ContainsKey(a.Key))
                .ToList();

            if (wave2And3.Count == 0)
                return false;

            // This is the end of wave 2 (zz or dzz or fl)
            KeyValuePair<DateTime, double> wave2End = isUp
                ? wave2And3.MinBy(a => a.Value)
                : wave2And3.MaxBy(a => a.Value);
            
            waves.Insert(1, new BarPoint(wave1EndValue, wave1EndScore.Key, m_BarsProviderMain));
            waves.Insert(2, new BarPoint(wave2End.Value, wave2End.Key, m_BarsProviderMain));
            waves.Insert(3, new BarPoint(wave3EndValue, wave3EndScore.Key, m_BarsProviderMain));
            waves.Insert(4, new BarPoint(wave4End.Value, wave4End.Key, m_BarsProviderMain));

            bool isValid = ValidateImpulse(
                waves[0], waves[1], waves[2], waves[3], waves[4], waves[5]);

            return isValid;
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
            bool isImpulse = IsImpulseByPivots(start, end, out result);

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
