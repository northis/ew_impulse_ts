namespace TradeKit.Core.Common
{
    public interface IPosition
    {
        int Id { get; }

        ISymbol Symbol { get; }

        double VolumeInUnits { get; }

        PositionType Type { get; }

        string Comment { get; }
    }
}
