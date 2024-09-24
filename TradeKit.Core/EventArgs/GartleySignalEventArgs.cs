using TradeKit.Core.Common;
using TradeKit.Core.Gartley;
using TradeKit.Core.PriceAction;

namespace TradeKit.Core.EventArgs
{
    public class GartleySignalEventArgs : SignalEventArgs
    {
        public GartleySignalEventArgs(
            BarPoint level,
            GartleyItem gartleyItem,
            DateTime startViewBarIndex,
            BarPoint divergenceStart = null,
            double? breakevenRatio = null,
            List<CandlesResult> candlePatterns=null)
            : base(level,
                level.WithPrice(gartleyItem.TakeProfit1), 
                level.WithPrice(gartleyItem.StopLoss), startViewBarIndex, breakevenRatio)
        {
            CandlePatterns = candlePatterns;
            GartleyItem = gartleyItem;
            DivergenceStart = divergenceStart;
        }

        /// <summary>
        /// Gets the candle patterns (Price Action) for filter or null.
        /// </summary>
        public List<CandlesResult> CandlePatterns { get; }

        /// <summary>
        /// Gets the Gartley pattern points.
        /// </summary>
        public GartleyItem GartleyItem { get; }

        /// <summary>
        /// Divergence start point
        /// </summary>
        public BarPoint DivergenceStart { get; }
    }
}
