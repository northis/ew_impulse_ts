namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Basic EW model params
    /// </summary>
    public record EWParams(
        int Period,
        double MinSizePercent,
        int BarsCount)
    {
    }
}
