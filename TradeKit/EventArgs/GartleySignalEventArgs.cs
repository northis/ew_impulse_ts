using System;
using TradeKit.Core;

namespace TradeKit.EventArgs
{
    public class GartleySignalEventArgs : SignalEventArgs
    {
        public GartleySignalEventArgs(
            LevelItem level, 
            GartleyItem gartleyItem,
            DateTime startViewBarIndex)
            :base(level, gartleyItem.TakeProfit1, gartleyItem.StopLoss, startViewBarIndex)
        {
            GartleyItem = gartleyItem;
        }

        /// <summary>
        /// Gets the Gartley pattern points.
        /// </summary>
        public GartleyItem GartleyItem { get; }
    }
}
