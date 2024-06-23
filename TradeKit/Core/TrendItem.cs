using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Indicators;

namespace TradeKit.Core
{
    /// <summary>
    ///  Class contains indicators & providers for the trend based on the "Bill Williams' Alligator" indicator.
    /// </summary>
    public class TrendItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TrendItem"/> class.
        /// </summary>
        /// <param name="indicators">The trend indicators (minor, main and major TF).</param>
        /// <param name="bollingerBandsIndicator">The bollinger bands indicators.</param>
        /// <param name="mainTrendIndicator">The main TF trend indicator.</param>
        private TrendItem(
            ZoneAlligator[] indicators, 
            BollingerBandsIndicator bollingerBandsIndicator, 
            ZoneAlligator mainTrendIndicator)
        {
            Indicators = indicators;
            BollingerBands = bollingerBandsIndicator;
            MainTrendIndicator = mainTrendIndicator;
        }

        /// <summary>
        /// The "Bill Williams' Alligator" indicators (3 TFs max)
        /// </summary>
        internal ZoneAlligator[] Indicators { get; }

        /// <summary>
        /// The "Bill Williams' Alligator" indicator
        /// </summary>
        internal ZoneAlligator MainTrendIndicator { get; }

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

        private static ZoneAlligator GetTrendIndicator(Algo algo, Bars bars)
        {
            ZoneAlligator ind = algo.Indicators.GetIndicator<ZoneAlligator>(bars);
            return ind;
        }

        /// <summary>
        /// Creates the <see cref="TrendItem"/> instance.
        /// </summary>
        /// <param name="mainTimeFrame">The main time frame.</param>
        /// <param name="algo">The algo instance (from an indicator or a cBot).</param>
        /// <param name="symbolName">The symbol name</param>
        public static TrendItem Create(TimeFrame mainTimeFrame, Algo algo, string symbolName)
        {
            TimeFrameInfo[] tfInfos = {
                TimeFrameHelper.GetPreviousTimeFrameInfo(mainTimeFrame),
                TimeFrameHelper.GetTimeFrameInfo(mainTimeFrame),
                TimeFrameHelper.GetNextTimeFrameInfo(mainTimeFrame)
            };

            ZoneAlligator mainIndicator = null;
            var indicators = new ZoneAlligator[tfInfos.Length];
            for (int i = 0; i < tfInfos.Length; i++)
            {
                TimeFrame tf = tfInfos[i].TimeFrame;
                Bars bars = algo.MarketData.GetBars(tf, symbolName);
                indicators[i] = GetTrendIndicator(algo, bars);

                if (tf == mainTimeFrame)
                    mainIndicator = indicators[i];
            }

            Bars barsMain = algo.MarketData.GetBars(mainTimeFrame, symbolName);
            BollingerBandsIndicator bollingerBands = GetBollingerBandsIndicator(algo, barsMain);
            return new TrendItem(indicators, bollingerBands, mainIndicator);
        }
    }
}
