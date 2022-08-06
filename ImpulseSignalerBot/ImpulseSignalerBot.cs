using cAlgo.API;
using TradeKit.Impulse;

namespace ImpulseSignalerBot
{   /// <summary>
    /// Bot can trade setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class ImpulseSignalerBot : ImpulseSignalerBaseRobot
    {
    }
}