namespace TradeKit.Core.Common
{
    /// <summary>
    /// Helper for trade-related logic (view-only, for indicators).
    /// </summary>
    public interface ITradeViewManager
    {   
        /// <summary>
        /// Gets the timeframe instance by its name.
        /// </summary>
        /// <param name="timeFrameName">Name of the TF.</param>
        ITimeFrame GetTimeFrame(string timeFrameName);

        /// <summary>
        /// Gets the symbol instance by its name.
        /// </summary>
        /// <param name="symbolName">Name of the symbol.</param>
        ISymbol GetSymbol(string symbolName);

        /// <summary>
        /// Gets the symbol names available on the current trade platform.
        /// </summary>
        HashSet<string> GetSymbolNamesAvailable();

        /// <summary>
        /// Gets the spread of the given symbol (real-time).
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        double GetSpread(ISymbol symbol);

        /// <summary>
        /// Gets the ask price of the given symbol (real-time).
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        double GetAsk(ISymbol symbol);

        /// <summary>
        /// Gets the bid price of the given symbol (real-time).
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        double GetBid(ISymbol symbol);

        /// <summary>
        /// Gets the trading hours for the symbol.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        ITradingHours[] GetTradingHours(ISymbol symbol);

        /// <summary>
        /// Converts the volume from points to units.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="volumeInPoints">The volume in points.</param>
        double NormalizeVolumeInUnits(ISymbol symbol, double volumeInPoints);
    }
}
