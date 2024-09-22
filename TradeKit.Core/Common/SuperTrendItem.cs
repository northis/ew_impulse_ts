using System.Diagnostics;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.Common
{
    /// <summary>
    ///  Class contains indicators & providers for the trend based on the "Super trend" indicator.
    /// </summary>
    public class SuperTrendItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuperTrendItem"/> class.
        /// </summary>
        /// <param name="indicators">The trend indicators (minor, main and major TF).</param>
        /// <param name="bollingerBandsIndicator">The bollinger bands indicator.</param>
        /// <param name="mainTrendIndicator">The main TF trend indicator.</param>
        private SuperTrendItem(
            ZoneAlligatorFinder[] indicators, BollingerBandsFinder bollingerBandsIndicator, ZoneAlligatorFinder mainTrendIndicator)
        {
            Indicators = indicators;
            BollingerBands = bollingerBandsIndicator;
            MainTrendIndicator = mainTrendIndicator;
        }

        /// <summary>
        /// The "zone alligator" indicators (3 TFs max)
        /// </summary>
        internal ZoneAlligatorFinder[] Indicators { get; }

        /// <summary>
        /// The "zone alligator" indicator
        /// </summary>
        internal ZoneAlligatorFinder MainTrendIndicator { get; }

        /// <summary>
        /// The "Bollinger Bands" indicator
        /// </summary>
        internal BollingerBandsFinder BollingerBands { get; }

        private static BollingerBandsFinder GetBollingerBandsIndicator(IBarsProvider barsProvider)
        {
            BollingerBandsFinder ind = new BollingerBandsFinder(barsProvider);
            return ind;
        }

        private static ZoneAlligatorFinder GetTrendIndicator(IBarsProvider barsProvider)
        {
            var ind = new ZoneAlligatorFinder(barsProvider);
            return ind;
        }

        /// <summary>
        /// Creates the <see cref="SuperTrendItem"/> instance.
        /// </summary>
        /// <param name="mainTimeFrame">The main time frame.</param>
        /// <param name="barsProvider">The bar provider.</param>
        public static SuperTrendItem Create(
            ITimeFrame mainTimeFrame, 
            IBarsProvider barsProvider)
        {
            TimeFrameInfo[] tfInfos = {
                TimeFrameHelper.GetPreviousTimeFrameInfo(mainTimeFrame),
                TimeFrameHelper.GetTimeFrameInfo(mainTimeFrame),
                TimeFrameHelper.GetNextTimeFrameInfo(mainTimeFrame)
            };

            ZoneAlligatorFinder mainIndicator = null;
            var indicators = new ZoneAlligatorFinder[tfInfos.Length];
            for (int i = 0; i < tfInfos.Length; i++)
            {
                ITimeFrame tf = tfInfos[i].TimeFrame;
                indicators[i] = GetTrendIndicator(barsProvider);

                if (tf.Name == mainTimeFrame.Name)
                    mainIndicator = indicators[i];
            }

            BollingerBandsFinder bollingerBands = GetBollingerBandsIndicator(barsProvider);
            return new SuperTrendItem(indicators, bollingerBands, mainIndicator);
        }
    }
}
