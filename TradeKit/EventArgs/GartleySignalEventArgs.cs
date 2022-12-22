using System;
using System.Collections.Generic;
using TradeKit.Core;
using TradeKit.Gartley;

namespace TradeKit.EventArgs
{
    public class GartleySignalEventArgs : SignalEventArgs
    {
        public GartleySignalEventArgs(
            LevelItem level,
            GartleyItem gartleyItem,
            DateTime startViewBarIndex,
            LevelItem divergenceStart = null)
            : base(level,
                level with {Price = gartleyItem.TakeProfit1},
                level with {Price = gartleyItem.StopLoss}, startViewBarIndex)
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
        public LevelItem DivergenceStart { get; }
    }
}
