namespace TradeKit.Core.Common
{
    public interface ISymbol
    {
        string Description { get; }
        string Name { get; }
        long Id { get; }
    }
}
