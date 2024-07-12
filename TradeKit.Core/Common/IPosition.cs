namespace TradeKit.Core.Common
{
    public interface IPosition
    {
        int Id { get; }

        ISymbol Symbol { get; }

        double VolumeInUnits { get; }
    }
}
