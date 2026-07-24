using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.Harmonic;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Harmonic
{
    /// <summary>
    /// Connects <see cref="HarmonicSetupFinder"/> to the existing cTrader robot infrastructure.
    /// </summary>
    public class HarmonicSignalerAlgoRobot : HarmonicBaseAlgoRobot
    {
        private readonly Robot m_HostRobot;
        private readonly CTraderManager m_CTraderManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="HarmonicSignalerAlgoRobot"/> class.
        /// </summary>
        /// <param name="hostRobot">The host robot.</param>
        /// <param name="robotParams">The common robot parameters.</param>
        /// <param name="harmonicParams">The harmonic search and setup settings.</param>
        public HarmonicSignalerAlgoRobot(
            Robot hostRobot, RobotParams robotParams, HarmonicParams harmonicParams)
            : base(new CTraderManager(hostRobot),
                new CTraderStorageManager(hostRobot),
                robotParams,
                harmonicParams,
                hostRobot.IsBacktesting,
                hostRobot.SymbolName,
                hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_CTraderManager = (CTraderManager)TradeManager;
            Init();
        }

        /// <inheritdoc/>
        protected override IBarsProvider CreateBarsProvider(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            return CTraderBarsProvider.Create(
                timeFrame, symbolEntity, m_HostRobot.MarketData, m_CTraderManager);
        }

        /// <inheritdoc/>
        protected override HarmonicSetupFinder CreateSetupFinder(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            return new HarmonicSetupFinder(
                CreateBarsProvider(timeFrame, symbolEntity), symbolEntity, HarmonicParams);
        }
    }
}
