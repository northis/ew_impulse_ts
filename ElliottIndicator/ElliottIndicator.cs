using cAlgo.API;
using TradeKit.CTrader.Indicators;

namespace ElliottIndicator
{
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class ElliottIndicator : IterativeImpulseZigzagIndicator
    {
    }
}
