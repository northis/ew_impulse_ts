using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    public record ImpulseResult(
        SortedDictionary<double, int> Profile,
        double OverlapseDegree,
        double OverlapseMaxDepth,
        double OverlapseMaxDistance,
        double HeterogeneityDegree,
        double HeterogeneityMax,
        int CandlesCount,
        double Size,
        double SingleCandleDegree)
    {
        public override string ToString()
        {
            return
                $"h{HeterogeneityDegree.ToPercent()}-{HeterogeneityMax.ToPercent()};o{OverlapseDegree.ToPercent()}-{OverlapseMaxDepth.ToPercent()};c{CandlesCount};s{Size:F4};sc{SingleCandleDegree.ToPercent()}";
        }
    }

}
