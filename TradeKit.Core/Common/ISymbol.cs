namespace TradeKit.Core.Common
{
    public interface ISymbol
    {
        /// <summary>
        /// Gets the current symbol description.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Gets the current symbol name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the current symbol ID.
        /// </summary>
        long Id { get; }

        /// <summary>
        /// Gets the number of digits for the symbol.
        /// </summary>
        int Digits { get; }

        /// <summary>
        /// Gets the pip size for current symbol.
        /// </summary>
        double PipSize { get; }

        /// <summary>
        /// Gets the size of 1 lot in units of the base currency.
        /// </summary>
        double LotSize { get; }

        /// <summary>
        /// Gets the monetary value of one pip.
        /// </summary>
        double PipValue { get; }
    }
}
