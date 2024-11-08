using cAlgo.API;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.CTrader.Core;
using TradeKit.CTrader.Impulse;

namespace ImpulseSignalerBot
{   
    /// <summary>
    /// Bot can trade setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class ImpulseSignalerRobot : CTraderBaseRobot<ImpulseSignalerAlgoRobot, ImpulseSetupFinder, ImpulseSignalEventArgs>
    {
        private ImpulseSignalerAlgoRobot m_ImpulseSignalerAlgoRobot;

        protected override void InitAlgoRobot()
        {
            m_ImpulseSignalerAlgoRobot = new ImpulseSignalerAlgoRobot(this, GetRobotParams());
        }

        protected override void DisposeAlgoRobot()
        {
            m_ImpulseSignalerAlgoRobot.Dispose();
        }

        protected override ImpulseSignalerAlgoRobot GetAlgoRobot()
        {
            return m_ImpulseSignalerAlgoRobot;
        }
    }
}