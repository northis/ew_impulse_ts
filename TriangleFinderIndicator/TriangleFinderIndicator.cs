using cAlgo.API;
using TradeKit.CTrader.Triangle;

namespace TriangleFinderIndicator
{
    /// <summary>
    /// Indicator can find possible setups based on the <b>running</b> ABCDE-triangle
    /// (see EW_R_TRIANGLE.md).
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class TriangleFinderIndicator : RunningTriangleFinderBaseIndicator
    {

    }
}
