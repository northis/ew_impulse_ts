using cAlgo.API.Internals;
using TradeKit.Core.Common;

namespace TradeKit.Core
{
    internal class CTraderSymbol : SymbolBase
    {
        public Symbol CSymbol { get; }

        public CTraderSymbol(Symbol symbol) : base(symbol.Name, symbol.Description, symbol.Id)
        {
            CSymbol = symbol;
        }
    }
}
