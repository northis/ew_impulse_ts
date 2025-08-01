using cAlgo.API;
using TradeKit.CTrader.Triangle;

namespace TriangleFinderIndicator
{
    /// <summary>
    /// Indicator can find possible setups based on ABCDE-triangle
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class TriangleFinderIndicator : TriangleFinderBaseIndicator
    {

    }
}
