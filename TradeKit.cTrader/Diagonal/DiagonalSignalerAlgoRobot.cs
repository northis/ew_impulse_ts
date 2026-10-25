using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
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
        private readonly Dictionary<string, DiagonalPendingOrderManager> m_PendingManagers =
            new();

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
            m_TradeManager.PositionOpened += OnPendingPositionOpened;
            m_TradeManager.PositionClosed += OnPendingPositionClosed;
        }

        private void OnPendingPositionOpened(object sender, OpenedPositionEventArgs args)
        {
            foreach (DiagonalPendingOrderManager manager in m_PendingManagers.Values)
                manager.OnPositionOpened(args.Position);
        }

        private void OnPendingPositionClosed(object sender, ClosedPositionEventArgs args)
        {
            foreach (DiagonalPendingOrderManager manager in m_PendingManagers.Values)
                manager.OnPositionClosed(args.Position);
        }

        /// <inheritdoc/>
        protected override void OnBarClosed(DiagonalSetupFinder setupFinder, int closedBarIndex)
        {
            if (m_PendingManagers.TryGetValue(
                    setupFinder.Id, out DiagonalPendingOrderManager manager))
            {
                manager.OnBarClosed(closedBarIndex);
            }
        }

        protected override IBarsProvider CreateBarsProvider(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            BarsProvider = CTraderBarsProvider.Create(
                timeFrame, symbolEntity, m_HostRobot.MarketData, m_TradeManager);

            return BarsProvider;
        }

        /// <summary>
        /// Volume for the managed pending order, including the optional per-setup money
        /// limit (the same rule as the market entry in BaseAlgoRobot).
        /// </summary>
        private double GetPendingVolume(ISymbol symbol, double rangePips)
        {
            double volume = GetVolume(symbol, rangePips);
            if (MaxMoneyPerSetup > 0)
            {
                double moneyPerSetup =
                    RiskPercentFromDeposit * TradeManager.GetAccountBalance() / 100;
                double moneyLimitRatio = moneyPerSetup / MaxMoneyPerSetup;
                if (moneyLimitRatio > 1)
                    volume /= moneyLimitRatio;
            }

            return volume;
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
            var finder = new DiagonalSetupFinder(
                barsProvider, symbolEntity, m_EwParams,
                m_DiagonalParams.TakeProfitRatio,
                m_DiagonalParams.RequireWave5Ratio,
                m_DiagonalParams.RequireWave4Ratio,
                false, // legacy requireInitialDiagonal; InitialMode below
                m_DiagonalParams.TakeProfitMode,
                m_DiagonalParams.MinConvergence,
                m_DiagonalParams.MaxConvergence,
                m_DiagonalParams.RequireInsideWedge,
                m_DiagonalParams.MaxSpillAreaRatio,
                m_DiagonalParams.MinWave3Penetration,
                m_DiagonalParams.MaxWaveDurationRatio,
                retraceAction: m_DiagonalParams.RetraceAction,
                minRiskRewardRatio: m_DiagonalParams.MinRiskRewardRatio,
                wave3RetraceRatio: m_DiagonalParams.Wave3RetraceRatio,
                minWave4Wave2Level: m_DiagonalParams.MinWave4Wave2Level,
                minWave4RetraceW3: m_DiagonalParams.MinWave4RetraceW3,
                maxWave4RetraceW3: m_DiagonalParams.MaxWave4RetraceW3,
                requireWave4Shorter: m_DiagonalParams.RequireWave4Shorter,
                requireWave2Shorter: m_DiagonalParams.RequireWave2Shorter,
                minWave2Retrace: m_DiagonalParams.MinWave2Retrace,
                maxWave2Retrace: m_DiagonalParams.MaxWave2Retrace,
                maxWave5SpillRatio: m_DiagonalParams.MaxWave5SpillRatio,
                minWave4Wave2DurationRatio: m_DiagonalParams.MinWave4Wave2DurationRatio,
                minWave3Wave1DurationRatio: m_DiagonalParams.MinWave3Wave1DurationRatio,
                minWave2Wave1DurationRatio: m_DiagonalParams.MinWave2Wave1DurationRatio,
                requireThickWaveEnds: m_DiagonalParams.RequireThickWaveEnds,
                tradeViewManager: m_TradeManager,
                thinMarketGuard: m_DiagonalParams.ThinMarketGuard,
                takeProfitRetraceRatio: m_DiagonalParams.TakeProfitRetraceRatio,
                stopWave3Ratio: m_DiagonalParams.StopWave3Ratio,
                triggerMode: m_DiagonalParams.TriggerMode,
                wave5SpillMode: m_DiagonalParams.Wave5SpillMode,
                wave5SpillThreshold: m_DiagonalParams.Wave5SpillThreshold,
                minSignalRiskRewardRatio: m_DiagonalParams.MinSignalRiskRewardRatio,
                initialMode: m_DiagonalParams.InitialMode);

            if (m_DiagonalParams.TriggerMode ==
                DiagonalTriggerMode.LINE13_PENDING_MANAGED)
            {
                m_PendingManagers[finder.Id] = new DiagonalPendingOrderManager(
                    finder,
                    m_TradeManager,
                    symbolEntity,
                    GetBotName(),
                    GetPendingVolume,
                    MaxVolumeLots);
            }

            return finder;
        }
    }
}
