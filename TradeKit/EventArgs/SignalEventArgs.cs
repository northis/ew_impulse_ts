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
        public SignalEventArgs(LevelItem level, LevelItem takeProfit, LevelItem stopLoss)
        {
            Level = level;
            TakeProfit = takeProfit;
            StopLoss = stopLoss;
        }

        /// <summary>
        /// Gets the entry level.
        /// </summary>
        public LevelItem Level { get; }

        /// <summary>
        /// Gets the take profit level.
        /// </summary>
        public LevelItem TakeProfit { get; }

        /// <summary>
        /// Gets the stop loss level.
        /// </summary>
        public LevelItem StopLoss { get; }
    }
}
