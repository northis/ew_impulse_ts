using cAlgo.API.Collections;
using cAlgo.API.Internals;
using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core
{
    public abstract class CTraderBaseAlgoRobot<TF, TK> : BaseAlgoRobot<TF, TK> where TF : BaseSetupFinder<TK> where TK : SignalEventArgs
    {
        private readonly Robot m_HostRobot; 
        private readonly CTraderManager m_CTraderManager; 
        protected const string STATE_SAVE_KEY = "ReportStateMap";

        protected CTraderBaseAlgoRobot(Robot hostRobot, RobotParams robotParams, bool isBackTesting, string symbolName, string timeFrameName) : base(new CTraderManager(hostRobot), robotParams, isBackTesting, symbolName, timeFrameName)
        {
            m_HostRobot = hostRobot;
            m_CTraderManager = (CTraderManager) TradeManager;
        }

        protected override void OnReportStateSave(Dictionary<string, int> stateMap)
        {
            m_HostRobot.LocalStorage.SetObject(STATE_SAVE_KEY, stateMap, LocalStorageScope.Device);
        }

        protected override Dictionary<string, int> GetSavedState()
        {
            return m_HostRobot.LocalStorage.GetObject<Dictionary<string, int>>(STATE_SAVE_KEY);
        }

        protected override double GetVolume(ISymbol symbol, double slPoints)
        {
            return m_CTraderManager.GetCTraderSymbol(symbol.Name)
                .NormalizeVolumeInUnits(base.GetVolume(symbol, slPoints));
        }

        protected override bool HasTradeBreakInside(
            DateTime dateStart, DateTime dateEnd, ISymbol symbol)
        {
            Symbol cTraderSymbol = m_CTraderManager.GetCTraderSymbol(symbol.Name);
            IReadonlyList<TradingSession> sessions = cTraderSymbol.MarketHours.Sessions;
            TimeSpan safeTimeDurationStart = TimeSpan.FromHours(1);

            DateTime setupDayStart = dateStart
                .Subtract(dateStart.TimeOfDay)
                .AddDays(-(int)dateStart.DayOfWeek);
            bool isSetupInDay = !sessions.Any();
            foreach (TradingSession session in sessions)
            {
                DateTime sessionDateTime = setupDayStart
                    .AddDays((int)session.StartDay)
                    .Add(session.StartTime)
                    .Add(safeTimeDurationStart);
                DateTime sessionEndTime = setupDayStart
                    .AddDays((int)session.EndDay)
                    .Add(session.EndTime)
                    .Add(-safeTimeDurationStart);

                if (dateStart > sessionDateTime && dateEnd < sessionEndTime)
                {
                    isSetupInDay = true;
                    break;
                }
            }

            return !isSetupInDay;
        }
    }
}
