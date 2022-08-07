using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Rate
{
    public class RateSignalerBaseRobot : BaseRobot<RateSetupFinder, SignalEventArgs>
    {
        private const string BOT_NAME = "RateSignalerRobot";

        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="state">The state.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override RateSetupFinder CreateSetupFinder(
            Bars bars, SymbolState state, Symbol symbolEntity)
        {
            var barsProvider = new CTraderBarsProvider(bars);
            var sf = new RateSetupFinder(barsProvider, state, symbolEntity);
            return sf;
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        /// <param name="signal">The <see cref="!:TK" /> instance containing the event data.</param>
        /// <returns>
        /// <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(
            RateSetupFinder setupFinder, SignalEventArgs signal)
        {
            bool res = setupFinder.LastEntry?.Index == signal.Level.Index;
            return res;
        }
    }
}