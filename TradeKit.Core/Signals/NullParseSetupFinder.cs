using TradeKit.Core.Common;

namespace TradeKit.Core.Signals
{
    /// <summary>
    /// Do nothing setup finder
    /// </summary>
    public class NullParseSetupFinder : ParseSetupFinder
    {
        public NullParseSetupFinder(IBarsProvider mainBarsProvider, ISymbol symbol, ITradeViewManager tvm) : base(
            mainBarsProvider, symbol, tvm, string.Empty)
        {
        }
    }
}
