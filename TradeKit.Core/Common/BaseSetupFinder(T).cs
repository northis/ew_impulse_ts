using System.Reflection;
using Plotly.NET;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Class contains the logic of trade setups searching.
    /// </summary>
    public abstract class BaseSetupFinder<T> where T : SignalEventArgs
    {
        private DateTime m_LastBarOpenDateTime;
        private bool m_IsInitialized;

        /// <summary>
        /// Gets the last open time of the bar.
        /// </summary>
        protected DateTime LastBarOpenDateTime => m_LastBarOpenDateTime;

        /// <summary>
        /// An initialization flag.
        /// </summary>
        protected bool IsInitialized => m_IsInitialized;

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

        static BaseSetupFinder()
        {
            Logger.Write($"Trade Kit version {Assembly.GetExecutingAssembly().GetName().Version}");
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
        }

        /// <summary>
        /// Marks the setup finder as initialized, indicating that the necessary initialization steps have been completed.
        /// </summary>
        public void MarkAsInitialized()
        {
            m_IsInitialized = true;
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
        /// Checks whether the data for a specified open DT candle bar contains a trade setup.
        /// </summary>
        /// <param name="openDateTime">The open DateTime of the candle bar to check for a trade setup.</param>
        protected abstract void CheckSetup(DateTime openDateTime);

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
        /// Checks the bar for specific conditions based on its opening date and time.
        /// </summary>
        /// <param name="openDateTime">The open datetime of the bar to be checked.</param>
        public virtual void CheckBar(DateTime openDateTime)
        {
            if (LastBarOpenDateTime > openDateTime) return;
            m_LastBarOpenDateTime = openDateTime;
            CheckSetup(openDateTime);
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
