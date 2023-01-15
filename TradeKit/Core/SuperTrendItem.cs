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
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="superTrend"> The "Super trend" indicator.</param>
        public SuperTrendItem(IBarsProvider barsProvider, SuperTrendIndicator superTrend)
        {
            BarsProvider = barsProvider;
            SuperTrend = superTrend;
        }

        /// <summary>
        /// The bars provider
        /// </summary>
        public IBarsProvider BarsProvider { get; }

        /// <summary>
        /// The "Super trend" indicator
        /// </summary>
        public SuperTrendIndicator SuperTrend { get; }
    }
}
