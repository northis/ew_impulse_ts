using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Indicators;

namespace TradeKit.Core
{
    /// <summary>
    ///  Class contains indicators & providers for the trend based on the "Super trend" indicator.
    /// </summary>
    public class SuperTrendItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuperTrendItem"/> class.
        /// </summary>
        /// <param name="indicators">The indicators.</param>
        /// <param name="bollingerBandsIndicator">The bollinger bands indicators.</param>
        private SuperTrendItem(
            SuperTrendIndicator[] indicators, BollingerBandsIndicator bollingerBandsIndicator)
        {
            Indicators = indicators;
            BollingerBands = bollingerBandsIndicator;
        }
        
        /// <summary>
        /// The "Super trend" indicator
        /// </summary>
        internal SuperTrendIndicator[] Indicators { get; }

        /// <summary>
        /// The "Bollinger Bands" indicator
        /// </summary>
        internal BollingerBandsIndicator BollingerBands { get; }

        private static BollingerBandsIndicator GetBollingerBandsIndicator(Algo algo, Bars bars)
        {
            BollingerBandsIndicator ind = algo.Indicators.GetIndicator<BollingerBandsIndicator>(
                bars,
                Helper.BOLLINGER_PERIODS,
                Helper.BOLLINGER_STANDARD_DEVIATIONS);

            return ind;
        }

        private static SuperTrendIndicator GetTrendIndicator(Algo algo, Bars bars)
        {
            SuperTrendIndicator ind = algo.Indicators.GetIndicator<SuperTrendIndicator>(bars,
                Helper.SUPERTREND_PERIOD,
                Helper.SUPERTREND_MULTIPLIER);

            return ind;
        }

        /// <summary>
        /// Creates the <see cref="SuperTrendItem"/> instance.
        /// </summary>
        /// <param name="mainTimeFrame">The main time frame.</param>
        /// <param name="algo">The algo instance (from an indicator or a cBot).</param>
        /// <param name="symbolName">The symbol name</param>
        public static SuperTrendItem Create(TimeFrame mainTimeFrame, Algo algo, string symbolName)
        {
            TimeFrameInfo[] tfInfos = {
                TimeFrameHelper.GetPreviousTimeFrameInfo(mainTimeFrame),
                TimeFrameHelper.GetTimeFrameInfo(mainTimeFrame),
                //TimeFrameHelper.GetNextTimeFrameInfo(mainTimeFrame)
            };

            var indicators = new SuperTrendIndicator[tfInfos.Length];
            for (int i = 0; i < tfInfos.Length; i++)
            {
                Bars bars = algo.MarketData.GetBars(tfInfos[i].TimeFrame, symbolName);
                indicators[i] = GetTrendIndicator(algo, bars);
            }

            Bars barsMain = algo.MarketData.GetBars(mainTimeFrame, symbolName);
            BollingerBandsIndicator bollingerBands = GetBollingerBandsIndicator(algo, barsMain);
            return new SuperTrendItem(indicators, bollingerBands);
        }
    }
}
