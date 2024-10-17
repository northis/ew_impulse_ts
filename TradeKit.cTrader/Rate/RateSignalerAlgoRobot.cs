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
        private readonly CTraderManager m_TradeManager;
        public RateSignalerAlgoRobot(Robot hostRobot, RobotParams robotParams, RateParams rateParams) :
            this(hostRobot, new CTraderManager(hostRobot), robotParams, rateParams)
        {
        }

        private RateSignalerAlgoRobot(Robot hostRobot, CTraderManager tradeManager,  RobotParams robotParams, RateParams rateParams) : base(new CTraderManager(hostRobot), new CTraderStorageManager(hostRobot), robotParams, rateParams, hostRobot.IsBacktesting, hostRobot.SymbolName, hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_RateParams = rateParams;
            m_TradeManager = tradeManager;
            Init();
        }
        protected override IBarsProvider CreateBarsProvider(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            return CTraderBarsProvider.Create(timeFrame, symbolEntity, m_HostRobot.MarketData, m_TradeManager);
        }

        protected override RateSetupFinder CreateSetupFinder(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            IBarsProvider barsProvider = CreateBarsProvider(timeFrame, symbolEntity);
            var sf = new RateSetupFinder(barsProvider, symbolEntity, m_RateParams.MaxBarSpeed, m_RateParams.MinBarSpeed,
                m_RateParams.SpeedPercent, m_RateParams.SpeedTpSlRatio);

            return sf;
        }
    }
}
