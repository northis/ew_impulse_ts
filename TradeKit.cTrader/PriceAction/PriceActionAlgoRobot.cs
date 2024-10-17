using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.PriceAction;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.PriceAction
{
    public class PriceActionAlgoRobot: PriceActionBaseAlgoBot
    {
        private readonly Robot m_HostRobot;
        private readonly PriceActionParams m_PriceActionParams;
        private readonly CTraderManager m_TradeManager;

        public PriceActionAlgoRobot(Robot hostRobot, RobotParams robotParams, PriceActionParams priceActionParams) : this(hostRobot, new CTraderManager(hostRobot),
            new CTraderStorageManager(hostRobot), robotParams, priceActionParams)
        {
        }

        private PriceActionAlgoRobot(Robot hostRobot, CTraderManager tradeManager, CTraderStorageManager storageManager,
            RobotParams robotParams, PriceActionParams priceActionParams) :
            base(tradeManager, storageManager, robotParams, priceActionParams, hostRobot.IsBacktesting,
                hostRobot.SymbolName, hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_PriceActionParams = priceActionParams;
            m_TradeManager = tradeManager;
            Init();
        }

        protected override IBarsProvider CreateBarsProvider(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            return CTraderBarsProvider.Create(timeFrame, symbolEntity, m_HostRobot.MarketData, m_TradeManager);
        }

        protected override PriceActionSetupFinder CreateSetupFinder(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            var cTraderBarsProvider = new CTraderBarsProvider(m_HostRobot.Bars, symbolEntity);
            HashSet<CandlePatternType> patternTypes = GetPatternsType();

            SuperTrendItem superTrendItem = null;
            if (m_PriceActionParams.UseTrendOnly)
                superTrendItem = SuperTrendItem.Create(cTraderBarsProvider.TimeFrame, cTraderBarsProvider);

            double? breakEvenRatio = null;
            if (m_PriceActionParams.BreakEvenRatio > 0)
                breakEvenRatio = m_PriceActionParams.BreakEvenRatio;

            var setupFinder = new PriceActionSetupFinder(
                cTraderBarsProvider, symbolEntity, m_PriceActionParams.UseStrengthBar, superTrendItem, patternTypes, breakEvenRatio);

            return setupFinder;
        }
    }
}
