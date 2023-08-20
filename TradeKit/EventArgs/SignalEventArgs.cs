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
        private readonly double? m_BreakevenRatio;
        /// <summary>
        /// Initializes a new instance of the <see cref="SignalEventArgs"/> class.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="takeProfit">The take profit level.</param>
        /// <param name="stopLoss">The stop loss.</param>
        /// <param name="startViewBarTime">The bar time we should visually analyze the chart from. Optional</param>
        /// <param name="breakevenRatio">Set as value between 0 (entry) and 1 (TP) to define the breakeven level or leave it null f you don't want to use the breakeven.</param>
        /// <param name="comment">The optional comment text to show.</param>
        public SignalEventArgs(
            BarPoint level,
            BarPoint takeProfit,
            BarPoint stopLoss,
            DateTime startViewBarTime = default,
            double? breakevenRatio = null,
            string comment = null)
        {
            m_BreakevenRatio = breakevenRatio;
            Level = level;
            TakeProfit = takeProfit;
            StopLoss = stopLoss;
            StartViewBarTime = startViewBarTime;
            Comment = comment;
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
        /// Gets the optional comment text to show.
        /// </summary>
        public string Comment { get; }

        /// <summary>
        /// Gets or sets a value indicating whether a breakeven was set on this signal.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance has breakeven; otherwise, <c>false</c>.
        /// </value>
        public bool HasBreakeven { get; set; }

        /// <summary>
        /// Gets a value indicating whether this signal can use breakeven.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this signal can use breakeven; otherwise, <c>false</c>.
        /// </value>
        public bool CanUseBreakeven => m_BreakevenRatio.HasValue;

        private double? m_BreakEvenPrice;

        /// <summary>
        /// Gets the break even price.
        /// </summary>
        public double BreakEvenPrice
        {
            get
            {
                if (m_BreakevenRatio.HasValue && !m_BreakEvenPrice.HasValue)
                {
                    bool isBool = TakeProfit > StopLoss;
                    double tpLen = Math.Abs(Level.Value - TakeProfit.Value);
                    m_BreakEvenPrice = Level.Value + tpLen * m_BreakevenRatio.Value * (isBool ? 1 : -1);
                }

                return m_BreakEvenPrice ?? 0;
            }
        }
    }
}
