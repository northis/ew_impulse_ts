using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Rate
{
    public abstract class RateSignalerBaseAlgoRobot : BaseAlgoRobot<RateSetupFinder, SignalEventArgs>
    {
        private readonly RateParams m_RateParams;
        private const string BOT_NAME = "RateSignalerRobot";

        protected RateSignalerBaseAlgoRobot(ITradeManager tradeManager,
            IStorageManager storageManager, RobotParams robotParams, RateParams rateParams, bool isBackTesting,
            string symbolName, string timeFrameName) : base(tradeManager, storageManager, robotParams, isBackTesting,
            symbolName, timeFrameName)
        {
            m_RateParams = rateParams;
        }

        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        /// <param name="signal">The <see cref="SignalEventArgs" /> instance containing the event data.</param>
        /// <returns>
        /// <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(
            RateSetupFinder setupFinder, SignalEventArgs signal)
        {
            bool res = setupFinder.LastEntry.BarIndex == signal.Level.BarIndex;
            return res;
        }

        /// <summary>
        /// Gets the volume.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="slPoints">The sl points.</param>
        protected override double GetVolume(ISymbol symbol, double slPoints)
        {
            return m_RateParams.TradeVolume == 0 ? base.GetVolume(symbol, slPoints) : m_RateParams.TradeVolume;
        }
    }
}