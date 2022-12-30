using System;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Impulse
{
    public class ImpulseSignalerBaseRobot : BaseRobot<ImpulseSetupFinder, ImpulseSignalEventArgs>
    {
        private const string BOT_NAME = "ImpulseSignalerRobot";

        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        /// <summary>
        /// Creates the setup finder.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override ImpulseSetupFinder CreateSetupFinder(Bars bars, Symbol symbolEntity)
        {
            var barsProvider = new CTraderBarsProvider(bars, symbolEntity);
            var sf = new ImpulseSetupFinder(barsProvider, symbolEntity);
            return sf;
        }

        /// <summary>
        /// Determines whether <see cref="signal"/> and <see cref="setupFinder"/> can contain an overnight signal.
        /// </summary>
        /// <param name="signal">The signal.</param>
        /// <param name="setupFinder">The setup finder.</param>
        protected override bool IsOvernightTrade(
            ImpulseSignalEventArgs signal, ImpulseSetupFinder setupFinder)
        {
            IBarsProvider bp = setupFinder.BarsProvider; 
            DateTime setupStart = signal.StopLoss.OpenTime;
            DateTime setupEnd = signal.Level.OpenTime + TimeFrameHelper.TimeFrames[bp.TimeFrame].TimeSpan;
            Logger.Write(
                $"A risky signal, the setup contains a trade session change: {bp.Symbol}, {setupFinder.TimeFrame}, {setupStart:s}-{setupEnd:s}");

            return HasTradeBreakInside(setupStart, setupEnd, setupFinder.Symbol);
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
            ImpulseSetupFinder finder, ImpulseSignalEventArgs signal)
        {
            if (Math.Abs(finder.SetupStartPrice - signal.StopLoss.Value) < double.Epsilon &&
                Math.Abs(finder.SetupEndPrice - signal.TakeProfit.Value) < double.Epsilon)
            {
                return true;
            }

            return false;
        }
    }
}