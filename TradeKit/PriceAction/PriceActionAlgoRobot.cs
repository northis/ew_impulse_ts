using cAlgo.API;
using System.Collections.Generic;
using TradeKit.Core;
using TradeKit.Core.Common;
using TradeKit.Core.PriceAction;

namespace TradeKit.PriceAction
{
    public class PriceActionAlgoRobot: PriceActionBaseAlgoBot
    {
        private readonly Robot m_HostRobot;
        private readonly PriceActionParams m_PriceActionParams;

        public PriceActionAlgoRobot(Robot hostRobot, RobotParams robotParams, PriceActionParams priceActionParams) : base(new CTraderManager(hostRobot), robotParams, priceActionParams, hostRobot.IsBacktesting, hostRobot.SymbolName, hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_PriceActionParams = priceActionParams;
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
