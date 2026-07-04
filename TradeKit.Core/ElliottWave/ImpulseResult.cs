using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    public record ImpulseResult(
        double OverlapseMaxDepth,
        int CandlesCount,
        double Size,
        double RatioZigzag,
        double HeterogeneityMax,
        double Area,
        double MaxDistance)
    {
        /// <summary>
        /// Gets or sets the Start candle we want to see this impulse.
        /// </summary>
        public Candle EdgeExtremum { get; set; }

        /// <summary>
        /// Gets or sets the correction-to-impulse bars ratio (correction bars / impulse candidate bars).
        /// Can be greater than 1 (i.e. more than 100%).
        /// </summary>
        public double CorrectionRatio { get; set; }

        public override string ToString()
        {
            return
                $"h{HeterogeneityMax.ToPercent()};o{OverlapseMaxDepth.ToPercent()};c{CandlesCount};s{Size:F4};rz{RatioZigzag.ToPercent()};a{Area.ToPercent()};d{MaxDistance.ToPercent()};cr{CorrectionRatio.ToPercent()}";
            //return
            //    $"h{HeterogeneityMax.ToPercent()}%;o{OverlapseMaxDepth.ToPercent()}";
        }
    }

}
