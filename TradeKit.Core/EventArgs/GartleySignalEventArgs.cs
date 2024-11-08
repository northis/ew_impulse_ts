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
            bool isLimit = false,
            BarPoint divergenceStart = null,
            double? breakevenRatio = null,
            List<CandlesResult> candlePatterns = null,
            double? tp = null,
            double? sl = null)
            : base(level,
                level.WithPrice(tp ?? gartleyItem.TakeProfit1),
                level.WithPrice(sl ?? gartleyItem.StopLoss), isLimit, startViewBarIndex, breakevenRatio,
                gartleyItem.PatternType.Format())
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
