using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class SimpleFinder : BaseFinder<double>
    {
        public SimpleFinder(IBarsProvider barsProvider, bool useAutoCalculateEvent = false, int defaultCleanBarsCount = 500) : base(barsProvider, useAutoCalculateEvent, defaultCleanBarsCount)
        {
        }

        public override void OnCalculate(int index, DateTime openDateTime)
        {
        }

        public void SetResult(DateTime dt, double value)
        {
            SetResultValue(dt, value);
        }
    }
}
