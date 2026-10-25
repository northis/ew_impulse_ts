using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Live pending-order loop for the managed line-13 diagonal mode (DIAGONAL.md §9.26).
    /// The robot calls <see cref="OnBarClosed"/> after every closed bar: the manager asks
    /// the finder for the line plan for the next candle, places or re-prices a limit order,
    /// and cancels it when the plan disappears. Fills and closes are reported by the robot
    /// via <see cref="OnPositionOpened"/> / <see cref="OnPositionClosed"/>.
    /// </summary>
    public class DiagonalPendingOrderManager
    {
        private sealed class PendingState
        {
            public string CandidateId { get; init; }
            public string Comment { get; init; }
            public int PendingOrderId { get; set; }
            public bool IsFilled { get; set; }
            public int PositionId { get; set; }
            public double Entry { get; set; }
            public double StopLoss { get; set; }
            public double TakeProfit { get; set; }
            public DateTime EntryTime { get; set; }
        }

        private readonly DiagonalSetupFinder m_Finder;
        private readonly ITradeManager m_TradeManager;
        private readonly ISymbol m_Symbol;
        private readonly string m_BotName;
        private readonly Func<ISymbol, double, double> m_VolumeProvider;
        private readonly double m_MaxVolumeLots;
        private PendingState m_State;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiagonalPendingOrderManager"/> class.
        /// </summary>
        public DiagonalPendingOrderManager(
            DiagonalSetupFinder finder,
            ITradeManager tradeManager,
            ISymbol symbol,
            string botName,
            Func<ISymbol, double, double> volumeProvider,
            double maxVolumeLots)
        {
            m_Finder = finder;
            m_TradeManager = tradeManager;
            m_Symbol = symbol;
            m_BotName = botName;
            m_VolumeProvider = volumeProvider;
            m_MaxVolumeLots = maxVolumeLots;
        }

        /// <summary>Whether a filled pending position is still open.</summary>
        public bool HasOpenPosition => m_State?.IsFilled == true;

        /// <summary>Id of the resting pending order, or 0 when there is none.</summary>
        public int PendingOrderId => m_State != null && !m_State.IsFilled
            ? m_State.PendingOrderId
            : 0;

        /// <summary>
        /// Places, re-prices or cancels the pending limit after the bar has closed.
        /// </summary>
        /// <param name="closedBarIndex">The just-closed bar index.</param>
        public void OnBarClosed(int closedBarIndex)
        {
            if (m_State?.IsFilled == true)
                return;

            // The fill event may arrive after the bar-close handler. Adopt an already
            // open position by its comment before placing anything new.
            if (m_State != null)
            {
                IPosition openPosition = m_TradeManager.GetPositions()
                    .FirstOrDefault(a => a.Comment == m_State.Comment);
                if (openPosition != null)
                {
                    OnPositionOpened(openPosition);
                    return;
                }
            }

            if (!m_Finder.TryGetPendingOrder(
                    closedBarIndex, null, out DiagonalPendingOrder plan))
            {
                CancelPending();
                return;
            }

            double bid = m_TradeManager.GetBid(m_Symbol);
            bool isLimitSide = plan.IsLong ? plan.Entry <= bid : plan.Entry > bid;
            if (!isLimitSide)
            {
                Logger.Write(
                    $"{m_Finder.Id}: pending line {plan.Entry:F5} is on the wrong side of bid {bid:F5}; skip");
                CancelPending();
                return;
            }

            double rangePips = Math.Abs(plan.TakeProfit - plan.StopLoss) / m_Symbol.PipSize;
            if (rangePips <= 0)
            {
                CancelPending();
                return;
            }

            double volume = m_VolumeProvider(m_Symbol, rangePips);
            double volumeInLots = volume / m_Symbol.LotSize;
            if (volume <= 0 || volumeInLots > m_MaxVolumeLots)
            {
                Logger.Write(
                    $"{m_Finder.Id}: pending volume {volumeInLots:F2} lots is out of range; skip");
                CancelPending();
                return;
            }

            if (m_State != null && m_State.CandidateId == plan.CandidateId)
            {
                IPosition existing = m_TradeManager.GetPendingOrders(m_Symbol)
                    .FirstOrDefault(a => a.Id == m_State.PendingOrderId);
                if (existing != null)
                {
                    OrderResult modified = m_TradeManager.ModifyPendingOrder(
                        existing, plan.Entry, plan.StopLoss, plan.TakeProfit);
                    if (modified?.IsSuccessful == true)
                    {
                        m_State.Entry = plan.Entry;
                        m_State.StopLoss = plan.StopLoss;
                        m_State.TakeProfit = plan.TakeProfit;
                        m_State.EntryTime = plan.EntryBarTime;
                        Logger.Write(
                            $"{m_Finder.Id}: pending re-priced to {plan.Entry:F5} (SL {plan.StopLoss:F5}, TP {plan.TakeProfit:F5})");
                        return;
                    }

                    Logger.Write($"{m_Finder.Id}: pending modify failed, re-placing");
                    CancelPending();
                }
                else
                {
                    // The order is gone but no fill was reported; treat it as not filled.
                    m_State = null;
                }
            }
            else
            {
                CancelPending();
            }

            var entryPoint = new BarPoint(
                plan.Entry, plan.EntryBarTime, m_Finder.TimeFrame, plan.EntryBarIndex);
            string comment = Helper.GetPositionId(m_Finder.Id, entryPoint, "P");

            // Re-check the side right before the placement: a live tick could have moved
            // the market between the first check and the cancel/replace.
            double bidNow = m_TradeManager.GetBid(m_Symbol);
            bool isLimitSideNow = plan.IsLong ? plan.Entry <= bidNow : plan.Entry > bidNow;
            if (!isLimitSideNow)
            {
                Logger.Write(
                    $"{m_Finder.Id}: pending line {plan.Entry:F5} moved to the wrong side of bid {bidNow:F5}; skip");
                return;
            }

            OrderResult order = m_TradeManager.OpenOrder(
                plan.IsLong, m_Symbol, volume, m_BotName,
                plan.StopLoss, plan.TakeProfit, comment, plan.Entry);

            if (order?.IsSuccessful == true)
            {
                m_State = new PendingState
                {
                    CandidateId = plan.CandidateId,
                    Comment = comment,
                    PendingOrderId = order.Position.Id,
                    Entry = plan.Entry,
                    StopLoss = plan.StopLoss,
                    TakeProfit = plan.TakeProfit,
                    EntryTime = plan.EntryBarTime
                };
                Logger.Write(
                    $"{m_Finder.Id}: pending placed at {plan.Entry:F5} (SL {plan.StopLoss:F5}, TP {plan.TakeProfit:F5}, R:R {plan.RiskReward:F2})");
            }
            else
            {
                Logger.Write($"{m_Finder.Id}: pending order failed");
            }
        }

        /// <summary>Marks the resting order as filled when the broker opens the position.</summary>
        public void OnPositionOpened(IPosition position)
        {
            if (m_State == null || position.Comment != m_State.Comment)
                return;

            m_State.IsFilled = true;
            m_State.PositionId = position.Id;
            m_Finder.MarkPendingOrderFilled(m_State.CandidateId);
            Logger.Write(
                $"{m_Finder.Id}: pending filled at {position.EntryPrice:F5} (position {position.Id})");
        }

        /// <summary>Clears the state when the filled position closes.</summary>
        public void OnPositionClosed(IPosition position)
        {
            if (m_State == null || position.Comment != m_State.Comment)
                return;

            m_State = null;
            Logger.Write($"{m_Finder.Id}: pending position closed");
        }

        private void CancelPending()
        {
            if (m_State == null)
                return;

            if (!m_State.IsFilled)
                m_TradeManager.CancelOrder(m_State.Comment);

            m_State = null;
        }
    }
}
