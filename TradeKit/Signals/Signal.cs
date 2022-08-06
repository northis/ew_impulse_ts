using System;

namespace TradeKit.Signals
{
    /// <summary>
    /// Trade signal entity
    /// </summary>
    public class Signal
    {
        /// <summary>
        /// Gets or sets the name of the symbol.
        /// </summary>
        public string SymbolName { get; set; }

        /// <summary>
        /// Gets or sets the date time of the signal.
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// Gets or sets the entry price.
        /// </summary>
        public double? Price { get; set; }

        /// <summary>
        /// Gets or sets the take profits array.
        /// </summary>
        public double[] TakeProfits { get; set; }

        /// <summary>
        /// Gets or sets the stop loss.
        /// </summary>
        public double StopLoss { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this signal is BUY (long).
        /// </summary>
        /// <value>
        ///   <c>true</c> if this signal is BUY (long); otherwise, <c>false</c>.
        /// </value>
        public bool IsLong { get; set; }
    }
}
