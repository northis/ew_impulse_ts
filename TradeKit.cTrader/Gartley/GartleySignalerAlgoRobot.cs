using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.Gartley;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Gartley
{
    public class GartleySignalerAlgoRobot : GartleyBaseAlgoRobot
    {
        private readonly Robot m_HostRobot;
        private readonly CTraderManager m_CTraderManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="GartleySignalerAlgoRobot"/> class.
        /// </summary>
        /// <param name="hostRobot">The host robot.</param>
        /// <param name="robotParams">The robot parameters.</param>
        /// <param name="gartleyParams">Gartley parameters.</param>
        public GartleySignalerAlgoRobot(Robot hostRobot, RobotParams robotParams, GartleyParams gartleyParams)
            : base(new CTraderManager(hostRobot), 
                new CTraderStorageManager(hostRobot),
                robotParams, 
                gartleyParams, 
                hostRobot.IsBacktesting,
                hostRobot.SymbolName, 
                hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_CTraderManager = (CTraderManager)TradeManager;
            Init();
        }

        protected override IBarsProvider CreateBarsProvider(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            return CTraderBarsProvider.Create(timeFrame, symbolEntity, m_HostRobot.MarketData, m_CTraderManager);
        }

        protected override GartleySetupFinder CreateSetupFinder(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            HashSet<GartleyPatternType> patternTypes = GetPatternsType();
            IBarsProvider cTraderBarsProvider = CreateBarsProvider(timeFrame, symbolEntity);
            var ao = new AwesomeOscillatorFinder(cTraderBarsProvider);

            SupertrendFinder supertrendFinder = null;
            if (GartleyParams.UseTrendOnly)
                supertrendFinder = new SupertrendFinder(cTraderBarsProvider);


            CandlePatternFinder cpf = GartleyParams.UseCandlePatterns
                ? new CandlePatternFinder(cTraderBarsProvider)
                : null;

            double? breakEvenRatio = null;
            if (GartleyParams.BreakEvenRatio > 0)
                breakEvenRatio = GartleyParams.BreakEvenRatio;

            var setupFinder = new GartleySetupFinder(
                cTraderBarsProvider, symbolEntity,
                GartleyParams.Accuracy, GartleyParams.BarDepthCount, GartleyParams.UseDivergences,
                supertrendFinder, patternTypes, ao, cpf, breakEvenRatio);

            return setupFinder;
        }
    }
}