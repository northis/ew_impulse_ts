using System;
using System.IO;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.Signals;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Signals
{
    public class SignalsCheckAlgoRobot : SignalsCheckBaseAlgoRobot
    {
        private readonly Robot m_HostRobot;
        private readonly SignalsParams m_SignalsParams;
        private readonly CTraderManager m_TradeManager;
        public SignalsCheckAlgoRobot(Robot hostRobot, RobotParams robotParams, SignalsParams signalsParams) 
            : this(hostRobot, new CTraderManager(hostRobot), new CTraderStorageManager(hostRobot), robotParams, signalsParams)
        {
        }

        private SignalsCheckAlgoRobot(Robot hostRobot, CTraderManager tradeManager,
            CTraderStorageManager storageManager, RobotParams robotParams, SignalsParams signalsParams) : base(
            tradeManager, storageManager, robotParams, hostRobot.IsBacktesting, hostRobot.SymbolName,
            hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_SignalsParams = signalsParams;
            m_TradeManager = tradeManager;
            TradeManager.PositionClosed += TradeManagerOnPositionClosed;
            Init();
        }

        protected override IBarsProvider CreateBarsProvider(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            return CTraderBarsProvider.Create(timeFrame, symbolEntity, m_HostRobot.MarketData, m_TradeManager);
        }

        private void TradeManagerOnPositionClosed(object sender, TradeKit.Core.EventArgs.ClosedPositionEventArgs e)
        {
            if (!m_SignalsParams.UseBreakeven)
            {
                return;
            }

            foreach (IPosition position in 
                     TradeManager.GetPositions().Where(a => a.Label == GetBotName()))
            {
                TradeManager.SetBreakeven(position, null);
            }
        }

        protected override ParseSetupFinder CreateSetupFinder(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            IBarsProvider barsProvider = CreateBarsProvider(timeFrame, symbolEntity);
            string path;
            if (string.IsNullOrEmpty(m_SignalsParams.SignalHistoryFilePath))
            {
                if (!Directory.Exists(m_SignalsParams.BulkHistoryFolderPath))
                {
                    throw new InvalidOperationException(
                        $"No folder {m_SignalsParams.SignalHistoryFilePath} found!");
                }

                string[] files = Directory.GetFiles(m_SignalsParams.BulkHistoryFolderPath)
                    .OrderBy(a => a)
                    .ToArray();
                if (m_SignalsParams.ZeroBasedFileIndexAsc < 0 || m_SignalsParams.ZeroBasedFileIndexAsc >= files.Length)
                {
                    throw new InvalidOperationException("Bad file index!");
                }

                path = Path.Combine(m_SignalsParams.BulkHistoryFolderPath, files[m_SignalsParams.ZeroBasedFileIndexAsc]);
            }
            else
            {
                path = m_SignalsParams.SignalHistoryFilePath;
            }

            var twm = new CTraderViewManager(m_HostRobot);
            if (!File.Exists(path))
            {
                return new NullParseSetupFinder(barsProvider, symbolEntity, twm);
            }

            Logger.Write($"Using path {path}");

            var sf = new ParseSetupFinder(barsProvider, symbolEntity, twm, path, m_SignalsParams.UseUtc, m_SignalsParams.UseOneTP);
            return sf;

        }
    }
}
