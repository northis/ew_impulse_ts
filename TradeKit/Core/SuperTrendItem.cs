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
        private SuperTrendItem(SuperTrendIndicator[] indicators)
        {
            Indicators = indicators;
        }
        
        /// <summary>
        /// The "Super trend" indicator (main)
        /// </summary>
        internal SuperTrendIndicator[] Indicators { get; }

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
                TimeFrameHelper.GetNextTimeFrameInfo(mainTimeFrame)
            };

            var indicators = new SuperTrendIndicator[tfInfos.Length];
            for (int i = 0; i < tfInfos.Length; i++)
            {
                Bars bars = algo.MarketData.GetBars(tfInfos[i].TimeFrame, symbolName);
                indicators[i] = GetTrendIndicator(algo, bars);
            }
            
            return new SuperTrendItem(indicators);
        }
    }
}
