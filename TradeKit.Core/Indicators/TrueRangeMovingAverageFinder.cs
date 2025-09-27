using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class TrueRangeMovingAverageFinder : SimpleMovingAverageFinder
    {
        public TrueRangeMovingAverageFinder(IBarsProvider barsProvider, int periods = 14, bool useAutoCalculateEvent = true) : base(barsProvider, periods, useAutoCalculateEvent)
        {
        }

        public override double GetPrice(int index)
        {
            double h = BarsProvider.GetHighPrice(index);
            double l = BarsProvider.GetLowPrice(index);
            double c = BarsProvider.GetClosePrice(checked(index - 1));
            return Math.Max(h, c) - Math.Min(l, c);
        }
    }
}
