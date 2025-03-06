using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Signals;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Signals
{
    public abstract class SignalsCTraderBaseRobot<T> : CTraderBaseRobot<T, ParseSetupFinder, SignalEventArgs>
        where T : BaseAlgoRobot<ParseSetupFinder, SignalEventArgs>
    {
        /// <summary>
        /// Joins the signals parsing strategy-specific parameters into one record.
        /// </summary>
        protected SignalsParams GetSignalsParams()
        {
            return new SignalsParams(SignalHistoryFilePath);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the signal history file path.
        /// </summary>
        [Parameter(nameof(SignalHistoryFilePath), DefaultValue = "")]
        public string SignalHistoryFilePath { get; set; }
        #endregion
    }
}
