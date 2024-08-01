using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.Rate;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Rate
{
    public class RateSignalerAlgoRobot : RateSignalerBaseAlgoRobot
    {
        private readonly Robot m_HostRobot;
        private readonly RateParams m_RateParams;

        public RateSignalerAlgoRobot(Robot hostRobot, RobotParams robotParams, RateParams rateParams) : base(new CTraderManager(hostRobot), robotParams, rateParams, hostRobot.IsBacktesting, hostRobot.SymbolName, hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_RateParams = rateParams;
        }

        protected override RateSetupFinder CreateSetupFinder(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            var barsProvider = new CTraderBarsProvider(m_HostRobot.Bars, symbolEntity);
            var sf = new RateSetupFinder(barsProvider, symbolEntity, m_RateParams.MaxBarSpeed, m_RateParams.MinBarSpeed,
                m_RateParams.SpeedPercent, m_RateParams.SpeedTpSlRatio);

            return sf;
        }
    }
}
