using System;
using cAlgo.API.Internals;
using TradeKit.Config;
using TradeKit.EventArgs;

namespace TradeKit
{
    /// <summary>
    /// Class contains the logic of trade setups searching.
    /// </summary>
    public abstract class BaseSetupFinder
    {
        protected readonly Symbol Symbol;

        /// <summary>
        /// Gets the state.
        /// </summary>
        public SymbolState State { get; }

        /// <summary>
        /// Gets the bars provider.
        /// </summary>
        public IBarsProvider BarsProvider { get; }
        
        /// <summary>
        /// Gets the identifier of this setup finder.
        /// </summary>
        public string Id => GetId(State.Symbol, State.TimeFrame);

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        /// <param name="symbolName">Name of the symbol.</param>
        /// <param name="timeFrame">The time frame.</param>
        public static string GetId(string symbolName, string timeFrame)
        {
            return symbolName + timeFrame;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="state">The state.</param>
        /// <param name="symbol">The symbol.</param>
        protected BaseSetupFinder(
            IBarsProvider mainBarsProvider,
            SymbolState state,
            Symbol symbol)
        {
            Symbol = symbol;
            BarsProvider = mainBarsProvider;
            State = state;
        }

        /// <summary>
        /// Occurs on stop loss.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnStopLoss;

        /// <summary>
        /// Occurs when on take profit.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnTakeProfit;

        /// <summary>
        /// Occurs when a new setup is found.
        /// </summary>
        public event EventHandler<SignalEventArgs> OnEnter;

        /// <summary>
        /// Checks the conditions of possible setup for a bar of <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index of bar to calculate.</param>
        public abstract void CheckBar(int index);

        /// <summary>
        /// Checks the tick.
        /// </summary>
        /// <param name="bid">The price (bid).</param>
        public abstract void CheckTick(double bid);

        /// <summary>
        /// Raises the <see cref="E:OnStopLoss" /> event.
        /// </summary>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnStopLossInvoke(LevelEventArgs e)
        {
            OnStopLoss?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:OnTakeProfit" /> event.
        /// </summary>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnTakeProfitInvoke(LevelEventArgs e)
        {
            OnTakeProfit?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:OnEnter" /> event.
        /// </summary>
        /// <param name="e">The <see cref="SignalEventArgs"/> instance containing the event data.</param>
        protected void OnEnterInvoke(SignalEventArgs e)
        {
            OnEnter?.Invoke(this, e);
        }
    }
}
