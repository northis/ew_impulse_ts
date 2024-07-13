namespace TradeKit.Core.Common
{
    public class SymbolBase : ISymbol
    {
        public SymbolBase(
            string name, string description, long id, int digits, double pipSize, double pipValue)
        {
            Description = description;
            Name = name;
            Id = id;
            Digits = digits;
            PipSize = pipSize;
            PipValue = pipValue;
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

        public double LotSize { get; }

        /// <summary>
        /// Gets the monetary value of one pip.
        /// </summary>
        public double PipValue { get; }

        public double GetSpread()
        {
            throw new NotImplementedException();
        }

        public double GetAsk()
        {
            throw new NotImplementedException();
        }

        public double GetBid()
        {
            throw new NotImplementedException();
        }
    }
}
