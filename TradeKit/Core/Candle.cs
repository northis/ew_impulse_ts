namespace TradeKit.Core
{
    /// <summary>
    /// OHLC Candle
    /// </summary>
    /// <seealso cref="System.IEquatable&lt;TradeKit.Core.Candle&gt;" />
    public record Candle(double O, double H, double L, double C);
}
