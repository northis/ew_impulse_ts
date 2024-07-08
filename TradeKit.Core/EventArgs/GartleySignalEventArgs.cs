using TradeKit.Core.Common;
using TradeKit.Core.Gartley;

namespace TradeKit.Core.EventArgs
{
    public class GartleySignalEventArgs : SignalEventArgs
    {
        public GartleySignalEventArgs(
            BarPoint level,
            GartleyItem gartleyItem,
            DateTime startViewBarIndex,
            BarPoint divergenceStart = null,
            double? breakevenRatio = null)
            : base(level,
                level.WithPrice(gartleyItem.TakeProfit1), 
                level.WithPrice(gartleyItem.StopLoss), startViewBarIndex, breakevenRatio)
        {
            GartleyItem = gartleyItem;
            DivergenceStart = divergenceStart;
        }

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
