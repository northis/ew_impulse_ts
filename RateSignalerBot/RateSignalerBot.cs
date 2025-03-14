using cAlgo.API;
using TradeKit.CTrader.Rate;

namespace RateSignalerBot
{
    /// <summary>
    /// Bot can trade high-speed part of the chart
    /// </summary>
    /// <seealso cref="RateCTraderBaseRobot" />
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class RateSignalerBot : RateCTraderBaseRobot<RateSignalerAlgoRobot>
    {
        private RateSignalerAlgoRobot m_RateSignalerAlgoRobot;
        protected override void InitAlgoRobot()
        {
            m_RateSignalerAlgoRobot = new RateSignalerAlgoRobot(
                this, GetRobotParams(), GetRateParams());
        }

        protected override void DisposeAlgoRobot()
        {
            m_RateSignalerAlgoRobot.Dispose();
        }

        protected override RateSignalerAlgoRobot GetAlgoRobot()
        {
            return m_RateSignalerAlgoRobot;
        }
    }
}