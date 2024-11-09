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
        public SymbolTickEventArgs(double bid, double ask, ISymbol symbol)
        {
            Bid = bid;
            Ask = ask;
            Symbol = symbol;
        }

        /// <summary>Gets the bid price.</summary>
        public double Bid { get; }

        /// <summary>Gets the ask price.</summary>
        public double Ask { get; }

        /// <summary>Gets the symbol.</summary>
        public ISymbol Symbol { get; }
    }
}
