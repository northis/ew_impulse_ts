using TradeKit.Core.Common;
using TradeKit.Core.Harmonic;
using TradeKit.Core.PriceAction;

namespace TradeKit.Core.EventArgs
{
    /// <summary>
    /// Arguments of a harmonic XABCD trade setup.
    /// </summary>
    /// <seealso cref="SignalEventArgs" />
    public class HarmonicSignalEventArgs : SignalEventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HarmonicSignalEventArgs"/> class.
        /// </summary>
        /// <param name="level">The entry level - the close of the bar the point D was confirmed on.</param>
        /// <param name="takeProfit">The working take profit price.</param>
        /// <param name="stopLoss">The stop loss price.</param>
        /// <param name="harmonicItem">The completed harmonic pattern.</param>
        /// <param name="riskReward">The risk/reward ratio of the setup.</param>
        /// <param name="startViewBarTime">The bar time the chart should be analyzed from.</param>
        /// <param name="breakevenRatio">A value between 0 (entry) and 1 (take profit) or <c>null</c>.</param>
        /// <param name="divergenceStart">The divergence start point, when the divergence filter is used.</param>
        /// <param name="candlePatterns">The price action candle patterns, when the filter is used.</param>
        public HarmonicSignalEventArgs(
            BarPoint level,
            double takeProfit,
            double stopLoss,
            HarmonicItem harmonicItem,
            double riskReward,
            DateTime startViewBarTime,
            double? breakevenRatio = null,
            BarPoint divergenceStart = null,
            List<CandlesResult> candlePatterns = null)
            : base(level,
                level.WithPrice(takeProfit),
                level.WithPrice(stopLoss),
                false,
                startViewBarTime,
                breakevenRatio,
                harmonicItem.PatternType.ToString())
        {
            HarmonicItem = harmonicItem;
            RiskReward = riskReward;
            DivergenceStart = divergenceStart;
            CandlePatterns = candlePatterns;
        }

        /// <summary>
        /// Gets the completed harmonic pattern the setup is based on.
        /// </summary>
        public HarmonicItem HarmonicItem { get; }

        /// <summary>
        /// Gets the risk/reward ratio the setup was accepted with.
        /// </summary>
        public double RiskReward { get; }

        /// <summary>
        /// Gets the divergence start point or <c>null</c>.
        /// </summary>
        public BarPoint DivergenceStart { get; }

        /// <summary>
        /// Gets the price action candle patterns or <c>null</c>.
        /// </summary>
        public List<CandlesResult> CandlePatterns { get; }
    }
}
