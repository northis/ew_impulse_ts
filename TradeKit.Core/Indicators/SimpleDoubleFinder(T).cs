using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class SimpleBaseFinder<T> : BaseFinder<T>
    {
        public SimpleBaseFinder(IBarsProvider barsProvider, bool useAutoCalculateEvent = false, int defaultCleanBarsCount = 500) : base(barsProvider, useAutoCalculateEvent, defaultCleanBarsCount)
        {
        }

        public override void OnCalculate(DateTime openDateTime)
        {
        }

        public void SetResult(DateTime dt, T value)
        {
            SetResultValue(dt, value);
        }
    }
}
