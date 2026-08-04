using cAlgo.API;
using TradeKit.CTrader.Harmonic;

namespace HarmonicSignalerBot
{
    /// <summary>
    /// Bot can trade setups based on harmonic patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class HarmonicSignalerBot : HarmonicCTraderBaseRobot<HarmonicSignalerAlgoRobot>
    {
        private HarmonicSignalerAlgoRobot m_HarmonicSignalerAlgoRobot;

        protected override void InitAlgoRobot()
        {
            m_HarmonicSignalerAlgoRobot = new HarmonicSignalerAlgoRobot(
                this, GetRobotParams(), GetHarmonicParams());
        }

        protected override void DisposeAlgoRobot()
        {
            m_HarmonicSignalerAlgoRobot.Dispose();
        }

        protected override HarmonicSignalerAlgoRobot GetAlgoRobot()
        {
            return m_HarmonicSignalerAlgoRobot;
        }
    }
}
