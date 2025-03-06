using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.CTrader.Core
{
    public class CTraderManager: CTraderViewManager, ITradeManager
    {
        private readonly Robot m_Robot;
        private string m_BotName;

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
            m_Robot.Positions.Opened += PositionsOpened;
        }

        private bool IsOwn(Position pos)
        {
            if (string.IsNullOrEmpty(m_BotName) || pos.Label != m_BotName)
                return false;

            return true;
        }

        private void PositionsOpened(PositionOpenedEventArgs obj)
        {
            if (!IsOwn(obj.Position))
                return;

            PositionOpened?.Invoke(this, new OpenedPositionEventArgs(ToIPosition(obj.Position)));
        }

        private void PositionsClosed(PositionClosedEventArgs obj)
        {
            if (!IsOwn(obj.Position))
                return;

            PositionClosedState reason = m_ReasonMapper[obj.Reason];
            PositionClosed?.Invoke(this, new ClosedPositionEventArgs(reason, ToIPosition(obj.Position)));
        }

        public IPosition[] GetPositions()
        {
            IPosition[] positions = m_Robot.Positions
                .Where(a => a.Label == m_BotName)
                .Select(ToIPosition)
                .ToArray();
            return positions;
        }

        public void SetBotName(string name)
        {
            m_BotName = name;
        }

        public IPosition GetClosedPosition(string positionId, double? tp, double? sl)
        {
            HistoricalTrade res = m_Robot.History.FirstOrDefault(a => a.Comment == positionId);
            if (res == null)
                return null;

            IPosition pos = ToIPosition(res, tp, sl);
            return pos;
        }

        private IPosition ToIPosition(Position position)
        {
            if (position == null)
                return null;

            return new CTraderPosition(position.Id,
                GetSymbol(position.SymbolName),
                position.VolumeInUnits,
                position.TradeType == TradeType.Buy ? PositionType.BUY : PositionType.SELL,
                m_BotName,
                position.Comment,
                position.EntryTime,
                m_Robot.Time, 
                position.StopLoss, 
                position.TakeProfit,
                position.Swap, 
                position.Quantity, 
                position.NetProfit, 
                position.GrossProfit, 
                position.EntryPrice,
                position.CurrentPrice, 
                position.Commissions);
        }
        private IPosition ToIPosition(PendingOrder pendingOrder)
        {
            if (pendingOrder == null)
                return null;

            return new CTraderPosition(pendingOrder.Id,
                GetSymbol(pendingOrder.SymbolName),
                pendingOrder.VolumeInUnits,
                pendingOrder.TradeType == TradeType.Buy ? PositionType.BUY : PositionType.SELL,
                pendingOrder.Comment,
                m_BotName,
                m_Robot.Time,
                null,
                pendingOrder.StopLoss,
                pendingOrder.TakeProfit,
                0,
                pendingOrder.Quantity,
                0,
                0,
                pendingOrder.TargetPrice,
                pendingOrder.CurrentPrice,
                0);
        }

        private IPosition ToIPosition(HistoricalTrade position, double? tp, double? sl)
        {
            return new CTraderPosition(position.PositionId,
                GetSymbol(position.SymbolName),
                position.VolumeInUnits,
                position.TradeType == TradeType.Buy ? PositionType.BUY : PositionType.SELL,
                position.Comment,
                m_BotName,
                position.EntryTime,
                position.ClosingTime,
                sl,
                tp,
                position.Swap, 
                position.Quantity, 
                position.NetProfit, 
                position.GrossProfit, 
                position.EntryPrice,
                position.ClosingPrice, 
                position.Commissions);
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

        /// <summary>
        /// Occurs when the position is opened.
        /// </summary>
        public event EventHandler<OpenedPositionEventArgs> PositionOpened;

        /// <summary>
        /// Opens the trade order.
        /// </summary>
        /// <param name="positionId">The position identifier.</param>
        public OrderResult CancelOrder(string positionId)
        {
            PendingOrder pendingOrder = m_Robot.PendingOrders
                .FirstOrDefault(a => a.Label == m_BotName && a.Comment == positionId);
            if (pendingOrder == null)
                return null;

            TradeResult orderResult = pendingOrder.Cancel();
            return new OrderResult(orderResult.IsSuccessful, ToIPosition(orderResult.Position));
        }

        /// <summary>
        /// Opens the trade order.
        /// </summary>
        /// <param name="isLong">if set to <c>true</c> when it is buy trade; false for sell.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="volume">The volume.</param>
        /// <param name="botName">Name of the bot.</param>
        /// <param name="stopInPips">The stop in pips.</param>
        /// <param name="takeInPips">The take in pips.</param>
        /// <param name="positionId">The position identifier.</param>
        /// <param name="limitPrice">The limit price for pending orders</param>
        public OrderResult OpenOrder(
            bool isLong, 
            ISymbol symbol, 
            double volume, 
            string botName, 
            double stopInPips, 
            double takeInPips,
            string positionId,
            double? limitPrice)
        {
            double normalizedVolume = GetCTraderSymbol(symbol.Name).NormalizeVolumeInUnits(volume);

            TradeResult order;
            IPosition pos;
            TradeType tradeType = isLong ? TradeType.Buy : TradeType.Sell;
            if (limitPrice.HasValue)
            {
                order = m_Robot.PlaceLimitOrder(tradeType, symbol.Name, normalizedVolume, limitPrice.Value, botName,
                    stopInPips, takeInPips, null, positionId);
                pos = ToIPosition(order.PendingOrder);
            }
            else
            {
                order = m_Robot.ExecuteMarketOrder(tradeType, symbol.Name, normalizedVolume, botName, stopInPips,
                    takeInPips, positionId);
                pos = ToIPosition(order.Position);
            }

            return pos == null ? null : new OrderResult(order.IsSuccessful, pos);
        }

        public void SetStopLossPrice(IPosition position, double? price)
        {
            Position cTraderPosition = ToPosition(position);
            cTraderPosition.ModifyStopLossPrice(price);
        }

        public void SetBreakeven(IPosition position)
        {
            Position cTraderPosition = ToPosition(position);

            double newPrice = cTraderPosition.StopLoss.HasValue
                ? cTraderPosition.EntryPrice > cTraderPosition.StopLoss
                    ? cTraderPosition.EntryPrice + cTraderPosition.Symbol.Spread
                    : cTraderPosition.EntryPrice - cTraderPosition.Symbol.Spread
                : cTraderPosition.EntryPrice;

            cTraderPosition.ModifyStopLossPrice(newPrice);
            cTraderPosition.ModifyVolume(Convert.ToInt32(cTraderPosition.VolumeInUnits / 2));
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
    }
}
