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

        protected override RateSetupFinder CreateSetupFinder(
            Bars bars, SymbolState state, Symbol symbolEntity)
        {
            throw new System.NotImplementedException();
        }

        protected override bool HasSameSetupActive(
            RateSetupFinder setupFinder, SignalEventArgs signal)
        {
            throw new System.NotImplementedException();
        }
    }
}