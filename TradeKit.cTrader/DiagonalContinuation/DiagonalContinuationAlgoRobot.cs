using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.DiagonalContinuation
{
    /// <summary>
    /// Research robot for the h1 diagonal-continuation trade (DIAGONAL.md §9.43).
    /// The wrapper finder suppresses the counter-trend signal; this robot tracks the running
    /// diagonal extreme and keeps a limit at <c>extreme - k*D</c> in the diagonal direction,
    /// with the stop at P0 and the target at <c>P0 + f*D</c>. This is the V3 dynamic re-price
    /// variant from the research harness.
    /// </summary>
    public class DiagonalContinuationAlgoRobot
        : BaseAlgoRobot<DiagonalContinuationSetupFinder, ElliottWaveSignalEventArgs>
    {
        private const string BOT_NAME = "DiagonalContinuationRobot";

        private readonly Robot m_HostRobot;
        private readonly CTraderManager m_TradeManager;
        private readonly EWParams m_EwParams;
        private readonly DiagonalParams m_DiagonalParams;
        private readonly DiagonalContinuationParams m_ContinuationParams;
        private readonly bool m_RequireHour1;
        private readonly bool m_CanTrade;
        private readonly HashSet<string> m_TimeFrameWarned = new();
        private readonly Dictionary<string, DiagonalContinuationPendingOrderManager> m_Managers =
            new();

        /// <summary>Gets the provider responsible for managing and retrieving bar data.</summary>
        public IBarsProvider BarsProvider { get; private set; }

        public DiagonalContinuationAlgoRobot(
            Robot hostRobot,
            RobotParams robotParams,
            EWParams ewParams,
            DiagonalParams diagonalParams,
            DiagonalContinuationParams continuationParams,
            bool requireHour1,
            bool allowToTrade)
            : this(hostRobot, new CTraderManager(hostRobot),
                new CTraderStorageManager(hostRobot), robotParams, ewParams,
                diagonalParams, continuationParams, requireHour1, allowToTrade)
        {
        }

        private DiagonalContinuationAlgoRobot(
            Robot hostRobot,
            CTraderManager tradeManager,
            CTraderStorageManager storageManager,
            RobotParams robotParams,
            EWParams ewParams,
            DiagonalParams diagonalParams,
            DiagonalContinuationParams continuationParams,
            bool requireHour1,
            bool allowToTrade)
            : base(tradeManager, storageManager, robotParams, hostRobot.IsBacktesting,
                hostRobot.SymbolName, hostRobot.TimeFrame.Name, false, false)
        {
            m_HostRobot = hostRobot;
            m_TradeManager = tradeManager;
            m_EwParams = ewParams;
            m_DiagonalParams = diagonalParams;
            m_ContinuationParams = continuationParams;
            m_RequireHour1 = requireHour1;
            m_CanTrade = allowToTrade || hostRobot.IsBacktesting;

            m_TradeManager.PositionOpened += OnPositionOpened;
            m_TradeManager.PositionClosed += OnPositionClosed;
            Init();
        }

        /// <inheritdoc/>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        protected override IBarsProvider CreateBarsProvider(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            BarsProvider = CTraderBarsProvider.Create(
                timeFrame, symbolEntity, m_HostRobot.MarketData, m_TradeManager);
            return BarsProvider;
        }

        protected override DiagonalContinuationSetupFinder CreateSetupFinder(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            IBarsProvider barsProvider = CreateBarsProvider(timeFrame, symbolEntity);
            return new DiagonalContinuationSetupFinder(
                barsProvider, symbolEntity, m_EwParams, m_DiagonalParams);
        }

        protected override void OnBarClosed(
            DiagonalContinuationSetupFinder setupFinder, int closedBarIndex)
        {
            if (!m_CanTrade)
                return;

            if (m_RequireHour1 &&
                setupFinder.TimeFrame.Name != TimeFrameHelper.Hour1.Name)
            {
                if (m_TimeFrameWarned.Add(setupFinder.Id))
                {
                    Logger.Write(
                        $"{setupFinder.Id}: H1 only is on, current timeframe is " +
                        $"{setupFinder.TimeFrame.Name}; the continuation trade is disabled");
                }

                return;
            }

            if (!m_Managers.TryGetValue(
                    setupFinder.Id, out DiagonalContinuationPendingOrderManager manager))
            {
                ElliottWaveSignalEventArgs signal = setupFinder.ConsumeDiagonalSignal();
                if (signal != null && !setupFinder.IsInSetup)
                {
                    var newManager = new DiagonalContinuationPendingOrderManager(
                        setupFinder,
                        m_TradeManager,
                        setupFinder.Symbol,
                        GetBotName(),
                        GetPendingVolume,
                        MaxVolumeLots,
                        m_ContinuationParams);

                    if (newManager.Start(signal, closedBarIndex))
                    {
                        m_Managers[setupFinder.Id] = newManager;
                        manager = newManager;
                    }
                }
            }

            if (manager == null)
                return;

            manager.OnBarClosed(closedBarIndex);
            if (manager.IsFinished)
            {
                m_Managers.Remove(setupFinder.Id);
                setupFinder.IsInSetup = false;
            }
        }

        private void OnPositionOpened(object sender, OpenedPositionEventArgs args)
        {
            foreach (DiagonalContinuationPendingOrderManager manager in m_Managers.Values)
                manager.OnPositionOpened(args.Position);
        }

        private void OnPositionClosed(object sender, ClosedPositionEventArgs args)
        {
            foreach (DiagonalContinuationPendingOrderManager manager in m_Managers.Values)
                manager.OnPositionClosed(args.Position);
        }

        /// <summary>
        /// Volume for the pending order, including the optional per-setup money limit (the
        /// same rule as the market entry in <see cref="BaseAlgoRobot{TF,TK}"/>).
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

        protected override bool HasSameSetupActive(
            DiagonalContinuationSetupFinder setupFinder, ElliottWaveSignalEventArgs signal)
        {
            return false;
        }

        protected override bool IsOvernightTrade(
            ElliottWaveSignalEventArgs signal, DiagonalContinuationSetupFinder setupFinder)
        {
            return false;
        }

        public override void Dispose()
        {
            m_TradeManager.PositionOpened -= OnPositionOpened;
            m_TradeManager.PositionClosed -= OnPositionClosed;
            base.Dispose();
        }
    }
}
