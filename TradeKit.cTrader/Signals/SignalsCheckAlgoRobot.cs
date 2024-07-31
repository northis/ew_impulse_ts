using System;
using System.IO;
using System.Linq;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.Core.Common;
using TradeKit.Core.Signals;

namespace TradeKit.Signals
{
    public class SignalsCheckAlgoRobot : SignalsCheckBaseAlgoRobot
    {
        private readonly Robot m_HostRobot;
        private readonly SignalsParams m_SignalsParams;

        public SignalsCheckAlgoRobot(Robot hostRobot, RobotParams robotParams, SignalsParams signalsParams) : base(new CTraderManager(hostRobot), robotParams, hostRobot.IsBacktesting, hostRobot.SymbolName, hostRobot.TimeFrame.Name)
        {
            m_HostRobot = hostRobot;
            m_SignalsParams = signalsParams;
            TradeManager.PositionClosed += TradeManagerOnPositionClosed;
        }

        private void TradeManagerOnPositionClosed(object sender, Core.EventArgs.ClosedPositionEventArgs e)
        {
            if (!m_SignalsParams.UseBreakeven)
            {
                return;
            }

            foreach (IPosition position in 
                     TradeManager.GetPositions().Where(a => a.Comment == GetBotName()))
            {
                TradeManager.SetBreakeven(position);
            }
        }

        protected override ParseSetupFinder CreateSetupFinder(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            IBarsProvider barsProvider = new CTraderBarsProvider(m_HostRobot.Bars, symbolEntity);
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

            if (!File.Exists(path))
            {
                return new NullParseSetupFinder(barsProvider, symbolEntity);
            }

            Logger.Write($"Using path {path}");

            var sf = new ParseSetupFinder(barsProvider, symbolEntity, path, m_SignalsParams.UseUtc, m_SignalsParams.UseOneTP);
            return sf;

        }
    }
}
