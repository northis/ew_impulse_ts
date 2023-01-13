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
        /// Gets or sets the stop loss level.
        /// Set for breakeven
        /// </summary>
        public BarPoint StopLoss { get; set; }

        /// <summary>
        /// Gets the start bar time we should visually analyze the chart from.
        /// </summary>
        public DateTime StartViewBarTime { get; }

        /// <summary>
        /// Gets or sets a value indicating whether a breakeven was set on this signal.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance has breakeven; otherwise, <c>false</c>.
        /// </value>
        public bool HasBreakeven { get; set; }


        private double? m_BreakEvenPrice;
        private const double BREAKEVEN_RATIO = 0.9;

        /// <summary>
        /// Gets the break even price.
        /// </summary>
        public double BreakEvenPrice
        {
            get
            {
                if (!m_BreakEvenPrice.HasValue)
                {
                    bool isBool = TakeProfit > Level;
                    double tpLen = Math.Abs(Level.Value - TakeProfit.Value);
                    m_BreakEvenPrice = TakeProfit.Value + tpLen * BREAKEVEN_RATIO * (isBool ? -1 : 1);
                }

                return m_BreakEvenPrice.Value;
            }
        }
    }
}
