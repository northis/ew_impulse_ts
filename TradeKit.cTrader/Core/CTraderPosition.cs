using TradeKit.Core.Common;

namespace TradeKit.CTrader.Core
{
    internal class CTraderPosition : IPosition
    {
        public CTraderPosition(int id, ISymbol symbol, double volumeInUnits, PositionType type, string comment)
        {
            Id = id;
            Symbol = symbol;
            VolumeInUnits = volumeInUnits;
            Type = type;
            Comment = comment;
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
    }
}
