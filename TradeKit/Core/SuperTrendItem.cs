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
        /// <param name="barsProviderMain">The bars provider (main).</param>
        /// <param name="superTrendMain"> The "Super trend" indicator (main).</param>
        /// <param name="superTrendMajor"> The "Super trend" indicator (major, optional).</param>
        /// <param name="barsProviderMajor">The bars provider (major, optional).</param>
        private SuperTrendItem(IBarsProvider barsProviderMain, SuperTrendIndicator superTrendMain, SuperTrendIndicator superTrendMajor = null, IBarsProvider barsProviderMajor = null)
        {
            BarsProviderMain = barsProviderMain;
            BarsProviderMajor = barsProviderMajor;
            SuperTrendMain = superTrendMain;
            SuperTrendMajor = superTrendMajor;
        }

        /// <summary>
        /// The bars provider (main)
        /// </summary>
        public IBarsProvider BarsProviderMain { get; }

        /// <summary>
        /// The bars provider (major)
        /// </summary>
        public IBarsProvider BarsProviderMajor { get; }

        /// <summary>
        /// The "Super trend" indicator (major)
        /// </summary>
        public SuperTrendIndicator SuperTrendMajor { get; }
        
        /// <summary>
        /// The "Super trend" indicator (main)
        /// </summary>
        public SuperTrendIndicator SuperTrendMain { get; }
        
        /// <summary>
        /// Creates the <see cref="SuperTrendItem"/> instance.
        /// </summary>
        /// <param name="mainTimeFrame">The main time frame.</param>
        /// <param name="algo">The algo instance (from an indicator or a cBot).</param>
        /// <param name="majorRatio">The major ratio (Big TF/Main TF).</param>
        /// <param name="barsProvider">The main bars provider.</param>
        public static SuperTrendItem Create(
            TimeFrame mainTimeFrame,
            Algo algo,
            double majorRatio,
            IBarsProvider barsProvider)
        {
            SuperTrendIndicator stMajor = null;
            IBarsProvider barsProviderMajor = null;
            if (majorRatio > 1)
            {
                TimeFrameInfo majorTf = TimeFrameHelper.GetNextTimeFrame(mainTimeFrame, majorRatio);
                Bars majorBars = algo.MarketData.GetBars(majorTf.TimeFrame);
                barsProviderMajor = new CTraderBarsProvider(majorBars, algo.Symbol);
                stMajor = algo.Indicators.GetIndicator<SuperTrendIndicator>(majorBars,
                    Helper.SUPERTREND_PERIOD,
                    Helper.SUPERTREND_MULTIPLIER);
            }

            SuperTrendIndicator stMain = algo.Indicators.GetIndicator<SuperTrendIndicator>(algo.Bars,
                Helper.SUPERTREND_PERIOD,
                Helper.SUPERTREND_MULTIPLIER);

            return new SuperTrendItem(barsProvider, stMain, stMajor, barsProviderMajor);
        }
    }
}
