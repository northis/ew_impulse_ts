using cAlgo.API;
using TradeKit.CTrader.PriceAction;

namespace PriceActionIndicator
{
    /// <summary>
    /// This indicator can find Price Action candle patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class PriceActionIndicator : PriceActionBaseIndicator
    {
        
    }
}