using cAlgo.API;
using TradeKit.CTrader.Signals;

namespace SignalsCheckIndicator
{
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class SignalsCheckIndicator : SignalsCheckBaseIndicator
    {
    }
}