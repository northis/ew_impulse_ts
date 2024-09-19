using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.CTrader.Core
{
    public class CTraderManager: CTraderViewManager, ITradeManager
    {
        private readonly Robot m_Robot;
        protected const string STATE_SAVE_KEY = "ReportStateMap";

        private readonly Dictionary<PositionCloseReason, PositionClosedState> m_ReasonMapper =
            new()
            {
                {PositionCloseReason.Closed, PositionClosedState.CLOSED},
                {PositionCloseReason.StopLoss, PositionClosedState.STOP_LOSS},
                {PositionCloseReason.TakeProfit, PositionClosedState.TAKE_PROFIT},
                {PositionCloseReason.StopOut, PositionClosedState.STOP_OUT},
            };

        public CTraderManager(Robot robot) : base(robot)
        {
            m_Robot = robot;
            m_Robot.Positions.Closed += PositionsClosed;
        }

        private void PositionsClosed(PositionClosedEventArgs obj)
        {
            PositionClosedState reason = m_ReasonMapper[obj.Reason];
            PositionClosed?.Invoke(this, new ClosedPositionEventArgs(reason));
        }

        public IPosition[] GetPositions()
        {
            IPosition[] positions = m_Robot.Positions
                    .Select(ToIPosition)
                    .ToArray();
            return positions;
        }

        private IPosition ToIPosition(Position position)
        {
            return new CTraderPosition(position.Id,
                GetSymbol(position.SymbolName),
                position.VolumeInUnits,
                position.TradeType == TradeType.Buy ? PositionType.BUY : PositionType.SELL,
                position.Comment);
        }

        private Position ToPosition(IPosition position)
        {
            Position cTraderPosition = m_Robot.Positions
                .FirstOrDefault(a => a.Id == position.Id);
            if (cTraderPosition == null)
                throw new InvalidOperationException(
                    $"Position with ID={position.Id} hasn't been found.");

            return cTraderPosition;
        }

        /// <summary>
        /// Occurs when the position is closed.
        /// </summary>
        public event EventHandler<ClosedPositionEventArgs> PositionClosed;

        public OrderResult OpenOrder(bool isLong, ISymbol symbol, double volume, string botName, double stopInPips, double takeInPips,
            string positionId)
        {
            TradeResult order = m_Robot.ExecuteMarketOrder(
                isLong ? TradeType.Buy: TradeType.Sell, symbol.Name, volume, botName, stopInPips, takeInPips, positionId);

            return order.Position == null ? null : new OrderResult(order.IsSuccessful, ToIPosition(order.Position));
        }

        public void SetStopLossPrice(IPosition position, double? price)
        {
            Position cTraderPosition = ToPosition(position);
            cTraderPosition.ModifyStopLossPrice(price);
        }

        public void SetBreakeven(IPosition position)
        {
            Position cTraderPosition = ToPosition(position);
            cTraderPosition.ModifyStopLossPrice(cTraderPosition.EntryPrice);
        }

        public void SetTakeProfitPrice(IPosition position, double? price)
        {
            Position cTraderPosition = ToPosition(position);
            cTraderPosition.ModifyTakeProfitPrice(price);
        }

        public OrderResult Close(IPosition position)
        {
            Position cTraderPosition = ToPosition(position);
            TradeResult order = cTraderPosition.Close();
            return new OrderResult(order.IsSuccessful, ToIPosition(order.Position));
        }

        public double GetAccountBalance()
        {
            return m_Robot.Account.Balance;
        }

        public void SaveState(Dictionary<string, int> stateMap)
        {
            m_Robot.LocalStorage.SetObject(STATE_SAVE_KEY, stateMap, LocalStorageScope.Device);
        }

        public Dictionary<string, int> GetSavedState()
        {
            return m_Robot.LocalStorage.GetObject<Dictionary<string, int>>(STATE_SAVE_KEY);
        }
    }
}
