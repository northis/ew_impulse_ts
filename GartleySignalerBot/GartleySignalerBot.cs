using cAlgo.API;
using TradeKit.Impulse;

namespace GartleySignalerBot
{   
    /// <summary>
    /// Bot can trade setups based on Gartley patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class GartleySignalerBot : ImpulseSignalerBaseRobot
    {
    }
}