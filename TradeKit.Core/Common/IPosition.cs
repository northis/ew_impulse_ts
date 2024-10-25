namespace TradeKit.Core.Common
{
    public interface IPosition
    {
        /// <summary>
        /// Gets the identifier.
        /// </summary>
        int Id { get; }

        /// <summary>
        /// Gets the symbol.
        /// </summary>
        ISymbol Symbol { get; }

        /// <summary>
        /// Gets the volume in units.
        /// </summary>
        double VolumeInUnits { get; }

        /// <summary>
        /// Gets the type.
        /// </summary>
        PositionType Type { get; }

        /// <summary>
        /// Gets the comment.
        /// </summary>
        string Comment { get; }

        /// <summary>
        /// Gets the swap.
        /// </summary>
        double Swap { get; }

        /// <summary>
        /// Gets the volume trade in lots.
        /// </summary>
        double Quantity { get; }

        /// <summary>
        /// Gets the stop loss.
        /// </summary>
        double? StopLoss { get; }

        /// <summary>
        /// Gets the take profit.
        /// </summary>
        double? TakeProfit { get; }

        /// <summary>
        /// Gets the charges amount.
        /// </summary>
        double Charges { get; }

        /// <summary>
        /// Gets the net profit.
        /// </summary>
        double NetProfit { get; }

        /// <summary>
        /// Gets the gross profit.
        /// </summary>
        double GrossProfit { get; }

        /// <summary>
        /// Gets the entry price.
        /// </summary>
        double EntryPrice { get; }

        /// <summary>
        /// Gets the current price.
        /// </summary>
        double CurrentPrice { get; }

        /// <summary>
        /// Gets the enter date time.
        /// </summary>
        DateTime EnterDateTime { get; }

        /// <summary>
        /// Gets the close date time.
        /// </summary>
        DateTime? CloseDateTime { get; }
    }
}
