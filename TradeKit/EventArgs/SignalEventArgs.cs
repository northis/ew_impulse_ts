using System;
using TradeKit.Core;

namespace TradeKit.EventArgs
{
    /// <summary>
    /// Arguments for signal events.
    /// </summary>
    /// <seealso cref="System.EventArgs" />
    public class SignalEventArgs : System.EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SignalEventArgs"/> class.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="takeProfit">The take profit level.</param>
        /// <param name="stopLoss">The stop loss.</param>
        /// <param name="startViewBarTime">The bar time we should visually analyze the chart from. Optional</param>
        public SignalEventArgs(
            BarPoint level,
            BarPoint takeProfit,
            BarPoint stopLoss, 
            DateTime startViewBarTime = default)
        {
            Level = level;
            TakeProfit = takeProfit;
            StopLoss = stopLoss;
            StartViewBarTime = startViewBarTime;
        }

        /// <summary>
        /// Gets the entry level.
        /// </summary>
        public BarPoint Level { get; }

        /// <summary>
        /// Gets the take profit level.
        /// </summary>
        public BarPoint TakeProfit { get; }

        /// <summary>
        /// Gets the stop loss level.
        /// </summary>
        public BarPoint StopLoss { get; }

        /// <summary>
        /// Gets the start bar time we should visually analyze the chart from.
        /// </summary>
        public DateTime StartViewBarTime { get; }
    }
}
