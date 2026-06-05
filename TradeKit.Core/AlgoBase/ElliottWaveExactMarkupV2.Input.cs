using TradeKit.Core.Common;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Second-version Elliott-wave markup engine (see <c>EW_MARKUP_v2.md</c>).
    /// <para>
    /// This partial implements <b>step 1 of §19</b> — the input minimal-period zigzag:
    /// it builds the zigzag from the bars provider, enforces the input invariants
    /// I1–I3 (strict alternation, extremum-on-boundary, minimal duration) and exposes
    /// the resulting pivots and segments. Higher-level tree markup is added in later
    /// partials.
    /// </para>
    /// </summary>
    public partial class ElliottWaveExactMarkupV2
    {
        /// <summary>
        /// A single zigzag segment between two adjacent pivots — the minimal unit of
        /// analysis. By default its type is <c>SIMPLE_IMPULSE</c> (see §2, §10).
        /// </summary>
        public sealed record Segment
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Segment"/> record.
            /// </summary>
            /// <param name="start">The pivot the segment starts at.</param>
            /// <param name="end">The pivot the segment ends at.</param>
            public Segment(BarPoint start, BarPoint end)
            {
                Start = start;
                End = end;
            }

            /// <summary>Gets the pivot the segment starts at.</summary>
            public BarPoint Start { get; }

            /// <summary>Gets the pivot the segment ends at.</summary>
            public BarPoint End { get; }

            /// <summary>Gets a value indicating whether the segment goes upward.</summary>
            public bool IsUp => End.Value > Start.Value;

            /// <summary>Gets the absolute price amplitude of the segment.</summary>
            public double Length => Math.Abs(End.Value - Start.Value);

            /// <summary>Gets the number of bars the segment spans (inclusive).</summary>
            public int BarsCount => Math.Abs(End.BarIndex - Start.BarIndex) + 1;
        }

        private readonly IBarsProvider m_BarsProvider;

        /// <summary>
        /// Gets the source bars provider the markup is built on.
        /// </summary>
        public IBarsProvider BarsProvider => m_BarsProvider;

        /// <summary>
        /// Gets the deviation percent used to build the minimal-period zigzag (§4).
        /// </summary>
        public double DeviationPercent { get; }

        /// <summary>
        /// Gets the ordered zigzag pivots (strictly alternating min/max — invariant I1).
        /// </summary>
        public IReadOnlyList<BarPoint> Pivots { get; }

        /// <summary>
        /// Gets the ordered segments between adjacent pivots.
        /// </summary>
        public IReadOnlyList<Segment> Segments { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ElliottWaveExactMarkupV2"/> class
        /// and builds the input minimal-period zigzag.
        /// </summary>
        /// <param name="barsProvider">The source bars provider with loaded candles.</param>
        /// <param name="startIndex">Start bar index of the range to analyze.</param>
        /// <param name="endIndex">End bar index of the range to analyze.</param>
        /// <param name="deviationPercent">
        /// Optional deviation percent for the zigzag. When <c>null</c> the value is taken
        /// from <see cref="DeviationOptimizer.FindOptimalDeviation"/> (the saturation
        /// point — see §4).
        /// </param>
        /// <param name="isUpDirection">Initial zigzag direction.</param>
        public ElliottWaveExactMarkupV2(
            IBarsProvider barsProvider,
            int startIndex,
            int endIndex,
            double? deviationPercent = null,
            bool isUpDirection = false)
        {
            m_BarsProvider = barsProvider ?? throw new ArgumentNullException(nameof(barsProvider));
            if (endIndex <= startIndex)
                throw new ArgumentException(
                    "endIndex must be greater than startIndex.", nameof(endIndex));

            DeviationPercent = deviationPercent
                ?? new DeviationOptimizer(barsProvider, startIndex, endIndex, isUpDirection)
                    .FindOptimalDeviation();

            List<BarPoint> pivots = BuildZigzag(
                barsProvider, startIndex, endIndex, DeviationPercent, isUpDirection);

            Pivots = pivots;
            Segments = BuildSegments(pivots);
        }

        /// <summary>
        /// Builds the minimal-period zigzag and aligns every pivot to the true OHLC
        /// extremum so the result honours invariant I2 (no intra-segment overshoot).
        /// </summary>
        private static List<BarPoint> BuildZigzag(
            IBarsProvider provider,
            int startIndex,
            int endIndex,
            double deviationPercent,
            bool isUpDirection)
        {
            var finder = new SimpleExtremumFinder(deviationPercent, provider, isUpDirection);
            finder.Calculate(startIndex, endIndex);
            List<BarPoint> pivots = finder.ToExtremaList();

            // Enforce invariant I2 (extremum = boundary): align every intermediate pivot
            // to the true OHLC extreme on the END side and the START side. A single pass
            // of each helper can leave a residual breach because the two passes move a
            // pivot toward opposite-side extrema, so iterate to a fixpoint (bounded).
            for (int pass = 0; pass < MAX_CORRIDOR_PASSES; pass++)
            {
                List<BarPoint> next = ExtremumFinderBase.EndFixCorridors(pivots, provider);
                next = ExtremumFinderBase.RefineToCorridors(next, provider);

                bool stable = SamePivots(next, pivots);
                pivots = next;
                if (stable)
                    break;
            }

            return pivots;
        }

        /// <summary>Maximum number of corridor-alignment passes used to enforce I2.</summary>
        private const int MAX_CORRIDOR_PASSES = 16;

        /// <summary>
        /// Returns <c>true</c> when two pivot lists are identical in bar index and value.
        /// </summary>
        private static bool SamePivots(IReadOnlyList<BarPoint> a, IReadOnlyList<BarPoint> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].BarIndex != b[i].BarIndex ||
                    Math.Abs(a[i].Value - b[i].Value) > 1e-12)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds the contiguous segment list from the ordered pivots.
        /// </summary>
        private static List<Segment> BuildSegments(IReadOnlyList<BarPoint> pivots)
        {
            var segments = new List<Segment>(Math.Max(0, pivots.Count - 1));
            for (int i = 0; i < pivots.Count - 1; i++)
                segments.Add(new Segment(pivots[i], pivots[i + 1]));
            return segments;
        }
    }
}
