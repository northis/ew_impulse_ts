using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Helper for trade-related logic.
    /// </summary>
    public interface ITradeManager
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
        /// Gets the current positions.
        /// </summary>
        IPosition[] GetPositions();

        /// <summary>
        /// Occurs when a position is being closed.
        /// </summary>
        event EventHandler<ClosedPositionEventArgs> PositionClosed;

        /// <summary>
        /// Opens the trade order.
        /// </summary>
        /// <param name="isLong">if set to <c>true</c> [is long].</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="volume">The volume.</param>
        /// <param name="botName">Name of the bot.</param>
        /// <param name="stopInPips">The stop in pips.</param>
        /// <param name="takeInPips">The take in pips.</param>
        /// <param name="positionId">The position identifier.</param>
        /// <returns>Result of the operation</returns>
        OrderResult OpenOrder(
            bool isLong,
            ISymbol symbol,
            double volume,
            string botName,
            double stopInPips,
            double takeInPips,
            string positionId);

        /// <summary>
        /// Gets the symbol names available on the current trade platform.
        /// </summary>
        HashSet<string> GetSymbolNamesAvailable();

        /// <summary>
        /// Sets the stop loss price.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="price">The price.</param>
        void SetStopLossPrice(IPosition position, double? price);

        /// <summary>
        /// Sets the take profit price.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="price">The price.</param>
        void SetTakeProfitPrice(IPosition position, double? price);

        /// <summary>
        /// Closes the specified position.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>Result of the operation</returns>
        OrderResult Close(IPosition position);

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
        /// Gets the account balance.
        /// </summary>
        double GetAccountBalance();
    }
}
