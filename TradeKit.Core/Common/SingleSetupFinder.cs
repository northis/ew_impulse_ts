using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Class contains the logic of trade setups searching.
    /// Single-setup version means it support only one setup in the same time.
    /// </summary>
    /// <typeparam name="T">Type of signal events arg</typeparam>
    /// <seealso cref="BaseSetupFinder{T}" />
    public abstract class SingleSetupFinder<T> : BaseSetupFinder<T> where T : SignalEventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SingleSetupFinder{T}"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        protected SingleSetupFinder(IBarsProvider mainBarsProvider, ISymbol symbol) 
            : base(mainBarsProvider, symbol)
        {
        }

        /// <summary>
        /// Gets or sets a value indicating whether this finder is in setup.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is in setup; otherwise, <c>false</c>.
        /// </value>
        public bool IsInSetup { get; set; }
    }
}
