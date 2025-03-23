using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    public record ImpulseResult(
        double OverlapseMaxDepth,
        int CandlesCount,
        double Size,
        double RatioZigzag)
    {
        public override string ToString()
        {
            //return
            //    $"h{HeterogeneityDegree.ToPercent()}-{HeterogeneityMax.ToPercent()};o{OverlapseDegree.ToPercent()}-{OverlapseMaxDepth.ToPercent()};c{CandlesCount};s{Size:F4};rz{RatioZigzag.ToPercent()}";
            return
                $"o{OverlapseMaxDepth.ToPercent()}%;c{CandlesCount};s{Size.ToPercent():F2}%;rz{RatioZigzag}%";
        }
    }

}
