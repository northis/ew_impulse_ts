namespace TradeKit.Core.Common
{
    /// <summary>
    /// Protects the stop of an <b>open</b> setup across a thin-market window (DIAGONAL.md §4.7).
    /// <para>
    /// The stop of a setup is a level on the chart, but the position behind it is closed by the
    /// <i>other</i> side of the spread: a short is stopped out by the ask. Around a session break
    /// the book empties and the spread widens many times over, so a stop that is perfectly safe
    /// in a thick market is swept by the spread alone — and the price then goes to the target.
    /// The guard pulls the stop away from the entry for the duration of the window and returns it
    /// once the market is thick again.
    /// </para>
    /// <para>
    /// The buffer is <c>max(SpreadBufferRatio × spread, RiskBufferRatio × |entry − SL|)</c>: the
    /// first term is what the widening is really about, the second is the scale-free fallback for
    /// a context with no live quotes (an archive replay, an indicator). While the window lasts the
    /// buffer only grows — the widest spread seen is the one that can hurt — and it is dropped to
    /// zero when the window ends.
    /// </para>
    /// </summary>
    public sealed class ThinMarketStopGuard
    {
        /// <summary>Default stop buffer as a multiple of the current spread.</summary>
        public const double DEFAULT_SPREAD_BUFFER_RATIO = 2.0;

        /// <summary>Default stop buffer as a share of the planned risk.</summary>
        public const double DEFAULT_RISK_BUFFER_RATIO = 0.25;

        /// <summary>
        /// Default daily rollover break start (UTC). Broker-specific — set it per instrument:
        /// a GMT+2/+3 server pauses around 22:00 UTC in winter and around 21:00 UTC in summer.
        /// </summary>
        public const string DEFAULT_DAILY_BREAK_START = "21:00";

        /// <summary>Default daily rollover break end (UTC).</summary>
        public const string DEFAULT_DAILY_BREAK_END = "22:00";

        private readonly ITradeViewManager m_ViewManager;
        private readonly ISymbol m_Symbol;
        private double m_TickSpread;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThinMarketStopGuard"/> class.
        /// </summary>
        /// <param name="viewManager">The read-only trade manager the live spread comes from; <c>null</c> — ticks only.</param>
        /// <param name="symbol">The instrument traded.</param>
        /// <param name="calendar">The thin-window schedule.</param>
        /// <param name="spreadBufferRatio">The stop buffer as a multiple of the spread.</param>
        /// <param name="riskBufferRatio">The stop buffer as a share of the planned risk.</param>
        public ThinMarketStopGuard(
            ITradeViewManager viewManager,
            ISymbol symbol,
            ThinMarketCalendar calendar,
            double spreadBufferRatio = DEFAULT_SPREAD_BUFFER_RATIO,
            double riskBufferRatio = DEFAULT_RISK_BUFFER_RATIO)
        {
            m_ViewManager = viewManager;
            m_Symbol = symbol;
            Calendar = calendar;
            SpreadBufferRatio = Math.Max(0, spreadBufferRatio);
            RiskBufferRatio = Math.Max(0, riskBufferRatio);
        }

        /// <summary>Gets the schedule the guard follows.</summary>
        public ThinMarketCalendar Calendar { get; }

        /// <summary>Gets the stop buffer as a multiple of the current spread.</summary>
        public double SpreadBufferRatio { get; }

        /// <summary>Gets the stop buffer as a share of the planned risk.</summary>
        public double RiskBufferRatio { get; }

        /// <summary>Gets a value indicating whether the guard can do anything at all.</summary>
        public bool IsEnabled => Calendar != null && Calendar.IsEnabled;

        /// <summary>
        /// Gets a value indicating whether the stop is currently pulled away from the entry.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Gets the current stop buffer in price units; <c>0</c> when the guard is inactive.
        /// </summary>
        public double Buffer { get; private set; }

        /// <summary>
        /// Feeds the spread of a live tick. The guard takes the widest of this and the spread
        /// the trade manager reports, so a context without ticks still works; the widest value
        /// over the window is kept by <see cref="Buffer"/>, which never shrinks while active.
        /// </summary>
        /// <param name="spread">The ask − bid distance in price units.</param>
        public void RecordSpread(double spread)
        {
            if (spread > 0)
                m_TickSpread = spread;
        }

        /// <summary>
        /// Advances the guard to <paramref name="time"/>. Returns <c>true</c> when
        /// <see cref="Buffer"/> changed and the consumer has to move the stop of the position.
        /// </summary>
        /// <param name="time">The current moment (UTC) — a bar close time or a tick time.</param>
        /// <param name="risk">The planned risk of the open setup, <c>|entry − SL|</c>.</param>
        public bool Update(DateTime time, double risk)
        {
            if (!IsEnabled)
                return false;

            if (!Calendar.IsThinMarket(time))
            {
                if (!IsActive)
                    return false;

                Reset();
                return true;
            }

            double previous = Buffer;
            bool wasActive = IsActive;
            double target = Math.Max(
                SpreadBufferRatio * CurrentSpread(),
                RiskBufferRatio * Math.Abs(risk));

            IsActive = true;
            Buffer = Math.Max(Buffer, target);
            return !wasActive || Buffer > previous;
        }

        /// <summary>
        /// Drops the guard state — the setup it protected has been closed.
        /// </summary>
        public void Reset()
        {
            IsActive = false;
            Buffer = 0;
            m_TickSpread = 0;
        }

        private double CurrentSpread()
        {
            double spread = m_TickSpread;
            if (m_ViewManager == null || m_Symbol == null)
                return spread;

            double ask = m_ViewManager.GetAsk(m_Symbol);
            double bid = m_ViewManager.GetBid(m_Symbol);
            return ask > bid && ask - bid > spread ? ask - bid : spread;
        }
    }
}
