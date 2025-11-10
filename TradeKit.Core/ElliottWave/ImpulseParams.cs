namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Basic impulse (smooth one) params
    /// </summary>
    public record ImpulseParams(
        int Period,
        double EnterRatio,
        double TakeRatio,
        double BreakEvenRatio,
        double MaxZigzagPercent,
        double MaxOverlapseLengthPercent,
        double HeterogeneityMax,
        double MinSizePercent,
        double AreaPercent,
        int BarsCount) : EWParams(Period, MinSizePercent, BarsCount)
    {
    }
}
