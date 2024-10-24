﻿using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Impulse
{
    public class ImpulseSignalerAlgoRobot : ElliottWaveBaseAlgoRobot
    {
        private readonly Robot m_HostRobot;
        private readonly CTraderManager m_TradeManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpulseSignalerAlgoRobot"/> class.
        /// </summary>
        /// <param name="hostRobot">The host robot.</param>
        /// <param name="robotParams">The robot parameters.</param>
        public ImpulseSignalerAlgoRobot(Robot hostRobot, RobotParams robotParams) : this(hostRobot,
            new CTraderManager(hostRobot), new CTraderStorageManager(hostRobot), robotParams)
        {

        }

        private ImpulseSignalerAlgoRobot(Robot hostRobot, CTraderManager tradeManager, CTraderStorageManager storageManager, RobotParams robotParams) : base(tradeManager, storageManager, robotParams, hostRobot.IsBacktesting, hostRobot.SymbolName, hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_TradeManager = tradeManager;
            Init();
        }

        protected override IBarsProvider CreateBarsProvider(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            return CTraderBarsProvider.Create(timeFrame, symbolEntity, m_HostRobot.MarketData, m_TradeManager);
        }

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="timeFrame">The TF.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override ImpulseSetupFinder CreateSetupFinder(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            IBarsProvider barsProvider = CreateBarsProvider(timeFrame, symbolEntity);
            var barProvidersFactory = new BarProvidersFactory(
                m_TradeManager.GetCTraderSymbol(symbolEntity.Name), 
                m_HostRobot.MarketData, 
                m_TradeManager);
            var sf = new ImpulseSetupFinder(barsProvider, barProvidersFactory);
            return sf;
        }
    }
}