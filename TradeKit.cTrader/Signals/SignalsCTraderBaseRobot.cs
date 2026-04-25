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
            return new SignalsParams(SignalHistoryFilePath, UseLimitOrders, BreakevenOnPipsRunning, TakeProfitIndex);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the signal history file path.
        /// </summary>
        [Parameter(nameof(SignalHistoryFilePath), DefaultValue = "")]
        public string SignalHistoryFilePath { get; set; }

        /// <summary>
        /// When true, a limit order is placed at the signal entry price when the current price
        /// is less favorable than the entry. When false, always enters at market.
        /// </summary>
        [Parameter("Use Limit Orders", DefaultValue = true)]
        public bool UseLimitOrders { get; set; }

        /// <summary>
        /// When true, a reply containing "+N pips running" is treated as a breakeven signal.
        /// </summary>
        [Parameter("Breakeven on +N pips message", DefaultValue = false)]
        public bool BreakevenOnPipsRunning { get; set; }

        /// <summary>
        /// 1-based index of the take-profit level to use (1 = closest to entry).
        /// If the index exceeds the number of TP levels in the signal, the farthest level is used.
        /// </summary>
        [Parameter("Take Profit Level Index", DefaultValue = 1, MinValue = 1)]
        public int TakeProfitIndex { get; set; }

        #endregion
    }
}
