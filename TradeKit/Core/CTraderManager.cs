using cAlgo.API;
using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API.Internals;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core
{
    internal class CTraderManager: ITradeManager
    {
        private readonly Robot m_HostRobot;

        private readonly Dictionary<PositionCloseReason, PositionClosedState> m_ReasonMapper =
            new()
            {
                {PositionCloseReason.Closed, PositionClosedState.CLOSED},
                {PositionCloseReason.StopLoss, PositionClosedState.STOP_LOSS},
                {PositionCloseReason.TakeProfit, PositionClosedState.TAKE_PROFIT},
                {PositionCloseReason.StopOut, PositionClosedState.STOP_OUT},
            };

        private readonly Dictionary<string, ITimeFrame> m_ITimeFrameMap = new();
        private readonly Dictionary<string, TimeFrame> m_TimeFrameMap = new();
        private readonly Dictionary<string, ISymbol> m_ISymbolMap = new();
        private readonly Dictionary<string, Symbol> m_SymbolMap = new();

        public CTraderManager(Robot hostRobot)
        {
            m_HostRobot = hostRobot;
            m_HostRobot.Positions.Closed += PositionsClosed;
        }

        private void PositionsClosed(PositionClosedEventArgs obj)
        {
            PositionClosedState reason = m_ReasonMapper[obj.Reason];
            PositionClosed?.Invoke(this, new ClosedPositionEventArgs(reason));
        }

        public ITimeFrame GetTimeFrame(string timeFrameName)
        {
            if (m_ITimeFrameMap.TryGetValue(timeFrameName, out ITimeFrame value))
                return value;

            TimeFrame cTraderTimeFrame = TimeFrame.Parse(timeFrameName);
            m_TimeFrameMap[timeFrameName] = cTraderTimeFrame;

            value = new CTraderTimeFrame(cTraderTimeFrame);
            m_ITimeFrameMap[timeFrameName] = value;
            return value;
        }

        /// <summary>
        /// Gets the cTrader symbol.
        /// </summary>
        /// <param name="symbolName">Name of the symbol.</param>
        internal Symbol GetCTraderSymbol(string symbolName)
        {
            if (m_SymbolMap.TryGetValue(symbolName, out Symbol value))
                return value;

            value = m_HostRobot.Symbols.GetSymbol(symbolName);
            m_SymbolMap[symbolName] = value;

            return value;
        }

        public ISymbol GetSymbol(string symbolName)
        {
            if (m_ISymbolMap.TryGetValue(symbolName, out ISymbol value))
                return value;

            Symbol valueLocal = GetCTraderSymbol(symbolName);

            value = new SymbolBase(valueLocal.Name, valueLocal.Description, valueLocal.Id, valueLocal.Digits, valueLocal.PipSize, valueLocal.PipValue, valueLocal.LotSize);
            m_ISymbolMap[symbolName] = value;
            return value;
        }

        public IPosition[] GetPositions()
        {
            IPosition[] positions = m_HostRobot.Positions
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
            Position cTraderPosition = m_HostRobot.Positions
                .FirstOrDefault(a => a.Id == position.Id);
            if (cTraderPosition == null)
                throw new InvalidOperationException(
                    $"Position with ID={position.Id} hasn't been found.");

            return cTraderPosition;
        }

        public event EventHandler<ClosedPositionEventArgs> PositionClosed;

        public OrderResult OpenOrder(bool isLong, ISymbol symbol, double volume, string botName, double stopInPips, double takeInPips,
            string positionId)
        {
            TradeResult order = m_HostRobot.ExecuteMarketOrder(
                isLong ? TradeType.Buy: TradeType.Sell, symbol.Name, volume, botName, stopInPips, takeInPips, positionId);

            return new OrderResult(order.IsSuccessful, ToIPosition(order.Position));
        }

        public HashSet<string> GetSymbolNamesAvailable()
        {
            return m_HostRobot.Symbols.Select(a => a).ToHashSet();
        }

        public void SetStopLossPrice(IPosition position, double? price)
        {
            Position cTraderPosition = ToPosition(position);
            cTraderPosition.ModifyStopLossPrice(price);
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

        public double GetSpread(ISymbol symbol)
        {
            Symbol valueLocal = GetCTraderSymbol(symbol.Name);
            return valueLocal.Spread;
        }

        public double GetAsk(ISymbol symbol)
        {
            Symbol valueLocal = GetCTraderSymbol(symbol.Name);
            return valueLocal.Ask;
        }

        public double GetBid(ISymbol symbol)
        {
            Symbol valueLocal = GetCTraderSymbol(symbol.Name);
            return valueLocal.Bid;
        }

        public double GetAccountBalance()
        {
            return m_HostRobot.Account.Balance;
        }
    }
}
