using TradeKit.Core.Common;

namespace TradeKit.Core.EventArgs
{
    public class SymbolTickEventArgs: System.EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolTickEventArgs"/> class.
        /// </summary>
        /// <param name="bid">The bid.</param>
        /// <param name="ask">The ask.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="time">The server time of the tick. Defaults to <see cref="DateTime.UtcNow"/>.</param>
        public SymbolTickEventArgs(double bid, double ask, ISymbol symbol, DateTime time = default)
        {
            Bid = bid;
            Ask = ask;
            Symbol = symbol;
            Time = time == default ? DateTime.UtcNow : time;
        }

        /// <summary>Gets the bid price.</summary>
        public double Bid { get; }

        /// <summary>Gets the ask price.</summary>
        public double Ask { get; }

        /// <summary>Gets the symbol.</summary>
        public ISymbol Symbol { get; }

        /// <summary>Gets the server UTC time of this tick.</summary>
        public DateTime Time { get; }
    }
}
