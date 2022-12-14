using cAlgo.API.Internals;
using TradeKit.Core;

namespace TradeKit.Signals
{
    /// <summary>
    /// Do nothing setup finder
    /// </summary>
    public class NullParseSetupFinder : ParseSetupFinder
    {
        public NullParseSetupFinder(IBarsProvider mainBarsProvider, Symbol symbol) : base(mainBarsProvider, symbol, string.Empty,true,true)
        {
        }

        public override void CheckBar(int index)
        {
        }

        public override void CheckTick(double bid)
        {
        }
    }
}
