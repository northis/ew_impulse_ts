namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Basic impulse (smooth one) params
    /// </summary>
    public record ImpulseParams(
        int StartPeriod,
        int EndPeriod,
        double SmoothDegree,
        int BarsCount)
    {
    }
}
