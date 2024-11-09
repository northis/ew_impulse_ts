using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Common
{
    public class SymbolBase : ISymbol
    {
        public SymbolBase(
            string name, string description, long id, int digits, double pipSize, double pipValue, double lotSize)
        {
            Description = description;
            Name = name;
            Id = id;
            Digits = digits;
            PipSize = pipSize;
            PipValue = pipValue;
            LotSize = lotSize;
        }

        /// <summary>
        /// Gets the current symbol description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the current symbol name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the current symbol ID.
        /// </summary>
        public long Id { get; }

        /// <summary>
        /// Gets the number of digits for the symbol.
        /// </summary>
        public int Digits { get; }

        /// <summary>
        /// Gets the pip size for current symbol.
        /// </summary>
        public double PipSize { get; }

        /// <summary>
        /// Gets the size of 1 lot in units of the base currency.
        /// </summary>
        public double LotSize { get; }

        /// <summary>
        /// Gets the monetary value of one pip.
        /// </summary>
        public double PipValue { get; }
    }
}
