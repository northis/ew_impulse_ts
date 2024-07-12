namespace TradeKit.Core.Common
{
    /// <summary>
    /// The reason for closing the position.
    /// </summary>
    public enum PositionClosedState
    {
        /// <summary>Positions was closed by the trader.</summary>
        CLOSED,
        /// <summary>Position was closed by the Stop Loss.</summary>
        STOP_LOSS,
        /// <summary>Position was closed by the Take Profit.</summary>
        TAKE_PROFIT,
        /// <summary>Position was closed because the Stop Out level reached. </summary>
        STOP_OUT,
    }
}
