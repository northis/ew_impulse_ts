using cAlgo.API;
using TradeKit.PriceAction;

namespace PriceActionSignalerBot
{ 
    /// <summary>
    /// Bot can trade setups based on Price Action candle patterns
    /// </summary>
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class PriceActionSignalerBot: PriceActionCTraderBaseRobot
    {
        private PriceActionAlgoRobot m_PriceActionAlgoRobot;

        protected override void InitAlgoRobot()
        {
            m_PriceActionAlgoRobot = new PriceActionAlgoRobot(this, GetRobotParams(), GetPriceActionParams());
        }

        protected override void DisposeAlgoRobot()
        {
            m_PriceActionAlgoRobot.Dispose();
        }
    }
}