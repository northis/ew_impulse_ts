using System;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Config;
using TradeKit.EventArgs;

namespace TradeKit
{
    public class ImpulseSignalerBaseRobot : BaseRobot<ImpulseSetupFinder>
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
        protected override ImpulseSetupFinder CreateSetupFinder(
            Bars bars, SymbolState state, Symbol symbolEntity)
        {
            var barsProvider = new CTraderBarsProvider(bars);
            var sf = new ImpulseSetupFinder(barsProvider, state, symbolEntity);
            return sf;
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="finder"></param>
        /// <param name="signal">The <see cref="SignalEventArgs" /> instance containing the event data.</param>
        /// <returns>
        ///   <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(
            ImpulseSetupFinder finder, SignalEventArgs signal)
        {
            if (Math.Abs(finder.SetupStartPrice - signal.StopLoss.Price) < double.Epsilon &&
                Math.Abs(finder.SetupEndPrice - signal.TakeProfit.Price) < double.Epsilon)
            {
                return true;
            }

            return false;
        }
    }
}