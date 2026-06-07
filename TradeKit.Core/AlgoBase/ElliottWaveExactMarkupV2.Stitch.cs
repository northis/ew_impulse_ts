using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Step 9 of EW_MARKUP_v2.md §19 — continuity (§15.3 T-MK-1/T-MK-3). A single
    /// whole-history <see cref="Parse"/> is intractable on a fine zigzag (the §14 beam
    /// DP is combinatorial in the range length), so the full input is covered by a
    /// <b>windowed, causal, left-to-right greedy stitch</b>: at each cursor the longest
    /// rule-valid top-level pattern that fits inside a bounded segment window is
    /// committed, then the cursor advances to its end. Segments that no multi-wave model
    /// can cover are filled with atomic <see cref="ElliottModelType.SIMPLE_IMPULSE"/>
    /// leaves so the tiling is hole-free (T-MK-1: adjacent tiles join end-to-end).
    /// <para>
    /// Because every committed tile depends only on segments at or before its own end,
    /// appending bars on the right cannot change an already-committed tile on the left
    /// (T-MK-3 boundary stability) — only the still-forming region at the right edge may
    /// change, which is exactly what the §13 projection covers. The bounded window keeps
    /// each <see cref="ParseRange"/> call cheap regardless of total history length, so the
    /// per-symbol auto-deviation zigzag (§4) — fine on liquid pairs, coarser on others —
    /// can be marked up continuously.
    /// </para>
    /// </summary>
    public partial class ElliottWaveExactMarkupV2
    {
        /// <summary>
        /// Maximum number of zigzag segments a single stitched top-level tile may span.
        /// Bounds the per-cursor <see cref="ParseRange"/> cost (the §14 DP is combinatorial
        /// in the range length) so the stitch stays linear in the history length.
        /// </summary>
        public const int STITCH_MAX_WINDOW_SEGMENTS = 17;

        /// <summary>Smallest multi-wave top-level pattern (a zigzag spans three segments).</summary>
        public const int STITCH_MIN_PATTERN_SEGMENTS = 3;

        /// <summary>
        /// Marks up the whole input zigzag continuously (EW_MARKUP_v2 §15.3). Returns an
        /// ordered, gap-free sequence of top-level tiles covering every segment
        /// <c>[0..Segments.Count-1]</c>, together with the coverage metrics (§15.4) and
        /// the best forming-pattern projection at the right edge (§13).
        /// </summary>
        public ContinuousMarkupResult ParseContinuous()
        {
            int n = Segments.Count;
            var metrics = new MarkupSearchMetrics { RangeSegments = n };
            var tiles = new List<TreeNode>();

            if (n == 0)
            {
                metrics.Coverage = 0.0;
                metrics.GapCount = 0;
                return new ContinuousMarkupResult(tiles, metrics, null);
            }

            // One persistent memo for the whole stitch: overlapping windows share
            // sub-ranges, and an absolute (i, j, model) cell is identical regardless of
            // which cursor first computes it, so caching it across cursors is sound.
            m_Memo = new Dictionary<(int, int, ElliottModelType), IReadOnlyList<Candidate>>();
            m_Metrics = metrics;
            m_NodesCreated = 0;
            m_Aborted = false;
            m_DeadDepth = 0;
            m_DeadCaptures = null;
            m_DeadLookup = null;

            int coveredByPatterns = 0;
            int gapCount = 0;
            bool inGap = false;
            int lastPatternEnd = -1;

            int p = 0;
            while (p < n)
            {
                (Candidate best, int endSeg) = m_Aborted ? (null, -1) : BestTileAt(p, n);

                if (best != null)
                {
                    tiles.Add(Materialize(best, isRoot: true));
                    coveredByPatterns += endSeg - p + 1;
                    lastPatternEnd = endSeg;
                    inGap = false;
                    p = endSeg + 1;
                }
                else
                {
                    // No multi-wave model fits at this cursor: cover the single segment
                    // with an atomic leaf so the tiling stays hole-free (T-MK-1).
                    tiles.Add(MakeFillerLeaf(p));
                    if (!inGap)
                        gapCount++;
                    inGap = true;
                    p++;
                }
            }

            metrics.NodesCreated = m_NodesCreated;
            metrics.Aborted = m_Aborted;
            metrics.CompleteRoots = tiles.Count;
            metrics.Coverage = (double)coveredByPatterns / n;
            metrics.GapCount = gapCount;
            FillMemoStats(metrics);

            TreeNode projection = BuildEdgeProjection(lastPatternEnd, n);
            return new ContinuousMarkupResult(tiles, metrics, projection);
        }

        /// <summary>
        /// Convenience adapter for the debug viewer (§13.1/§17.1): runs the gap-free
        /// continuous markup (<see cref="ParseContinuous"/>) and exposes the top-level
        /// tiles as the roots of a <see cref="MarkupSearchResult"/>. Unlike
        /// <see cref="Parse"/> — which only yields a root when the <em>whole</em> range is a
        /// single complete model — this always covers an arbitrary range, so the replay
        /// tree is never empty.
        /// </summary>
        public MarkupSearchResult ParseTiled()
        {
            ContinuousMarkupResult c = ParseContinuous();
            return new MarkupSearchResult(c.Tiles, c.Metrics, c.BestProjection);
        }

        /// <summary>
        /// Finds the longest rule-valid top-level pattern that starts at segment
        /// <paramref name="p"/> and fits within the bounded window
        /// <c>[p .. min(p+W-1, n-1)]</c>. Among candidates of equal (maximal) length the
        /// highest model-weighted score wins; ties resolve by <see cref="StartModels"/>
        /// order for determinism (T-MK-4).
        /// </summary>
        private (Candidate best, int endSeg) BestTileAt(int p, int n)
        {
            int hi = Math.Min(p + STITCH_MAX_WINDOW_SEGMENTS - 1, n - 1);

            // Prefer the longest coverage (§15.4 Coverage → max): iterate q descending and
            // return the first end that yields any valid top-level pattern.
            for (int q = hi; q >= p + STITCH_MIN_PATTERN_SEGMENTS - 1; q--)
            {
                Candidate localBest = null;
                double localScore = double.NegativeInfinity;

                foreach (ElliottModelType model in StartModels)
                {
                    foreach (Candidate c in ParseRange(p, q, model))
                    {
                        double score = c.Score * ModelProbability(c.Model);
                        if (score > localScore)
                        {
                            localScore = score;
                            localBest = c with { Score = score };
                        }
                    }

                    if (m_Aborted)
                        return (null, -1);
                }

                if (localBest != null)
                    return (localBest, q);
            }

            return (null, -1);
        }

        /// <summary>
        /// Builds an atomic <see cref="ElliottModelType.SIMPLE_IMPULSE"/> leaf over the
        /// single segment <paramref name="seg"/> (§10), used to fill a stretch that no
        /// multi-wave model can cover so the stitched tiling has no holes.
        /// </summary>
        private TreeNode MakeFillerLeaf(int seg)
        {
            var node = new TreeNode(ElliottModelType.SIMPLE_IMPULSE, null, 0)
            {
                RangeStartSegment = seg,
                RangeEndSegment = seg,
                StartPivot = Pivots[seg],
                EndPivot = Pivots[seg + 1],
                Score = ModelProbability(ElliottModelType.SIMPLE_IMPULSE)
            };
            node.MarkComplete();
            return node;
        }

        /// <summary>
        /// Computes the single best §13 projection for the still-forming pattern at the
        /// right edge — the bounded trailing region after the last committed multi-wave
        /// tile. Returns <c>null</c> when the history ends exactly on a completed tile or
        /// the trailing region is larger than one window.
        /// </summary>
        private TreeNode BuildEdgeProjection(int lastPatternEnd, int n)
        {
            if (m_Aborted)
                return null;

            int start = lastPatternEnd + 1;
            if (start >= n || n - start > STITCH_MAX_WINDOW_SEGMENTS)
                return null;

            List<Candidate> projection = BuildProjectionRoots(start, n - 1);
            return projection.Count > 0 ? MaterializeProjection(projection[0]) : null;
        }

        /// <summary>Fills the §15.4 memo/beam complexity stats from the persistent stitch memo.</summary>
        private void FillMemoStats(MarkupSearchMetrics metrics)
        {
            long keptTotal = 0;
            int cells = 0;
            foreach (KeyValuePair<(int, int, ElliottModelType), IReadOnlyList<Candidate>> e in m_Memo)
            {
                cells++;
                keptTotal += e.Value.Count;
            }

            metrics.MemoCells = cells;
            metrics.AvgBeamWidth = cells > 0 ? (double)keptTotal / cells : 0.0;
        }
    }

    /// <summary>
    /// Result of a continuous whole-history markup (EW_MARKUP_v2 §15.3): a gap-free,
    /// ordered sequence of top-level tiles plus the coverage metrics and the forming-edge
    /// projection.
    /// </summary>
    public sealed class ContinuousMarkupResult
    {
        internal ContinuousMarkupResult(
            IReadOnlyList<TreeNode> tiles, MarkupSearchMetrics metrics, TreeNode bestProjection)
        {
            Tiles = tiles;
            Metrics = metrics;
            BestProjection = bestProjection;
        }

        /// <summary>
        /// Gets the ordered top-level tiles covering the whole input with no gaps: every
        /// segment is inside exactly one tile and adjacent tiles join end-to-end (T-MK-1).
        /// </summary>
        public IReadOnlyList<TreeNode> Tiles { get; }

        /// <summary>Gets the coverage and complexity metrics for the stitch (§15.4).</summary>
        public MarkupSearchMetrics Metrics { get; }

        /// <summary>
        /// Gets the single best <see cref="NodeStatus.PROJECTED"/> continuation for the
        /// still-forming pattern at the right edge (§13); <c>null</c> when the history ends
        /// on a completed tile.
        /// </summary>
        public TreeNode BestProjection { get; }
    }
}
