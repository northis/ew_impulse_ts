using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class SimpleDoubleFinder : SimpleBaseFinder<double>
    {
        public SimpleDoubleFinder(IBarsProvider barsProvider, bool useAutoCalculateEvent = false, int defaultCleanBarsCount = 500) : base(barsProvider, useAutoCalculateEvent, defaultCleanBarsCount)
        {
        }
    }
}
