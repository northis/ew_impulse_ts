using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.Impulse;
using TradeKit.Json;
using TradeKit.ML;
using TradeKit.Resources;

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
        public ElliottWavePatternFinder(TimeFrame mainTimeFrame, BarProvidersFactory barProvidersFactory)
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
            List<BarPoint> waves = new List<BarPoint> {start, end};
            result = new ElliottModelResult(ElliottModelType.IMPULSE, waves, null, null);

            DateTime startDate = start.OpenTime;
            DateTime endDate = end.OpenTime.Add(m_MainFrameInfo.TimeSpan);
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK;
            byte[] modelBytes = MLModels.modelsEW_full;
            byte[] modelRegressionBytes = MLModels.modelsEW_full;//TODO
            List<JsonCandleExport> candles = Helper.GetCandles(m_BarsProviderMinor, startDate, endDate);
            if (candles.Count < Helper.ML_MIN_BARS_COUNT &&
                m_BarsProviderMinorX2 != null)
            {
                candles = Helper.GetCandles(m_BarsProviderMinorX2, startDate, endDate);
            }

            CombinedPrediction<JsonCandleExport> prediction =
                MachineLearning.Predict(candles, start.Value, end.Value, modelBytes, modelRegressionBytes, rank);
            
            result.Models = prediction.Classification.GetModelsMap();
            (ElliottModelType, float) model = result.Models[0];
            (ElliottModelType, float)[] topModels = result.Models.Take(2).ToArray();

            if (model.Item1 != ElliottModelType.IMPULSE ||
                topModels.Any(a => a.Item1 == ElliottModelType.DOUBLE_ZIGZAG))
            {
                return false;
            }

            BarPoint GetBarPoint((JsonCandleExport, double) item)
            {
                var bp = new BarPoint(item.Item2, item.Item1.OpenDate, m_BarsProviderMain);
                return bp;
            }

            waves.Insert(1, GetBarPoint(prediction.Wave1));
            waves.Insert(2, GetBarPoint(prediction.Wave2));

            if (prediction.Wave3 != null && prediction.Wave4 != null)
            {
                waves.Insert(3, GetBarPoint(prediction.Wave3.Value));
                waves.Insert(4, GetBarPoint(prediction.Wave4.Value));
            }

            result.Type = model.Item1;
            result.MaxScore = prediction.Classification.MaxValue;
            return true;
        }
    }
}
