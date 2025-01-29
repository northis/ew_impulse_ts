namespace TradeKit.Core.ElliottWave
{
    public record ImpulseResult(
        SortedDictionary<double, int> Profile,
        double OverlapseDegree,
        double OverlapseMaxDepth,
        double OverlapseMaxDistance,
        double HeterogeneityDegree,
        int CandlesCount,
        double Size,
        double SingleCandleDegree);
}
