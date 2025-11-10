using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    public record ImpulseResult(
        double OverlapseMaxDepth,
        int CandlesCount,
        double Size,
        double RatioZigzag,
        double HeterogeneityMax,
        double Area)
    {
        /// <summary>
        /// Gets or sets the Start candle we want to see this impulse.
        /// </summary>
        public Candle EdgeExtremum { get; set; }
        public override string ToString()
        {
            return
                $"h{HeterogeneityMax.ToPercent()};o{OverlapseMaxDepth.ToPercent()};c{CandlesCount};s{Size:F4};rz{RatioZigzag.ToPercent()};a{Area.ToPercent()}";
            //return
            //    $"h{HeterogeneityMax.ToPercent()}%;o{OverlapseMaxDepth.ToPercent()}";
        }
    }

}
