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
                $"{HeterogeneityDegree.ToPercent()}/{HeterogeneityMax.ToPercent()}/{OverlapseDegree.ToPercent()}/{OverlapseMaxDepth.ToPercent()}/{CandlesCount}";
        }
    }

}
