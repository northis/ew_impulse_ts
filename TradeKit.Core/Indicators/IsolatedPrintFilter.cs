using System;
using System.Collections.Generic;
using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// A confirmed isolated-print segment (DIAGONAL.md §4.4): a run of consecutive bars
    /// whose price range overlaps neither the previous bar's range nor the next bar's
    /// range, displaced far enough from them and retraced quickly afterwards. Such bars
    /// are excluded from wave construction — a model built on them is built on a price
    /// the market never actually traded.
    /// </summary>
    /// <param name="StartBar">Index of the segment's first bar.</param>
    /// <param name="EndBar">Index of the segment's last bar (inclusive).</param>
    /// <param name="Displacement">
    /// The gap depth D — the distance from the segment's extreme to the nearest traded
    /// boundary of the neighboring bars.
    /// </param>
    /// <param name="IsDown">True for a down-print (the segment hangs below both neighbors).</param>
    public readonly record struct IsolatedPrintSegment(
        int StartBar, int EndBar, double Displacement, bool IsDown);

    /// <summary>
    /// Causal detector of <b>isolated prints</b> — spikes gapped away on both sides
    /// (DIAGONAL.md §4.4). A run of <c>1..MaxSpikeBars</c> bars is confirmed as a print
    /// when (1) its price range is disjoint from the ranges of both neighboring bars,
    /// (2) the displacement is significant against the local volatility (the median range
    /// of the bars before the segment), and (3) price retraces at least
    /// <see cref="MinRetraceShare"/> of the displacement within
    /// <see cref="RetraceWindowBars"/> bars after the segment.
    /// <para>
    /// A segment is confirmed only when its retrace window has fully closed, i.e. with a
    /// delay of <see cref="RetraceWindowBars"/> bars — the decision never looks ahead.
    /// Confirmed bars are reported through <see cref="IsExcluded"/>. The zigzag stays
    /// untouched; consumers simply stop reading the excluded bars.
    /// </para>
    /// </summary>
    public sealed class IsolatedPrintFilter
    {
        /// <summary>Default maximum length of a print segment, in bars.</summary>
        public const int DEFAULT_MAX_SPIKE_BARS = 3;

        /// <summary>Default minimum gap depth, in local median ranges.</summary>
        public const double DEFAULT_MIN_DISPLACEMENT_ATR = 4.0;

        /// <summary>Default retrace confirmation window, in bars — also the confirmation delay.</summary>
        public const int DEFAULT_RETRACE_WINDOW_BARS = 12;

        /// <summary>Default share of the displacement price must retrace within the window.</summary>
        public const double DEFAULT_MIN_RETRACE_SHARE = 0.8;

        /// <summary>How many bars before the segment feed the local volatility estimate.</summary>
        private const int VOLATILITY_LOOKBACK = 50;

        /// <summary>Minimum bars needed to trust the volatility estimate.</summary>
        private const int MIN_VOLATILITY_BARS = 20;

        private readonly IBarsProvider m_BarsProvider;
        private readonly List<IsolatedPrintSegment> m_Segments = new();
        private int m_LastProcessedBar = -1;
        private int m_LastConfirmedEnd;

        /// <summary>
        /// Initializes a new instance of the <see cref="IsolatedPrintFilter"/> class.
        /// </summary>
        /// <param name="barsProvider">The source bars provider (same instance the consumer uses).</param>
        /// <param name="maxSpikeBars">Maximum length of a print segment; <c>≤ 0</c> disables detection.</param>
        /// <param name="minDisplacementAtr">Minimum gap depth in local median ranges; <c>≤ 0</c> disables detection.</param>
        /// <param name="retraceWindowBars">The retrace confirmation window (and delay), in bars.</param>
        /// <param name="minRetraceShare">Share of the displacement price must retrace within the window.</param>
        public IsolatedPrintFilter(
            IBarsProvider barsProvider,
            int maxSpikeBars = DEFAULT_MAX_SPIKE_BARS,
            double minDisplacementAtr = DEFAULT_MIN_DISPLACEMENT_ATR,
            int retraceWindowBars = DEFAULT_RETRACE_WINDOW_BARS,
            double minRetraceShare = DEFAULT_MIN_RETRACE_SHARE)
        {
            m_BarsProvider = barsProvider;
            MaxSpikeBars = maxSpikeBars;
            MinDisplacementAtr = minDisplacementAtr;
            RetraceWindowBars = Math.Max(1, retraceWindowBars);
            MinRetraceShare = Math.Min(Math.Max(0, minRetraceShare), 1);
        }

        /// <summary>Gets the maximum number of consecutive bars a print may span.</summary>
        public int MaxSpikeBars { get; }

        /// <summary>Gets the minimum gap depth, expressed in local median ranges.</summary>
        public double MinDisplacementAtr { get; }

        /// <summary>Gets the retrace confirmation window in bars — also the confirmation delay.</summary>
        public int RetraceWindowBars { get; }

        /// <summary>Gets the share of the displacement price must retrace within the window.</summary>
        public double MinRetraceShare { get; }

        /// <summary>Gets the confirmed segments in chronological order.</summary>
        public IReadOnlyList<IsolatedPrintSegment> Segments => m_Segments;

        /// <summary>
        /// Gets a value indicating whether detection is enabled.
        /// <c>MaxSpikeBars ≤ 0</c> or <c>MinDisplacementAtr ≤ 0</c> switches it off
        /// (<see cref="OnCalculate"/> becomes a no-op, <see cref="IsExcluded"/> — false).
        /// </summary>
        public bool IsEnabled => MaxSpikeBars > 0 && MinDisplacementAtr > 0;

        /// <summary>
        /// Advances the filter through the closed bar with
        /// <paramref name="openDateTime"/>. Must be called once per bar, in order —
        /// the same calling pattern as the zigzag's <c>OnCalculate</c>.
        /// </summary>
        /// <param name="openDateTime">The open time of the just closed bar.</param>
        public void OnCalculate(DateTime openDateTime)
        {
            if (!IsEnabled)
                return;

            int index = m_BarsProvider.GetIndexByTime(openDateTime);
            if (index <= m_LastProcessedBar)
                return;
            m_LastProcessedBar = index;

            // A segment ending at `end` is confirmable only once the bar
            // end + RetraceWindowBars has closed. Process every end that has matured,
            // so skipped bars do not drop segments on the floor.
            int maxEnd = index - RetraceWindowBars;
            if (maxEnd < 1)
                return;

            for (int end = Math.Max(1, m_LastConfirmedEnd + 1); end <= maxEnd; end++)
                ConfirmSegmentsEndingAt(end, index);

            m_LastConfirmedEnd = maxEnd;
        }

        /// <summary>
        /// Determines whether the bar is inside a confirmed isolated-print segment and so
        /// must be excluded from the analysis.
        /// </summary>
        /// <param name="barIndex">The bar index to test.</param>
        public bool IsExcluded(int barIndex)
        {
            int lo = 0;
            int hi = m_Segments.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (barIndex < m_Segments[mid].StartBar)
                    hi = mid - 1;
                else if (barIndex > m_Segments[mid].EndBar)
                    lo = mid + 1;
                else
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Tries every <c>1..MaxSpikeBars</c>-long segment ending at
        /// <paramref name="end"/>, longest first so adjacent print bars merge into one
        /// segment. At most one segment can be confirmed per end — any other candidate
        /// shares bars with it.
        /// </summary>
        private void ConfirmSegmentsEndingAt(int end, int retraceUntil)
        {
            for (int start = Math.Max(1, end - MaxSpikeBars + 1); start <= end; start++)
            {
                IsolatedPrintSegment? segment = TryConfirm(start, end, retraceUntil);
                if (segment != null)
                {
                    m_Segments.Add(segment.Value);
                    return;
                }
            }
        }

        private IsolatedPrintSegment? TryConfirm(int start, int end, int retraceUntil)
        {
            // A segment overlapping an already confirmed one is part of it.
            if (OverlapsConfirmed(start, end))
                return null;

            double segLow = double.MaxValue;
            double segHigh = double.MinValue;
            for (int i = start; i <= end; i++)
            {
                segLow = Math.Min(segLow, m_BarsProvider.GetLowPrice(i));
                segHigh = Math.Max(segHigh, m_BarsProvider.GetHighPrice(i));
            }

            double prevLow = m_BarsProvider.GetLowPrice(start - 1);
            double prevHigh = m_BarsProvider.GetHighPrice(start - 1);
            double nextLow = m_BarsProvider.GetLowPrice(end + 1);
            double nextHigh = m_BarsProvider.GetHighPrice(end + 1);

            // Two-sided gap: the segment's range is disjoint from BOTH neighbors' ranges —
            // the scale-invariant signature of a price that was never traded.
            bool isDown = segHigh < prevLow && segHigh < nextLow;
            bool isUp = segLow > prevHigh && segLow > nextHigh;
            if (!isDown && !isUp)
                return null;

            // Displacement D: from the segment's extreme to the nearest traded level.
            double displacement = isDown
                ? Math.Min(prevLow, nextLow) - segLow
                : segHigh - Math.Max(prevHigh, nextHigh);

            double baseline = MedianRangeBefore(start);
            if (baseline <= 0 || displacement < MinDisplacementAtr * baseline)
                return null;

            // Fast retrace: within the window after the segment price gives back at least
            // MinRetraceShare of the displacement — separates a bad print from a genuine
            // flash crash that keeps living at the new levels.
            double target = isDown
                ? segHigh + MinRetraceShare * displacement
                : segLow - MinRetraceShare * displacement;

            for (int i = end + 1; i <= retraceUntil; i++)
            {
                bool retraced = isDown
                    ? m_BarsProvider.GetHighPrice(i) >= target
                    : m_BarsProvider.GetLowPrice(i) <= target;
                if (retraced)
                    return new IsolatedPrintSegment(start, end, displacement, isDown);
            }

            return null;
        }

        private bool OverlapsConfirmed(int start, int end)
        {
            // Segments are appended chronologically — walk from the tail.
            for (int i = m_Segments.Count - 1; i >= 0; i--)
            {
                if (m_Segments[i].EndBar < start)
                    break;
                if (m_Segments[i].StartBar <= end)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The median high-low range of up to <see cref="VOLATILITY_LOOKBACK"/> bars
        /// immediately before <paramref name="bar"/>; <c>0</c> when there is not enough
        /// history or the market is dead (nothing to scale against).
        /// </summary>
        private double MedianRangeBefore(int bar)
        {
            int from = Math.Max(0, bar - VOLATILITY_LOOKBACK);
            int count = bar - from;
            if (count < MIN_VOLATILITY_BARS)
                return 0;

            var ranges = new double[count];
            for (int i = 0; i < count; i++)
            {
                int j = from + i;
                ranges[i] = m_BarsProvider.GetHighPrice(j) - m_BarsProvider.GetLowPrice(j);
            }

            Array.Sort(ranges);
            return ranges[count / 2];
        }
    }
}
