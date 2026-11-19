using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Live limit-order loop for the diagonal-continuation robot (DIAGONAL.md §9.43).
    /// After a completed leading diagonal the manager tracks the running extreme, keeps a
    /// limit at <c>extreme - k*D</c> in the diagonal direction, stops at <c>P0</c> and targets
    /// <c>P0 + f*D</c> (or the end/entry anchor). This is the V3 "dynamic re-price" variant:
    /// the deep correction itself is the entry trigger; no separate end confirmation is used.
    /// </summary>
    public class DiagonalContinuationPendingOrderManager
    {
        private sealed class PendingState
        {
            public string Comment { get; set; }
            public int PendingOrderId { get; set; }
            public bool IsFilled { get; set; }
            public int PositionId { get; set; }
            public double Entry { get; set; }
            public double StopLoss { get; set; }
            public double TakeProfit { get; set; }
            public int EntryBarIndex { get; set; } = -1;
            public bool TimeStopTried { get; set; }
        }

        private readonly DiagonalContinuationSetupFinder m_Finder;
        private readonly ITradeManager m_TradeManager;
        private readonly ISymbol m_Symbol;
        private readonly string m_BotName;
        private readonly Func<ISymbol, double, double> m_VolumeProvider;
        private readonly double m_MaxVolumeLots;
        private readonly DiagonalContinuationParams m_Params;

        private PendingState m_State;
        private bool m_IsUp;
        private double m_P0;
        private double m_Extreme;
        private int m_ExtremeBar;
        private int m_SignalBar;
        private DateTime m_SignalTime;
        private int m_LastClosedBar = -1;

        public DiagonalContinuationPendingOrderManager(
            DiagonalContinuationSetupFinder finder,
            ITradeManager tradeManager,
            ISymbol symbol,
            string botName,
            Func<ISymbol, double, double> volumeProvider,
            double maxVolumeLots,
            DiagonalContinuationParams parameters)
        {
            m_Finder = finder;
            m_TradeManager = tradeManager;
            m_Symbol = symbol;
            m_BotName = botName;
            m_VolumeProvider = volumeProvider;
            m_MaxVolumeLots = maxVolumeLots;
            m_Params = parameters;
        }

        /// <summary>Whether a filled position is still open.</summary>
        public bool HasOpenPosition => m_State?.IsFilled == true;

        /// <summary>Whether the manager has nothing left to do.</summary>
        public bool IsFinished => m_State == null;

        /// <summary>Id of the resting pending order, or 0 when there is none.</summary>
        public int PendingOrderId => m_State != null && !m_State.IsFilled
            ? m_State.PendingOrderId
            : 0;

        /// <summary>
        /// Starts a continuation setup from the diagonal signal. The first limit is placed by
        /// the following <see cref="OnBarClosed"/> call for the signal bar.
        /// </summary>
        public bool Start(ElliottWaveSignalEventArgs signal, int signalBarIndex)
        {
            if (m_State != null || signal?.WavePoints == null || signal.WavePoints.Length < 6)
                return false;

            BarPoint p0 = signal.WavePoints[0];
            BarPoint p5 = signal.WavePoints[5];
            double height = Math.Abs(p5.Value - p0.Value);
            if (height <= 1e-12)
                return false;

            m_IsUp = signal.TakeProfit.Value < signal.StopLoss.Value;
            m_P0 = p0.Value;
            m_Extreme = p5.Value;
            m_ExtremeBar = p5.BarIndex;
            m_SignalBar = signal.Level.BarIndex;
            m_SignalTime = signal.Level.OpenTime;
            m_LastClosedBar = signalBarIndex;

            var entryPoint = new BarPoint(
                m_Extreme, m_SignalTime, m_Finder.TimeFrame, m_SignalBar);
            m_State = new PendingState
            {
                Comment = Helper.GetPositionId(m_Finder.Id, entryPoint, "C")
            };

            m_Finder.IsInSetup = true;
            Logger.Write(
                $"{m_Finder.Id}: continuation start {m_Symbol.Name} " +
                $"{(m_IsUp ? "long" : "short")} P0={m_P0:F5} P5={m_Extreme:F5} D={height:F5}");
            return true;
        }

        /// <summary>
        /// Places, re-prices or cancels the pending limit after the bar has closed, and
        /// applies the optional time stop to a filled position.
        /// </summary>
        public void OnBarClosed(int closedBarIndex)
        {
            m_LastClosedBar = closedBarIndex;
            if (m_State == null)
                return;

            if (m_State.IsFilled)
            {
                TryTimeStop(closedBarIndex);
                return;
            }

            // A fill event may arrive after the bar-close handler. Adopt an already open
            // position by its comment before touching the pending order.
            IPosition openPosition = m_TradeManager.GetPositions()
                .FirstOrDefault(a => a.Comment == m_State.Comment);
            if (openPosition != null)
            {
                OnPositionOpened(openPosition);
                TryTimeStop(closedBarIndex);
                return;
            }

            if (m_State.PendingOrderId != 0)
            {
                IPosition existing = m_TradeManager.GetPendingOrders(m_Symbol)
                    .FirstOrDefault(a => a.Id == m_State.PendingOrderId);
                if (existing == null)
                {
                    // The order is gone but no fill was reported: re-place it below if the
                    // setup is still valid.
                    m_State.PendingOrderId = 0;
                }
            }

            if (closedBarIndex > m_SignalBar)
            {
                double high = m_Finder.BarsProvider.GetHighPrice(closedBarIndex);
                double low = m_Finder.BarsProvider.GetLowPrice(closedBarIndex);

                // The diagonal is invalid once the origin is traded through before the fill.
                if (m_IsUp ? low <= m_P0 : high >= m_P0)
                {
                    Logger.Write(
                        $"{m_Finder.Id}: continuation invalidated by P0 at bar {closedBarIndex}");
                    CancelPending();
                    return;
                }

                if (m_IsUp && high > m_Extreme)
                {
                    m_Extreme = high;
                    m_ExtremeBar = closedBarIndex;
                }
                else if (!m_IsUp && low < m_Extreme)
                {
                    m_Extreme = low;
                    m_ExtremeBar = closedBarIndex;
                }
            }

            if (closedBarIndex - m_SignalBar >= m_Params.MaxWaitBars)
            {
                Logger.Write(
                    $"{m_Finder.Id}: continuation wait expired at bar {closedBarIndex}");
                CancelPending();
                return;
            }

            PlaceOrModify(closedBarIndex);
        }

        /// <summary>Marks the resting order as filled when the broker opens the position.</summary>
        public void OnPositionOpened(IPosition position)
        {
            if (m_State == null || position.Comment != m_State.Comment)
                return;

            m_State.IsFilled = true;
            m_State.PositionId = position.Id;
            m_State.EntryBarIndex = m_Finder.BarsProvider.GetIndexByTime(position.EnterDateTime);
            m_Finder.IsInSetup = true;
            Logger.Write(
                $"{m_Finder.Id}: continuation filled at {position.EntryPrice:F5} " +
                $"(position {position.Id}, entry bar {m_State.EntryBarIndex})");
        }

        /// <summary>Clears the state when the filled position closes.</summary>
        public void OnPositionClosed(IPosition position)
        {
            if (m_State == null || position.Id != m_State.PositionId)
                return;

            Logger.Write(
                $"{m_Finder.Id}: continuation position closed at {position.CurrentPrice:F5}");
            m_State = null;
            m_Finder.IsInSetup = false;
        }

        private void TryTimeStop(int closedBarIndex)
        {
            if (m_State == null || !m_State.IsFilled || m_State.TimeStopTried ||
                m_Params.MaxHoldBars <= 0)
            {
                return;
            }

            int entryBar = m_State.EntryBarIndex >= 0
                ? m_State.EntryBarIndex
                : m_LastClosedBar;
            if (closedBarIndex - entryBar < m_Params.MaxHoldBars)
                return;

            m_State.TimeStopTried = true;
            IPosition position = m_TradeManager.GetPositions()
                .FirstOrDefault(a => a.Id == m_State.PositionId);
            if (position == null)
            {
                m_State = null;
                m_Finder.IsInSetup = false;
                return;
            }

            Logger.Write(
                $"{m_Finder.Id}: continuation time stop at bar {closedBarIndex} " +
                $"(entry bar {entryBar}, max {m_Params.MaxHoldBars})");
            m_TradeManager.Close(position);
        }

        private void PlaceOrModify(int closedBarIndex)
        {
            if (m_State == null)
                return;

            double height = Math.Abs(m_Extreme - m_P0);
            if (height <= 1e-12)
            {
                CancelPending();
                return;
            }

            int sgn = m_IsUp ? 1 : -1;
            double entry = m_Extreme - sgn * m_Params.EnterRatio * height;
            double stop = m_P0 - sgn * m_Params.StopBuffer * height;
            double target = m_Params.TargetKind switch
            {
                DiagonalContinuationTargetKind.DIAGONAL_START =>
                    m_P0 + sgn * m_Params.TargetRatio * height,
                DiagonalContinuationTargetKind.DIAGONAL_END =>
                    m_Extreme + sgn * m_Params.TargetRatio * height,
                _ => entry + sgn * m_Params.TargetRatio * height
            };

            double risk = Math.Abs(entry - stop);
            if (risk <= 1e-12 || sgn * (target - entry) <= 0)
            {
                CancelPending();
                return;
            }

            double bid = m_TradeManager.GetBid(m_Symbol);
            bool isLimitSide = m_IsUp ? entry <= bid : entry > bid;
            if (!isLimitSide)
            {
                // The market has moved through the level: the order would be a market
                // order now. Re-check on the next bar instead of chasing it.
                return;
            }

            double rangePips = Math.Abs(target - stop) / m_Symbol.PipSize;
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
                    $"{m_Finder.Id}: continuation volume {volumeInLots:F2} lots is out of range");
                CancelPending();
                return;
            }

            if (m_State.PendingOrderId != 0)
            {
                IPosition existing = m_TradeManager.GetPendingOrders(m_Symbol)
                    .FirstOrDefault(a => a.Id == m_State.PendingOrderId);
                if (existing != null)
                {
                    OrderResult modified = m_TradeManager.ModifyPendingOrder(
                        existing, entry, stop, target);
                    if (modified?.IsSuccessful == true)
                    {
                        m_State.Entry = entry;
                        m_State.StopLoss = stop;
                        m_State.TakeProfit = target;
                        return;
                    }

                    Logger.Write($"{m_Finder.Id}: continuation modify failed, re-placing");
                    CancelPending();
                    // CancelPending clears the state; restart from the stored signal context.
                    RestoreStateAfterCancel();
                }
            }

            var entryPoint = new BarPoint(
                entry, m_SignalTime, m_Finder.TimeFrame, m_SignalBar);
            string comment = Helper.GetPositionId(m_Finder.Id, entryPoint, "C");

            OrderResult order = m_TradeManager.OpenOrder(
                m_IsUp, m_Symbol, volume, m_BotName, stop, target, comment, entry);
            if (order?.IsSuccessful == true)
            {
                m_State.Comment = comment;
                m_State.PendingOrderId = order.Position.Id;
                m_State.Entry = entry;
                m_State.StopLoss = stop;
                m_State.TakeProfit = target;
                Logger.Write(
                    $"{m_Finder.Id}: continuation pending at {entry:F5} " +
                    $"(SL {stop:F5}, TP {target:F5}, R:R {Math.Abs(target - entry) / risk:F2})");
            }
            else
            {
                Logger.Write($"{m_Finder.Id}: continuation pending order failed");
            }
        }

        private void RestoreStateAfterCancel()
        {
            // CancelPending() is also used when the setup is dead; this helper is only
            // called from the modify-failure path, where the setup is still alive.
            if (m_State != null)
                return;

            var entryPoint = new BarPoint(
                m_Extreme, m_SignalTime, m_Finder.TimeFrame, m_SignalBar);
            m_State = new PendingState
            {
                Comment = Helper.GetPositionId(m_Finder.Id, entryPoint, "C")
            };
            m_Finder.IsInSetup = true;
        }

        private void CancelPending()
        {
            if (m_State == null)
                return;

            if (!m_State.IsFilled && m_State.PendingOrderId != 0)
                m_TradeManager.CancelOrder(m_State.Comment);

            m_State = null;
            m_Finder.IsInSetup = false;
        }
    }
}
