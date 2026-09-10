using System.Globalization;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// A period the instrument does not trade: the gap between two trading sessions
    /// (a weekend, a holiday, an intraday break) or the daily rollover break.
    /// </summary>
    /// <param name="Start">The moment the trading stops.</param>
    /// <param name="End">The moment the trading resumes.</param>
    public readonly record struct TradingBreak(DateTime Start, DateTime End);

    /// <summary>
    /// Answers whether a moment belongs to a <b>thin-market window</b> — the stretch around a
    /// trading-session break where the order book is empty and the spread widens far beyond
    /// its normal value (DIAGONAL.md §4.7).
    /// <para>
    /// Unlike <see cref="Indicators.ThinMarketFilter"/> nothing is measured from the bars: the
    /// window is <b>scheduled</b>, so it is known <i>before</i> it starts — which is the whole
    /// point, a position has to be protected ahead of the break, not after it. Two sources of
    /// the schedule are combined:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ITradingHours"/> sessions of the instrument (what the broker reports): every
    /// gap between two sessions is a break — the weekend for a continuous FX schedule, an
    /// intraday pause for an exchange-traded one;
    /// </description></item>
    /// <item><description>
    /// an explicit daily break (<c>HH:mm</c> in UTC), because a broker that quotes FX without a
    /// pause still thins out at the rollover, and <see cref="ITradingHours"/> says nothing about
    /// it.
    /// </description></item>
    /// </list>
    /// <para>
    /// A break is widened by <see cref="PreCloseMargin"/> before it and by
    /// <see cref="PostOpenMargin"/> after it: the market goes thin before the official close and
    /// stays thin for the first minutes after the open.
    /// </para>
    /// </summary>
    public sealed class ThinMarketCalendar
    {
        /// <summary>Default margin before a break, in minutes.</summary>
        public const int DEFAULT_PRE_CLOSE_MINUTES = 15;

        /// <summary>Default margin after the trading resumes, in minutes.</summary>
        public const int DEFAULT_POST_OPEN_MINUTES = 15;

        /// <summary>How far (in days) the schedule is scanned around the requested moment.</summary>
        private const int SCAN_DAYS = 2;

        private readonly ITradingHours[] m_Sessions;
        private readonly TimeSpan? m_DailyBreakStart;
        private readonly TimeSpan? m_DailyBreakEnd;

        /// <summary>Cached answer, valid while the moment stays inside [CachedFrom, CachedTo).</summary>
        private bool m_CachedValue;
        private DateTime m_CachedFrom = DateTime.MaxValue;
        private DateTime m_CachedTo = DateTime.MinValue;

        /// <summary>The moment the cache was computed at — a rewind invalidates it.</summary>
        private DateTime m_CachedAt = DateTime.MaxValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThinMarketCalendar"/> class.
        /// </summary>
        /// <param name="sessions">The trading sessions of the instrument; empty — the schedule is the daily break alone.</param>
        /// <param name="preCloseMargin">How long before a break the market is already considered thin.</param>
        /// <param name="postOpenMargin">How long after the trading resumes the market is still considered thin.</param>
        /// <param name="dailyBreakStartUtc">The daily break start (UTC time of day); <c>null</c> — there is none.</param>
        /// <param name="dailyBreakEndUtc">The daily break end (UTC time of day); <c>null</c> — the break is the <paramref name="dailyBreakStartUtc"/> instant.</param>
        public ThinMarketCalendar(
            ITradingHours[] sessions,
            TimeSpan preCloseMargin,
            TimeSpan postOpenMargin,
            TimeSpan? dailyBreakStartUtc = null,
            TimeSpan? dailyBreakEndUtc = null)
        {
            m_Sessions = sessions ?? Array.Empty<ITradingHours>();
            PreCloseMargin = preCloseMargin < TimeSpan.Zero ? TimeSpan.Zero : preCloseMargin;
            PostOpenMargin = postOpenMargin < TimeSpan.Zero ? TimeSpan.Zero : postOpenMargin;
            m_DailyBreakStart = dailyBreakStartUtc;
            m_DailyBreakEnd = dailyBreakEndUtc;
            IsEnabled = m_Sessions.Length > 0 || m_DailyBreakStart.HasValue;
        }

        /// <summary>Gets the margin before a break that is still considered thin.</summary>
        public TimeSpan PreCloseMargin { get; }

        /// <summary>Gets the margin after the trading resumes that is still considered thin.</summary>
        public TimeSpan PostOpenMargin { get; }

        /// <summary>
        /// Gets a value indicating whether the calendar knows any schedule at all: without
        /// sessions and without a daily break there is nothing to protect against.
        /// </summary>
        public bool IsEnabled { get; }

        /// <summary>
        /// Determines whether <paramref name="time"/> (UTC) is inside a thin-market window.
        /// </summary>
        /// <param name="time">The moment to test.</param>
        public bool IsThinMarket(DateTime time)
        {
            if (!IsEnabled)
                return false;

            if (time >= m_CachedAt && time >= m_CachedFrom && time < m_CachedTo)
                return m_CachedValue;

            return Recache(time);
        }

        /// <summary>
        /// Parses an <c>HH:mm</c> UTC time of day. An empty or malformed value means "none".
        /// </summary>
        /// <param name="value">The text to parse.</param>
        /// <returns>The parsed time of day, or <c>null</c>.</returns>
        public static TimeSpan? ParseTimeOfDay(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!TimeSpan.TryParse(value.Trim(), CultureInfo.InvariantCulture, out TimeSpan parsed))
                return null;

            return parsed.Duration() < TimeSpan.FromDays(1) ? parsed : null;
        }

        /// <summary>
        /// Recomputes the cached answer: the window <paramref name="time"/> falls into, or the
        /// nearest upcoming one when it is outside any window.
        /// </summary>
        private bool Recache(DateTime time)
        {
            m_CachedAt = time;
            DateTime? nextWindowStart = null;
            foreach (TradingBreak brk in BreaksAround(time))
            {
                DateTime windowStart = brk.Start - PreCloseMargin;
                DateTime windowEnd = brk.End + PostOpenMargin;
                if (windowEnd <= windowStart)
                    continue;

                if (time >= windowStart && time < windowEnd)
                {
                    m_CachedValue = true;
                    m_CachedFrom = windowStart;
                    m_CachedTo = windowEnd;
                    return true;
                }

                if (windowStart > time &&
                    (nextWindowStart == null || windowStart < nextWindowStart.Value))
                {
                    nextWindowStart = windowStart;
                }
            }

            m_CachedValue = false;
            m_CachedFrom = DateTime.MinValue;
            // Nothing upcoming on the scanned horizon — re-check in a day rather than on every tick.
            m_CachedTo = nextWindowStart ?? time.Date.AddDays(SCAN_DAYS);
            return false;
        }

        /// <summary>
        /// Enumerates the trading breaks around <paramref name="time"/>: the daily rollover
        /// break (when configured) and the gaps between the sessions of the instrument.
        /// </summary>
        private IEnumerable<TradingBreak> BreaksAround(DateTime time)
        {
            if (m_DailyBreakStart.HasValue)
            {
                TimeSpan start = m_DailyBreakStart.Value;
                TimeSpan end = m_DailyBreakEnd ?? start;
                for (int day = -SCAN_DAYS; day <= 1; day++)
                {
                    DateTime breakStart = time.Date.AddDays(day).Add(start);
                    DateTime breakEnd = time.Date.AddDays(day).Add(end);
                    // A break that ends before it starts crosses midnight; an equal pair is a
                    // single instant (the margins around it are what makes the window).
                    if (breakEnd < breakStart)
                        breakEnd = breakEnd.AddDays(1);
                    yield return new TradingBreak(breakStart, breakEnd);
                }
            }

            if (m_Sessions.Length == 0)
                yield break;

            foreach (TradingBreak gap in SessionGaps(time))
                yield return gap;
        }

        /// <summary>
        /// Collects the session intervals of the weeks around <paramref name="time"/>, merges
        /// the overlapping ones and returns the gaps between them — the periods the instrument
        /// does not trade.
        /// </summary>
        private IEnumerable<TradingBreak> SessionGaps(DateTime time)
        {
            // The same week anchor BaseAlgoRobot.HasTradeBreakInside uses: Sunday midnight.
            DateTime weekStart = time.Date.AddDays(-(int)time.DayOfWeek);
            var sessions = new List<TradingBreak>();
            for (int week = -1; week <= 1; week++)
            {
                foreach (ITradingHours hours in m_Sessions)
                {
                    DateTime start = weekStart
                        .AddDays(7 * week)
                        .AddDays((int)hours.StartDay)
                        .Add(hours.StartTime);
                    DateTime end = weekStart
                        .AddDays(7 * week)
                        .AddDays((int)hours.EndDay)
                        .Add(hours.EndTime);
                    if (end <= start)
                        end = end.AddDays(7);
                    sessions.Add(new TradingBreak(start, end));
                }
            }

            sessions.Sort((a, b) => a.Start.CompareTo(b.Start));

            TradingBreak current = sessions[0];
            for (int i = 1; i < sessions.Count; i++)
            {
                TradingBreak next = sessions[i];
                if (next.Start > current.End)
                {
                    yield return new TradingBreak(current.End, next.Start);
                    current = next;
                }
                else if (next.End > current.End)
                {
                    current = current with { End = next.End };
                }
            }
        }
    }
}
