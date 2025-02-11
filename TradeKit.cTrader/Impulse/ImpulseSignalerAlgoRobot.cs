using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Impulse
{
    public class ImpulseSignalerAlgoRobot : ElliottWaveBaseAlgoRobot
    {
        private readonly Robot m_HostRobot;
        private readonly CTraderManager m_TradeManager;
        private readonly ImpulseParams m_ImpulseParams;


        /// <summary>
        /// Initializes a new instance of the <see cref="ImpulseSignalerAlgoRobot"/> class.
        /// </summary>
        /// <param name="hostRobot">The host robot.</param>
        /// <param name="robotParams">The robot parameters.</param>
        /// <param name="impulseParams">The impulse parameters.</param>
        public ImpulseSignalerAlgoRobot(Robot hostRobot, RobotParams robotParams, ImpulseParams impulseParams) : this(hostRobot,
            new CTraderManager(hostRobot), new CTraderStorageManager(hostRobot), robotParams, impulseParams)
        {

        }

        private ImpulseSignalerAlgoRobot(Robot hostRobot, CTraderManager tradeManager, CTraderStorageManager storageManager, RobotParams robotParams, ImpulseParams impulseParams) : base(tradeManager, storageManager, robotParams, hostRobot.IsBacktesting, hostRobot.SymbolName, hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_TradeManager = tradeManager;
            m_ImpulseParams = impulseParams;
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
            var sf = new ImpulseSetupFinder(barsProvider, m_ImpulseParams);
            return sf;
        }
    }
}