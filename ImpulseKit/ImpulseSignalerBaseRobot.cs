using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Config;

namespace TradeKit
{
    public class ImpulseSignalerBaseRobot : BaseRobot<SetupFinder>
    {
        private const string BOT_NAME = "ImpulseSignalerRobot";

        public override string GetBotName()
        {
            return BOT_NAME;
        }

        /// <summary>
        /// Creates the setup finder.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="state">The state.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override SetupFinder CreateSetupFinder(
            Bars bars, SymbolState state, Symbol symbolEntity)
        {
            var barsProvider = new CTraderBarsProvider(bars);
            var sf = new SetupFinder(barsProvider, state, symbolEntity);
            return sf;
        }
    }
}