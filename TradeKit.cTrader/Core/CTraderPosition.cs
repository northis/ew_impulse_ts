using System;
using TradeKit.Core.Common;

namespace TradeKit.CTrader.Core
{
    internal class CTraderPosition : IPosition
    {
        public CTraderPosition(int id, ISymbol symbol, double volumeInUnits, PositionType type, string label, string comment,
            DateTime enterDateTime, DateTime? closeDateTime, double? stopLoss, double? takeProfit, double swap, double quantity, double netProfit, double grossProfit, double entryPrice, double currentPrice, double charges = 0)
        {
            Id = id;
            Symbol = symbol;
            VolumeInUnits = volumeInUnits;
            Type = type;
            Comment = comment;
            EnterDateTime = enterDateTime;
            CloseDateTime = closeDateTime;
            Charges = charges;
            StopLoss = stopLoss;
            TakeProfit = takeProfit;
            Swap = swap;
            Quantity = quantity;
            NetProfit = netProfit;
            GrossProfit = grossProfit;
            EntryPrice = entryPrice;
            CurrentPrice = currentPrice;
            Label = label;
        }

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets the symbol.
        /// </summary>
        public ISymbol Symbol { get; }

        /// <summary>
        /// Gets the volume in units.
        /// </summary>
        public double VolumeInUnits { get; }

        /// <summary>
        /// Gets the type.
        /// </summary>
        public PositionType Type { get; }

        /// <summary>
        /// Gets the comment.
        /// </summary>
        public string Comment { get; }

        /// <summary>
        /// Gets the label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Gets the swap.
        /// </summary>
        public double Swap { get; }

        /// <summary>
        /// Gets the volume trade in lots.
        /// </summary>
        public double Quantity { get; }

        /// <summary>
        /// Gets the stop loss.
        /// </summary>
        public double? StopLoss { get; }

        /// <summary>
        /// Gets the take profit.
        /// </summary>
        public double? TakeProfit { get; }

        /// <summary>
        /// Gets the charges amount.
        /// </summary>
        public double Charges { get; }

        /// <summary>
        /// Gets the net profit.
        /// </summary>
        public double NetProfit { get; }

        /// <summary>
        /// Gets the gross profit.
        /// </summary>
        public double GrossProfit { get; }

        /// <summary>
        /// Gets the entry price.
        /// </summary>
        public double EntryPrice { get; }

        /// <summary>
        /// Gets the current price.
        /// </summary>
        public double CurrentPrice { get; }

        /// <summary>
        /// Gets the enter date time.
        /// </summary>
        public DateTime EnterDateTime { get; }

        /// <summary>
        /// Gets the close date time.
        /// </summary>
        public DateTime? CloseDateTime { get; }
    }
}
