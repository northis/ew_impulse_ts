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
        protected const string STATE_SAVE_KEY = "ReportStateMap";

        protected CTraderBaseAlgoRobot(Robot hostRobot, RobotParams robotParams, bool isBackTesting, string symbolName, string timeFrameName) : base(robotParams, isBackTesting, symbolName, timeFrameName)
        {
            m_HostRobot = hostRobot;

            m_HostRobot.Positions.Closed += PositionsClosed;
        }

        private void PositionsClosed(PositionClosedEventArgs obj)
        {
            PositionClosed.Invoke(this, new ClosedPositionEventArgs(obj.Reason));
        }

        protected override event EventHandler<ClosedPositionEventArgs> PositionClosed;
        protected override HashSet<string> GetSymbolNamesAvailable()
        {
            throw new NotImplementedException();
        }

        protected override ISymbol GetSymbol(string symbolName)
        {
            throw new NotImplementedException();
        }

        protected override ITimeFrame GetTimeFrame(string timeFrameName)
        {
            throw new NotImplementedException();
        }

        protected override void OnReportStateSave(Dictionary<string, int> stateMap)
        {
            m_HostRobot.LocalStorage.SetObject(STATE_SAVE_KEY, stateMap, LocalStorageScope.Device);
        }

        protected override Dictionary<string, int> GetSavedState()
        {
            return m_HostRobot.LocalStorage.GetObject<Dictionary<string, int>>(STATE_SAVE_KEY);
        }

        protected override TF CreateSetupFinder(ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            throw new NotImplementedException();
        }

        protected override IPosition[] GetPositions()
        {
            m_HostRobot.Positions.Select(a=>a.)
            throw new NotImplementedException();
        }
        
        protected override bool HasTradeBreakInside(
            DateTime dateStart, DateTime dateEnd, ISymbol symbol)
        {
            Symbol cTraderSymbol = symbol.ToSymbol();

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

        protected override OrderResult OpenOrder(bool isLong, ISymbol symbol, double volume, string botName, double stopInPips, double takeInPips,
            string positionId)
        {
            throw new NotImplementedException();
        }

        protected override double GetAccountBalance()
        {
            throw new NotImplementedException();
        }
    }
}
