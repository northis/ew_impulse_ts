using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Helper for trade-related logic.
    /// </summary>
    public interface ITradeManager : ITradeViewManager
    {   
        /// <summary>
        /// Gets the current positions.
        /// </summary>
        IPosition[] GetPositions();

        /// <summary>
        /// Gets the closed position.
        /// </summary>
        /// <param name="positionId">The position identifier.</param>
        /// <param name="tp">The tp.</param>
        /// <param name="sl">The sl.</param>
        IPosition GetClosedPosition(string positionId, double? tp, double? sl);

        /// <summary>
        /// Occurs when a position is being closed.
        /// </summary>
        event EventHandler<ClosedPositionEventArgs> PositionClosed;

        /// <summary>
        /// Opens the trade order.
        /// </summary>
        /// <param name="isLong">if set to <c>true</c> when it is buy trade; false for sell.</param>
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
        /// Sets the stop loss price.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="price">The price.</param>
        void SetStopLossPrice(IPosition position, double? price);

        /// <summary>
        /// Sets the stop loss to the entry price.
        /// </summary>
        /// <param name="position">The position.</param>
        public void SetBreakeven(IPosition position);

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
        /// Gets the account balance.
        /// </summary>
        double GetAccountBalance();
    }
}
