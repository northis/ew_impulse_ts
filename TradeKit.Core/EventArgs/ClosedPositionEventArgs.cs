using TradeKit.Core.Common;

namespace TradeKit.Core.EventArgs
{
    public class ClosedPositionEventArgs : OpenedPositionEventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClosedPositionEventArgs"/> class.
        /// </summary>
        /// <param name="state">The reason the position was closed.</param>
        /// <param name="position">The closed position.</param>
        /// <param name="closePrice">
        /// The price the position was <b>really</b> closed at. It is not the stop level: a wide
        /// spread or a gap fills the stop with slippage, and the statistics count the result in
        /// risks, so −1R becomes −1.5R (DIAGONAL.md §6.7). <c>null</c> falls back to the last
        /// price the position reported.
        /// </param>
        public ClosedPositionEventArgs(
            PositionClosedState state, IPosition position, double? closePrice = null)
            : base(position)
        {
            State = state;
            ClosePrice = closePrice ?? position?.CurrentPrice ?? 0;
        }

        public PositionClosedState State { get; }

        /// <summary>
        /// Gets the actual closing price of the position; <c>0</c> when it is unknown.
        /// </summary>
        public double ClosePrice { get; }
    }
}
