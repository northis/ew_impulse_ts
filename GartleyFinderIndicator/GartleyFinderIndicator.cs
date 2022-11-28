using cAlgo.API;
using TradeKit.Impulse;

namespace GartleyFinderIndicator
{
    /// <summary>
    /// Indicator can find possible setups based on Gartley patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class GartleyFinderIndicator : ImpulseFinderBaseIndicator
    {

    }
}
