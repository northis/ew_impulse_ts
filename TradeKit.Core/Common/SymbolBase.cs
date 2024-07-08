namespace TradeKit.Core.Common
{
    public class SymbolBase : ISymbol
    {
        public SymbolBase(string name, string description, long id)
        {
            Description = description;
            Name = name;
            Id = id;
        }

        public string Description { get; }
        public string Name { get; }
        public long Id { get; }
    }
}
