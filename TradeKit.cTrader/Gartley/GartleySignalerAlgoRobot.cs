using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.Gartley;
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
            IBarsProvider cTraderBarsProvider = CreateBarsProvider(timeFrame, symbolEntity);
            var setupFinder = new GartleySetupFinder(
                cTraderBarsProvider, symbolEntity,
                GartleyParams.Accuracy, 
                GartleyParams.BarDepthCount,
                false, 
                GartleyParams.UseDivergences,
                GartleyParams.UseTrendOnly, 
                GartleyParams.UseCandlePatterns,
                GartleyParams.MaxPatternSizeBars);

            return setupFinder;
        }
    }
}