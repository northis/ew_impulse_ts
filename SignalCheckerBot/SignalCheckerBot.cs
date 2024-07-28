using cAlgo.API;
using TradeKit.Signals;

namespace SignalCheckerBot
{
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class SignalCheckerBot : SignalsCTraderBaseRobot
    {
        private SignalsCheckAlgoRobot m_SignalsCheckAlgoRobot;

        protected override void InitAlgoRobot()
        {
            m_SignalsCheckAlgoRobot = new SignalsCheckAlgoRobot(this, GetRobotParams(), GetSignalsParams());
        }

        protected override void DisposeAlgoRobot()
        {
            m_SignalsCheckAlgoRobot.Dispose();
        }
    }
}