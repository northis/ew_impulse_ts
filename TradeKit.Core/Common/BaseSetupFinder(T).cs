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
        }

        /// <summary>
        /// Occurs when a new setup is found.
        /// </summary>
        public event EventHandler<T> OnEnter;

        /// <summary>
        /// Occurs when a break even should be set for the setup.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnBreakEven;

        /// <summary>
        /// Occurs on stop loss.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnStopLoss;

        /// <summary>
        /// Occurs when on take profit.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnTakeProfit;

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
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        protected abstract void CheckSetup(int index);
        
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
        /// Raises the <see cref="E:OnBreakEven" /> event.
        /// </summary>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnBreakEvenInvoke(LevelEventArgs e)
        {
            OnBreakEven?.Invoke(this, e);
        }
    }
}
