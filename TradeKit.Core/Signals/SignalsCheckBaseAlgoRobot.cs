using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Signals
{
    /// <summary>
    /// Algo-robot for history trading the signals from the file
    /// </summary>
    public abstract class SignalsCheckBaseAlgoRobot : BaseAlgoRobot<ParseSetupFinder, SignalEventArgs>
    {
        private const string BOT_NAME = "SignalsCheckRobot";
        protected SignalsCheckBaseAlgoRobot(ITradeManager tradeManager, IStorageManager storageManager,
            RobotParams robotParams, bool isBackTesting, string symbolName, string timeFrameName) : base(tradeManager,
            storageManager, robotParams, isBackTesting, symbolName, timeFrameName)
        {
        }

        /// <inheritdoc/>
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
            ParseSetupFinder setupFinder, SignalEventArgs signal)
        {
            bool res = setupFinder.LastEntry.BarIndex == signal.Level.BarIndex;
            return res;
        }
    }
}
