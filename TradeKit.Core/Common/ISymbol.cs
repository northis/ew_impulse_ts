namespace TradeKit.Core.Common
{
    public interface ISymbol
    {
        string Description { get; }
        string Name { get; }
        string Id { get; }
    }
}
