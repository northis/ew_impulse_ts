namespace TradeKit.Core.Common
{
    /// <summary>
    /// Settings of the thin-market stop guard (DIAGONAL.md §4.7): how the schedule of the
    /// thin windows is built and how far the stop of an open setup is pulled away from the
    /// entry while such a window lasts.
    /// </summary>
    /// <param name="Enabled">
    /// The master switch. Off — nothing changes: signals are emitted next to the session
    /// breaks as before and the stop of an open setup is never moved.
    /// </param>
    /// <param name="PreCloseMinutes">How many minutes before the trading stops the market is already thin.</param>
    /// <param name="PostOpenMinutes">How many minutes after the trading resumes the market is still thin.</param>
    /// <param name="SpreadBufferRatio">The stop buffer as a multiple of the current spread.</param>
    /// <param name="RiskBufferRatio">
    /// The stop buffer as a share of the planned risk <c>|entry − SL|</c> — the scale-free
    /// fallback for when the spread is unknown (an archive replay, an indicator, a bar with no
    /// ticks between the sessions).
    /// </param>
    /// <param name="DailyBreakStartUtc">
    /// The daily rollover break start, <c>HH:mm</c> in UTC; empty — the instrument has none.
    /// </param>
    /// <param name="DailyBreakEndUtc">
    /// The daily rollover break end, <c>HH:mm</c> in UTC; empty — the break is a single instant.
    /// </param>
    public record ThinMarketGuardParams(
        bool Enabled,
        int PreCloseMinutes = ThinMarketCalendar.DEFAULT_PRE_CLOSE_MINUTES,
        int PostOpenMinutes = ThinMarketCalendar.DEFAULT_POST_OPEN_MINUTES,
        double SpreadBufferRatio = ThinMarketStopGuard.DEFAULT_SPREAD_BUFFER_RATIO,
        double RiskBufferRatio = ThinMarketStopGuard.DEFAULT_RISK_BUFFER_RATIO,
        string DailyBreakStartUtc = ThinMarketStopGuard.DEFAULT_DAILY_BREAK_START,
        string DailyBreakEndUtc = ThinMarketStopGuard.DEFAULT_DAILY_BREAK_END)
    {
    }
}
