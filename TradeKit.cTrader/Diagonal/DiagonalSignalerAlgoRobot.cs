using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Diagonal
{
    /// <summary>
    /// Signaler robot for contracting-diagonal setups (see DIAGONAL.md).
    /// </summary>
    public class DiagonalSignalerAlgoRobot : DiagonalBaseAlgoRobot
    {
        private readonly Robot m_HostRobot;
        private readonly CTraderManager m_TradeManager;
        private readonly EWParams m_EwParams;
        private readonly DiagonalParams m_DiagonalParams;

        /// <summary>
        /// Gets the provider responsible for managing and retrieving bar data.
        /// </summary>
        public IBarsProvider BarsProvider { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DiagonalSignalerAlgoRobot"/> class.
        /// </summary>
        /// <param name="hostRobot">The host robot.</param>
        /// <param name="robotParams">The robot parameters.</param>
        /// <param name="ewParams">The Elliott-wave parameters.</param>
        /// <param name="diagonalParams">The diagonal-specific parameters.</param>
        public DiagonalSignalerAlgoRobot(Robot hostRobot, RobotParams robotParams,
            EWParams ewParams, DiagonalParams diagonalParams)
            : this(hostRobot, new CTraderManager(hostRobot),
                new CTraderStorageManager(hostRobot), robotParams, ewParams, diagonalParams)
        {
        }

        private DiagonalSignalerAlgoRobot(Robot hostRobot, CTraderManager tradeManager,
            CTraderStorageManager storageManager, RobotParams robotParams,
            EWParams ewParams, DiagonalParams diagonalParams)
            : base(tradeManager, storageManager, robotParams, hostRobot.IsBacktesting,
                hostRobot.SymbolName, hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_TradeManager = tradeManager;
            m_EwParams = ewParams;
            m_DiagonalParams = diagonalParams;
            Init();
        }

        protected override IBarsProvider CreateBarsProvider(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            BarsProvider = CTraderBarsProvider.Create(
                timeFrame, symbolEntity, m_HostRobot.MarketData, m_TradeManager);

            return BarsProvider;
        }

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="timeFrame">The TF.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override DiagonalSetupFinder CreateSetupFinder(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            IBarsProvider barsProvider = CreateBarsProvider(timeFrame, symbolEntity);
            return new DiagonalSetupFinder(
                barsProvider, symbolEntity, m_EwParams,
                m_DiagonalParams.TakeProfitRatio,
                m_DiagonalParams.RequireWave5Ratio,
                m_DiagonalParams.RequireWave4Ratio,
                m_DiagonalParams.RequireInitialMovement,
                m_DiagonalParams.TakeProfitMode,
                m_DiagonalParams.MinConvergence,
                m_DiagonalParams.RequireInsideWedge,
                m_DiagonalParams.MaxSpillAreaRatio,
                m_DiagonalParams.MinWave3Penetration,
                m_DiagonalParams.MaxWaveDurationRatio);
        }
    }
}
