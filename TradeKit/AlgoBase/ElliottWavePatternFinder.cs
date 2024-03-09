using System;
using System.Collections.Generic;
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
            List<BarPoint> waves = new List<BarPoint> { start, end };
            result = new ElliottModelResult(ElliottModelType.IMPULSE, waves, null, null);

            DateTime startDate = start.OpenTime;
            DateTime endDate = end.OpenTime.Add(m_MainFrameInfo.TimeSpan);
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK;
            
            List<JsonCandleExport> candles = Helper.GetCandles(m_BarsProviderMinor, startDate, endDate);
            if (candles.Count < Helper.ML_MIN_BARS_COUNT &&
                m_BarsProviderMinorX2 != null)
            {
                candles = Helper.GetCandles(m_BarsProviderMinorX2, startDate, endDate);
            }

            var predictions =
                MachineLearning.Predict(candles, start.Value, end.Value, rank);

            List<float> scores = new List<float>();

            string modelType = string.Empty;
            foreach (string predictionKey in predictions.Keys)
            {
                ClassPrediction prediction = predictions[predictionKey];
                var modelMain = (ElliottModelType) prediction.PredictedIsFit;
                //(ElliottModelType, float) model = result.Models[0];
                //(ElliottModelType, float)[] topModels = result.Models.Take(2).ToArray();

                if (modelMain != ElliottModelType.IMPULSE &&
                    modelMain != ElliottModelType.SIMPLE_IMPULSE)
                {
                    continue;
                }

                result.Models = prediction.GetModelsMap();

                result.Type = modelMain;
                scores.Add(result.Models
                    .First(a => a.Item1 == modelMain).Item2);
                modelType += $" {(int)modelMain}";
            }
            
            result.ModelType = modelType;
            result.MaxScore = scores.Count > 0 ? scores.Average() : 0;
            return result.MaxScore > 40;
        }
    }
}
