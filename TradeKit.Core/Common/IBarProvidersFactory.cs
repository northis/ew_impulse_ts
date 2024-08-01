namespace TradeKit.Core.Common
{
    /// <summary>
    /// Factory to get providers for symbol
    /// </summary>
    public interface IBarProvidersFactory
    {
        /// <summary>
        /// Gets the symbol.
        /// </summary>
        public ISymbol Symbol { get; }

        /// <summary>
        /// Gets the bars provider.
        /// </summary>
        /// <param name="timeFrame">The time frame.</param>
        public IBarsProvider GetBarsProvider(ITimeFrame timeFrame);
    }
}
