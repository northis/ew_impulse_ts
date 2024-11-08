using cAlgo.API;
using TradeKit.CTrader.Signals;

namespace SignalCheckerBot
{
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class SignalCheckerBot : SignalsCTraderBaseRobot<SignalsCheckAlgoRobot>
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

        protected override SignalsCheckAlgoRobot GetAlgoRobot()
        {
            return m_SignalsCheckAlgoRobot;
        }
    }
}