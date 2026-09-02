using System;
using System.Collections.Generic;
using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// A confirmed thin-market segment (DIAGONAL.md §4.5): a run of
    /// <see cref="MinThinRunBars"/>+ consecutive bars whose activity metric fell below
    /// <see cref="ThinFactor"/> of the rolling median. Inside such a segment the quotes are
    /// mostly random — models may span it, but no sub-wave may END there.
    /// </summary>
    /// <param name="StartBar">Index of the segment's first bar.</param>
    /// <param name="EndBar">Index of the segment's last bar (inclusive, grows while the run continues).</param>
    public readonly record struct ThinMarketSegment(int StartBar, int EndBar);

    /// <summary>
    /// Causal detector of <b>thin-market segments</b> — stretches where liquidity is low and
    /// prints are mostly random (DIAGONAL.md §4.5), typically the tail after the Asian session
    /// open. A bar is <i>dead</i> when its activity metric falls below
    /// <c>ThinFactor · median(metric)</c> of the preceding <see cref="BaselineBars"/> bars;
    /// a run of <see cref="MinThinRunBars"/> dead bars forms a segment.
    /// <para>
    /// The metric is the bar's <b>tick volume</b> when the provider supplies it (the rolling
    /// median of volumes is positive), and the <b>high-low range</b> as a fallback otherwise —
    /// so archives saved without the volume column keep working, on the observation that a
    /// thin market collapses the traded range together with the activity.
    /// </para>
    /// <para>
    /// Unlike the isolated-print filter no retrace confirmation is needed: thinness of the
    /// bars that already closed is directly observable, so a segment appears as soon as its
    /// run reaches <see cref="MinThinRunBars"/> dead bars and grows while the run continues.
    /// Membership is reported through <see cref="IsInThinSegment"/>; the zigzag stays
    /// untouched — consumers refuse to build wave ends on the flagged bars.
    /// </para>
    /// </summary>
    public sealed class ThinMarketFilter
    {
        /// <summary>Default dead-bar factor: a bar is dead below factor × rolling median.</summary>
        public const double DEFAULT_THIN_FACTOR = 0.3;

        /// <summary>Default rolling-median window, in bars (≈ a day of m5 bars).</summary>
        public const int DEFAULT_BASELINE_BARS = 288;

        /// <summary>Default minimum run of dead bars that forms a thin segment.</summary>
        public const int DEFAULT_MIN_THIN_RUN_BARS = 12;

        /// <summary>Minimum bars in the window before the median is trusted.</summary>
        private const int MIN_BASELINE_BARS = 20;

        private readonly IBarsProvider m_BarsProvider;
        private readonly List<ThinMarketSegment> m_Segments = new();
        private readonly List<double> m_Volumes = new();
        private readonly List<double> m_Ranges = new();
        private int m_LastProcessedBar = -1;
        private int m_RunStart = -1;
        private int m_RunEnd = -1;
        private bool m_LastBarVolumeMode;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThinMarketFilter"/> class.
        /// </summary>
        /// <param name="barsProvider">The source bars provider (same instance the consumer uses).</param>
        /// <param name="thinFactor">Dead-bar threshold as a share of the rolling median; <c>≤ 0</c> disables detection.</param>
        /// <param name="baselineBars">The rolling-median window, in bars.</param>
        /// <param name="minThinRunBars">Minimum run of dead bars forming a segment; <c>≤ 0</c> disables detection.</param>
        public ThinMarketFilter(
            IBarsProvider barsProvider,
            double thinFactor = DEFAULT_THIN_FACTOR,
            int baselineBars = DEFAULT_BASELINE_BARS,
            int minThinRunBars = DEFAULT_MIN_THIN_RUN_BARS)
        {
            m_BarsProvider = barsProvider;
            ThinFactor = thinFactor;
            BaselineBars = Math.Max(MIN_BASELINE_BARS, baselineBars);
            MinThinRunBars = minThinRunBars;
        }

        /// <summary>Gets the dead-bar threshold, as a share of the rolling median.</summary>
        public double ThinFactor { get; }

        /// <summary>Gets the rolling-median window, in bars.</summary>
        public int BaselineBars { get; }

        /// <summary>Gets the minimum run of dead bars that forms a thin segment.</summary>
        public int MinThinRunBars { get; }

        /// <summary>Gets the detected segments in chronological order.</summary>
        public IReadOnlyList<ThinMarketSegment> Segments => m_Segments;

        /// <summary>
        /// Gets a value indicating whether the last processed bar measured activity by tick
        /// volume (<c>true</c>) or by the high-low range proxy (<c>false</c> — the provider
        /// reports no volume).
        /// </summary>
        public bool UsesVolume => m_LastBarVolumeMode;

        /// <summary>
        /// Gets a value indicating whether detection is enabled.
        /// <c>ThinFactor ≤ 0</c> or <c>MinThinRunBars ≤ 0</c> switches it off
        /// (<see cref="OnCalculate"/> becomes a no-op, <see cref="IsInThinSegment"/> — false).
        /// </summary>
        public bool IsEnabled => ThinFactor > 0 && MinThinRunBars > 0;

        /// <summary>
        /// Advances the filter through the closed bar with
        /// <paramref name="openDateTime"/>. Must be called once per bar, in order — the same
        /// calling pattern as the zigzag's <c>OnCalculate</c>.
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

            while (m_Volumes.Count <= index)
                EvaluateBar(m_Volumes.Count);
        }

        /// <summary>
        /// Determines whether the bar lies inside a thin-market segment: a wave end there is
        /// a noise extremum and the model must not rely on it.
        /// </summary>
        /// <param name="barIndex">The bar index to test.</param>
        public bool IsInThinSegment(int barIndex)
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
        /// Classifies one bar against the rolling median of the preceding window, volume
        /// metric when available, range proxy otherwise, and extends/closes the thin run.
        /// </summary>
        private void EvaluateBar(int bar)
        {
            m_Volumes.Add(m_BarsProvider.GetTickVolume(bar));
            m_Ranges.Add(m_BarsProvider.GetHighPrice(bar) - m_BarsProvider.GetLowPrice(bar));

            int from = Math.Max(0, bar - BaselineBars);
            int count = bar - from;
            if (count < MIN_BASELINE_BARS)
                return;

            bool volumeMode = MedianOf(m_Volumes, from, count) > 0;
            m_LastBarVolumeMode = volumeMode;
            List<double> metric = volumeMode ? m_Volumes : m_Ranges;
            double median = MedianOf(metric, from, count);
            bool dead = median > 0 && metric[bar] < ThinFactor * median;

            if (dead)
            {
                if (m_RunStart < 0)
                    m_RunStart = bar;
                m_RunEnd = bar;

                if (m_RunEnd - m_RunStart + 1 >= MinThinRunBars)
                {
                    // Open or grow the segment; the run may continue on the next bars.
                    int last = m_Segments.Count - 1;
                    if (last >= 0 && m_Segments[last].EndBar == m_RunEnd - 1)
                        m_Segments[last] = m_Segments[last] with { EndBar = m_RunEnd };
                    else
                        m_Segments.Add(new ThinMarketSegment(m_RunStart, m_RunEnd));
                }
            }
            else
            {
                m_RunStart = -1;
                m_RunEnd = -1;
            }
        }

        private static double MedianOf(List<double> values, int from, int count)
        {
            var window = new double[count];
            for (int i = 0; i < count; i++)
                window[i] = values[from + i];

            Array.Sort(window);
            return window[count / 2];
        }
    }
}
