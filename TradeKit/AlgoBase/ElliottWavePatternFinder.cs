using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var tfInfo = TimeFrameHelper.GetPreviousTimeFrameInfo(mainTimeFrame);

            m_BarsProviderMinor = barProvidersFactory.GetBarsProvider(tfInfo.TimeFrame);
            m_BarsProviderMinorX2 = tfInfo.TimeFrame == tfInfo.PrevTimeFrame
                ? null
                : barProvidersFactory.GetBarsProvider(tfInfo.PrevTimeFrame);
            m_MainFrameInfo = TimeFrameHelper.TimeFrames[mainTimeFrame];
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
            result = new ElliottModelResult(ElliottModelType.IMPULSE,
                new[] { start, end },
                new ElliottModelResult[] { });

            DateTime startDate = start.OpenTime;
            DateTime endDate = end.OpenTime.Add(m_MainFrameInfo.TimeSpan);

            List<JsonCandleExport> candles = Helper.GetCandles(
                m_BarsProviderMinor, startDate, endDate);
            if (candles.Count < Helper.ML_MIN_BARS_COUNT &&
                m_BarsProviderMinorX2 != null)
            {
                candles = Helper.GetCandles(m_BarsProviderMinorX2, startDate, endDate);
            }

            Debugger.Launch();
            ModelOutput prediction =
                MachineLearning.Predict(candles, start.Value, end.Value, MLModels.impulse1m);

            if (prediction == null || prediction.MainModel != ElliottModelType.IMPULSE)
            {
                return false;
            }

            Dictionary<ElliottModelType, float> models = prediction.GetModelsDictionary();
            return result != null;
        }
    }
}
