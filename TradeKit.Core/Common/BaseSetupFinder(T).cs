using Plotly.NET;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Class contains the logic of trade setups searching.
    /// </summary>
    public abstract class BaseSetupFinder<T> where T : SignalEventArgs
    {
        private int m_LastBarIndex;

        /// <summary>
        /// Gets the last bar index.
        /// </summary>
        protected int LastBar => m_LastBarIndex;

        /// <summary>
        /// Gets the symbol.
        /// </summary>
        public ISymbol Symbol { get; }

        /// <summary>
        /// Gets the time frame.
        /// </summary>
        public ITimeFrame TimeFrame { get; }

        /// <summary>
        /// Gets the bars provider.
        /// </summary>
        public IBarsProvider BarsProvider { get; }

        /// <summary>
        /// Gets the identifier of this setup finder.
        /// </summary>
        public virtual string Id => GetId(Symbol.Name, TimeFrame.Name);

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
        /// Initializes a new instance of the <see cref="BaseSetupFinder{T}"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bar provider.</param>
        /// <param name="symbol">The symbol.</param>
        protected BaseSetupFinder(
            IBarsProvider mainBarsProvider,
            ISymbol symbol)
        {
            Symbol = symbol;
            BarsProvider = mainBarsProvider;
            TimeFrame = mainBarsProvider.TimeFrame;
            Logger.Write($"Trade Kit version {Helper.VERSION}");
        }

        /// <summary>
        /// Occurs when a new setup is found.
        /// </summary>
        public event EventHandler<T> OnEnter;

        /// <summary>
        /// Occurs when SL or TP is changed for an existing setup.
        /// </summary>
        public event EventHandler<T> OnEdit;

        /// <summary>
        /// Occurs when a breakeven should be set for the setup.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnBreakeven;

        /// <summary>
        /// Occurs on limit order is canceled.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnCanceled;

        /// <summary>
        /// Occurs on limit order is activated.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnActivated;

        /// <summary>
        /// Occurs on stop loss.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnStopLoss;

        /// <summary>
        /// Occurs when on take profit.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnTakeProfit;

        /// <summary>
        /// Occurs when on the position of the setup is closed manually.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnManualClose;

        /// <summary>
        /// Raises the <see cref="E:OnStopLoss" /> event.
        /// </summary>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnStopLossInvoke(LevelEventArgs e)
        {
            OnStopLoss?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:OnManualClose" /> event.
        /// </summary>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnManualCloseInvoke(LevelEventArgs e)
        {
            OnManualClose?.Invoke(this, e);
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
        /// Raises the <see cref="E:OnCanceled" /> event.
        /// </summary>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnCanceledInvoke(LevelEventArgs e)
        {
            OnCanceled?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:OnActivated" /> event.
        /// </summary>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnActivatedInvoke(LevelEventArgs e)
        {
            OnActivated?.Invoke(this, e);
        }

        /// <summary>
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        protected abstract void CheckSetup(int index);

        /// <summary>
        /// Checks the tick. Used for quick update the state of the setup finder. Optional.
        /// </summary>
        /// <param name="tick">The <see cref="SymbolTickEventArgs"/> instance containing the event data.</param>
        public virtual void CheckTick(SymbolTickEventArgs tick)
        {

        }

        /// <summary>
        /// Notifies the setup finder that the position was closed manually.
        /// </summary>
        /// <param name="signalEventArgs">The signal args of the original setup finder.</param>
        /// <param name="args">Closing arguments</param>
        public virtual void NotifyManualClose(T signalEventArgs, ClosedPositionEventArgs args)
        {

        }

        /// <summary>
        /// Checks the conditions of possible setup for a bar of <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index of bar to calculate.</param>
        public virtual void CheckBar(int index)
        {
            if (m_LastBarIndex != index)
            {
                m_LastBarIndex = index;
                CheckSetup(m_LastBarIndex);
            }
        }

        /// <summary>
        /// Raises the <see cref="E:OnEnter" /> event.
        /// </summary>
        /// <param name="e">The <see cref="T"/> instance containing the event data.</param>
        protected void OnEnterInvoke(T e)
        {
            OnEnter?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:OnEdit" /> event.
        /// </summary>
        /// <param name="e">The <see cref="T"/> instance containing the event data.</param>
        protected void OnEditInvoke(T e)
        {
            OnEdit?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:OnBreakEven" /> event.
        /// </summary>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnBreakEvenInvoke(LevelEventArgs e)
        {
            OnBreakeven?.Invoke(this, e);
        }
    }
}
