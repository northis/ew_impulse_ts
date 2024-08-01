using cAlgo.API;
using TradeKit.Gartley;

namespace GartleySignalerBot
{   
    /// <summary>
    /// Bot can trade setups based on Gartley patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class GartleySignalerBot : GartleyCTraderBaseRobot
    {
        private GartleySignalerAlgoRobot m_GartleySignalerAlgoRobot;

        protected override void InitAlgoRobot()
        {
            m_GartleySignalerAlgoRobot = new GartleySignalerAlgoRobot(
                this, GetRobotParams(), GetGartleyParams());
        }

        protected override void DisposeAlgoRobot()
        {
            m_GartleySignalerAlgoRobot.Dispose();
        }
    }
}