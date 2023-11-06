using cAlgo.API;
using TradeKit.Impulse;
using TradeKit.Indicators;

namespace ImpulseFinderIndicator
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class ImpulseFinderIndicator : ExactZigzagIndicator
    {

    }
}
