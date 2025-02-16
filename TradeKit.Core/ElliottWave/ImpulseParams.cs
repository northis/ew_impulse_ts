namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Basic impulse (smooth one) params
    /// </summary>
    public record ImpulseParams(
        int Period,
        double ChannelRatio,
        double EnterRatio,
        double TakeRatio,
        double BreakEvenRatio,
        double HeterogeneityDegreePercent,
        double HeterogeneityMax,
        double MinSizePercent,
        double MaxOverlapsePercent,
        double MaxOverlapseLengthPercent,
        int BarsCount)
    {
    }
}
