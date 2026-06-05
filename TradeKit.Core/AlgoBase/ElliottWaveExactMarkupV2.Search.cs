using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Step 5 of EW_MARKUP_v2.md §19 — the markup search engine: interval
    /// dynamic-programming (CYK-style chart parsing, §14.3) over the input zigzag
    /// segments, bounded by beam search (§14.2) and hard caps (§14.5), with the
    /// hard death rules (§7–9) applied incrementally as each wave is attached
    /// (branch-and-bound, §14.1).
    /// <para>
    /// The DP works over <see cref="Candidate"/> blueprints — the hypothesis
    /// "segment range <c>[i..j]</c> is model <c>M</c>" is independent of how the
    /// path before it was marked up, so it is memoized by <c>(i, j, M)</c>. Every
    /// produced candidate is a fully-assembled, rule-valid model; the chosen roots
    /// are materialized into <see cref="TreeNode"/> trees and marked
    /// <see cref="NodeStatus.COMPLETE"/>.
    /// </para>
    /// <para>
    /// Scoring here is intentionally minimal (model probability coefficients from
    /// <see cref="ElliottWavePatternHelper.ModelRules"/>); the full Fibonacci
    /// scoring and the soft penalties (§16) are calibrated in Step 8. Projections
    /// (PROJECTED, §13) and extensions (§11) arrive in Step 6; full-history
    /// stitching / coverage (§15.3) in Step 9.
    /// </para>
    /// </summary>
    public partial class ElliottWaveExactMarkupV2
    {
        /// <summary>Maximum live variants kept per segment range (§14.2).</summary>
        public const int BEAM_WIDTH = 20;

        /// <summary>Minimum variants guaranteed per model type within a beam (§14.2).</summary>
        public const int MIN_PER_MODEL = 3;

        /// <summary>Maximum notation depth a node may reach (§12).</summary>
        public const int MAX_LEVELS_V2 = 10;

        /// <summary>Absolute safety cap on assembled candidates per search (§14.5).</summary>
        public const int MAX_NODES_TOTAL = 2_000_000;

        /// <summary>
        /// Models the engine currently assembles. Restricted to the models with
        /// dedicated hard-rule support (§7–9); expanding diagonals,
        /// <see cref="ElliottModelType.COMBINATION"/> and other rare models are
        /// deferred (they remain valid only as atomic <see cref="ElliottModelType.SIMPLE_IMPULSE"/>
        /// leaves until later calibration).
        /// </summary>
        private static readonly HashSet<ElliottModelType> S_SUPPORTED_MODELS = new()
        {
            ElliottModelType.IMPULSE,
            ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
            ElliottModelType.DIAGONAL_CONTRACTING_ENDING,
            ElliottModelType.ZIGZAG,
            ElliottModelType.DOUBLE_ZIGZAG,
            ElliottModelType.TRIPLE_ZIGZAG,
            ElliottModelType.FLAT_EXTENDED,
            ElliottModelType.FLAT_RUNNING,
            ElliottModelType.FLAT_REGULAR,
            ElliottModelType.TRIANGLE_CONTRACTING,
            ElliottModelType.TRIANGLE_RUNNING,
            ElliottModelType.SIMPLE_IMPULSE
        };

        /// <summary>
        /// Models granted the §6.3 W4-overlap exception: when an IMPULSE wave 4 is
        /// one of these, it may legally end inside the wave-1 price zone.
        /// </summary>
        private static readonly HashSet<ElliottModelType> S_WAVE4_OVERLAP_ALLOWED = new()
        {
            ElliottModelType.TRIANGLE_CONTRACTING,
            ElliottModelType.TRIANGLE_RUNNING,
            ElliottModelType.FLAT_EXTENDED,
            ElliottModelType.FLAT_RUNNING,
            ElliottModelType.FLAT_REGULAR
        };

        private Dictionary<(int, int, ElliottModelType), IReadOnlyList<Candidate>> m_Memo;
        private MarkupSearchMetrics m_Metrics;
        private int m_NodesCreated;
        private bool m_Aborted;

        /// <summary>
        /// An immutable assembled-model blueprint produced by the DP. Materialized
        /// into a <see cref="TreeNode"/> on demand for the selected roots.
        /// </summary>
        private sealed record Candidate(
            ElliottModelType Model,
            string WavePos,
            int StartSeg,
            int EndSeg,
            byte Level,
            double Score,
            IReadOnlyList<Candidate> Children);

        /// <summary>
        /// Runs the markup search over the whole input range and returns the best
        /// <see cref="NodeStatus.COMPLETE"/> roots together with search metrics.
        /// </summary>
        public MarkupSearchResult Parse(int deadDepth = 0) => ParseSegmentRange(0, Segments.Count - 1, deadDepth);

        /// <summary>
        /// Runs the markup search over the segment range <c>[startSeg..endSeg]</c>.
        /// </summary>
        /// <param name="startSeg">Inclusive first segment index.</param>
        /// <param name="endSeg">Inclusive last segment index.</param>
        /// <param name="deadDepth">
        /// When &gt; 0, dead wave hypotheses are captured during the search so they can be
        /// attached to the exported tree (§17 debug export). Default 0 means zero overhead.
        /// </param>
        public MarkupSearchResult ParseSegmentRange(int startSeg, int endSeg, int deadDepth = 0)
        {
            if (startSeg < 0 || endSeg >= Segments.Count || endSeg < startSeg)
                throw new ArgumentOutOfRangeException(nameof(startSeg));

            m_Memo = new Dictionary<(int, int, ElliottModelType), IReadOnlyList<Candidate>>();
            m_Metrics = new MarkupSearchMetrics();
            m_NodesCreated = 0;
            m_Aborted = false;
            m_DeadDepth = deadDepth;
            m_DeadCaptures = deadDepth > 0 ? new List<DeadCapture>() : null;
            m_DeadLookup = null;

            var roots = new List<Candidate>();
            foreach (ElliottModelType model in StartModels)
                roots.AddRange(ParseRange(startSeg, endSeg, model));

            roots = Beam(roots);

            // §13 prediction mode: best partial continuation reaching the current bar.
            List<Candidate> projection = BuildProjectionRoots(startSeg, endSeg);

            var nodes = new List<TreeNode>(roots.Count);
            foreach (Candidate c in roots)
                nodes.Add(Materialize(c, isRoot: true));

            TreeNode bestProjection = projection.Count > 0
                ? MaterializeProjection(projection[0])
                : null;

            FillMetrics(nodes, startSeg, endSeg);
            return new MarkupSearchResult(nodes, m_Metrics, bestProjection);
        }

        /// <summary>
        /// Returns the beam-kept candidate blueprints for "range <c>[i..j]</c> is
        /// model <paramref name="model"/>", using memoization.
        /// </summary>
        private IReadOnlyList<Candidate> ParseRange(int i, int j, ElliottModelType model)
        {
            if (!S_SUPPORTED_MODELS.Contains(model))
                return Array.Empty<Candidate>();

            var key = (i, j, model);
            if (m_Memo.TryGetValue(key, out IReadOnlyList<Candidate> cached))
                return cached;

            // Guard against re-entrancy (ranges strictly shrink, so this is a belt).
            m_Memo[key] = Array.Empty<Candidate>();

            IReadOnlyList<Candidate> result =
                model == ElliottModelType.SIMPLE_IMPULSE
                    ? BuildLeaf(i, j)
                    : BuildComposite(i, j, model);

            m_Memo[key] = result;
            return result;
        }

        /// <summary>Builds the atomic <see cref="ElliottModelType.SIMPLE_IMPULSE"/> leaf (one segment).</summary>
        private IReadOnlyList<Candidate> BuildLeaf(int i, int j)
        {
            if (i != j || m_Aborted)
                return Array.Empty<Candidate>();

            if (++m_NodesCreated > MAX_NODES_TOTAL)
            {
                m_Aborted = true;
                return Array.Empty<Candidate>();
            }

            double score = ModelProbability(ElliottModelType.SIMPLE_IMPULSE);
            return new[]
            {
                new Candidate(
                    ElliottModelType.SIMPLE_IMPULSE, null, i, j, 0, score,
                    Array.Empty<Candidate>())
            };
        }

        /// <summary>
        /// Builds all rule-valid assemblies of a multi-wave model over <c>[i..j]</c>
        /// by recursively splitting the range into its expected waves, applying the
        /// hard rules incrementally and beam-limiting the result.
        /// </summary>
        private IReadOnlyList<Candidate> BuildComposite(int i, int j, ElliottModelType model)
        {
            int waves = ElliottWaveExactMarkup.GetExpectedWaves(model);
            int len = j - i + 1;

            // K odd-length parts sum to len ⇒ len and K share parity; need len ≥ K.
            if (len < waves || ((len - waves) & 1) != 0)
                return Array.Empty<Candidate>();

            var results = new List<Candidate>();
            Extend(model, waves, i, j, 0, new List<Candidate>(waves), results);
            return Beam(results);
        }

        /// <summary>
        /// Recursively fixes the next wave of <paramref name="model"/> over the
        /// remaining range, attaching each beam-kept sub-model option and pruning
        /// on the incremental hard rules (§7–9).
        /// </summary>
        private void Extend(
            ElliottModelType model,
            int waves,
            int segStart,
            int rangeEnd,
            int waveIndex,
            List<Candidate> chosen,
            List<Candidate> results)
        {
            if (m_Aborted)
                return;

            bool isLast = waveIndex == waves - 1;
            int remainingAfter = waves - waveIndex - 1;

            // The current wave occupies [segStart..p] (odd length); leave enough,
            // parity-matched, segments for the remaining waves.
            for (int p = segStart; p <= rangeEnd; p++)
            {
                int waveLen = p - segStart + 1;
                if ((waveLen & 1) == 0)
                    continue; // each wave spans an odd number of alternating segments

                int rest = rangeEnd - p;
                if (rest < remainingAfter || ((rest - remainingAfter) & 1) != 0)
                    continue;
                if (isLast && p != rangeEnd)
                    continue;

                foreach (Candidate child in WaveOptions(model, waveIndex, segStart, p))
                {
                    chosen.Add(child);

                    DeathReason death = CheckIncremental(model, chosen);
                    if (death != DeathReason.NONE)
                    {
                        m_Metrics.Count(death);
                        if (m_DeadCaptures != null && m_DeadCaptures.Count < MAX_DEAD_CAPTURES)
                            m_DeadCaptures.Add(new DeadCapture(
                                model, chosen[0].StartSeg, rangeEnd, waveIndex,
                                child.Model, child.StartSeg, child.EndSeg, death));
                        chosen.RemoveAt(chosen.Count - 1);
                        continue;
                    }

                    if (isLast)
                        Finalize(model, i: SegFirst(chosen), j: rangeEnd, chosen, results);
                    else
                        Extend(model, waves, p + 1, rangeEnd, waveIndex + 1, chosen, results);

                    chosen.RemoveAt(chosen.Count - 1);
                    if (m_Aborted)
                        return;
                }
            }
        }

        private static int SegFirst(List<Candidate> chosen) => chosen[0].StartSeg;

        /// <summary>
        /// Materializes a finished wave sequence into a <see cref="Candidate"/> and
        /// adds it to <paramref name="results"/> (subject to the level cap and the
        /// global node cap).
        /// </summary>
        private void Finalize(
            ElliottModelType model, int i, int j, List<Candidate> chosen, List<Candidate> results)
        {
            byte level = 0;
            double score = ModelProbability(model);
            foreach (Candidate c in chosen)
            {
                if (c.Level >= level)
                    level = (byte)(c.Level + 1);
                score *= c.Score;
            }

            if (level > MAX_LEVELS_V2)
                return;

            if (++m_NodesCreated > MAX_NODES_TOTAL)
            {
                m_Aborted = true;
                return;
            }

            results.Add(new Candidate(
                model, null, i, j, level, score, chosen.ToArray()));
        }

        /// <summary>
        /// Returns the beam-kept sub-model candidates that can fill wave
        /// <paramref name="waveIndex"/> of <paramref name="parentModel"/> over
        /// range <c>[a..b]</c> (§5.1 source-of-truth rules + the atomic leaf).
        /// </summary>
        private IReadOnlyList<Candidate> WaveOptions(
            ElliottModelType parentModel, int waveIndex, int a, int b)
        {
            string waveKey = ElliottWaveExactMarkup.GetWaveKey(parentModel, waveIndex + 1);
            var options = new List<Candidate>();

            // SIMPLE_IMPULSE may fill any position, but only a single segment (§10).
            if (a == b)
                options.AddRange(ParseRange(a, b, ElliottModelType.SIMPLE_IMPULSE));

            if (ElliottWavePatternHelper.ModelRules.TryGetValue(parentModel, out ModelRules rules)
                && rules.Models.TryGetValue(waveKey, out ElliottModelType[] subModels))
            {
                foreach (ElliottModelType sub in subModels)
                {
                    if (sub == ElliottModelType.SIMPLE_IMPULSE || !S_SUPPORTED_MODELS.Contains(sub))
                        continue;
                    options.AddRange(ParseRange(a, b, sub));
                }
            }

            return Beam(options);
        }

        /// <summary>
        /// Runs the incremental hard-price (§7) and hard-time (§8) checks against
        /// the waves gathered so far for <paramref name="model"/>.
        /// </summary>
        private DeathReason CheckIncremental(ElliottModelType model, List<Candidate> chosen)
        {
            var waves = new List<Segment>(chosen.Count);
            foreach (Candidate c in chosen)
                waves.Add(new Segment(Pivots[c.StartSeg], Pivots[c.EndSeg + 1]));

            bool wave4Simple = waves.Count < 4
                || !S_WAVE4_OVERLAP_ALLOWED.Contains(chosen[3].Model);

            DeathReason price = CheckPriceRules(model, waves, wave4Simple);
            if (price != DeathReason.NONE)
                return price;

            return CheckTimeWindow(model, waves);
        }

        /// <summary>
        /// Keeps the top <see cref="BEAM_WIDTH"/> candidates by score while
        /// guaranteeing at least <see cref="MIN_PER_MODEL"/> per model type (§14.2).
        /// Ordering is fully deterministic for the determinism invariant (T-MK-4).
        /// </summary>
        private static List<Candidate> Beam(List<Candidate> candidates)
        {
            if (candidates.Count <= 1)
                return candidates;

            candidates.Sort(CompareCandidates);

            var kept = new List<Candidate>(Math.Min(BEAM_WIDTH, candidates.Count));
            var perModel = new Dictionary<ElliottModelType, int>();
            var overflow = new List<Candidate>();

            foreach (Candidate c in candidates)
            {
                perModel.TryGetValue(c.Model, out int n);
                if (n < MIN_PER_MODEL)
                {
                    perModel[c.Model] = n + 1;
                    kept.Add(c);
                }
                else
                {
                    overflow.Add(c);
                }
            }

            foreach (Candidate c in overflow)
            {
                if (kept.Count >= BEAM_WIDTH)
                    break;
                kept.Add(c);
            }

            kept.Sort(CompareCandidates);
            if (kept.Count > BEAM_WIDTH)
                kept.RemoveRange(BEAM_WIDTH, kept.Count - BEAM_WIDTH);

            return kept;
        }

        /// <summary>Deterministic candidate ordering: score desc, then stable tie-breaks.</summary>
        private static int CompareCandidates(Candidate x, Candidate y)
        {
            int byScore = y.Score.CompareTo(x.Score);
            if (byScore != 0)
                return byScore;
            int byModel = ((int)x.Model).CompareTo((int)y.Model);
            if (byModel != 0)
                return byModel;
            int byStart = x.StartSeg.CompareTo(y.StartSeg);
            return byStart != 0 ? byStart : x.EndSeg.CompareTo(y.EndSeg);
        }

        /// <summary>
        /// Recursively turns a <see cref="Candidate"/> blueprint into a live
        /// <see cref="TreeNode"/> tree and marks it <see cref="NodeStatus.COMPLETE"/>.
        /// </summary>
        private TreeNode Materialize(Candidate c, bool isRoot)
        {
            var node = new TreeNode(
                c.Model,
                isRoot ? null : c.WavePos,
                c.Level)
            {
                RangeStartSegment = c.StartSeg,
                RangeEndSegment = c.EndSeg,
                StartPivot = Pivots[c.StartSeg],
                EndPivot = Pivots[c.EndSeg + 1],
                Score = c.Score
            };

            for (int k = 0; k < c.Children.Count; k++)
            {
                Candidate childBlueprint = c.Children[k];
                Candidate withPos = childBlueprint with
                {
                    WavePos = ElliottWaveExactMarkup.GetWaveKey(c.Model, k + 1)
                };
                node.AddChild(Materialize(withPos, isRoot: false));
            }

            node.MarkComplete();
            return node;
        }

        /// <summary>Model probability coefficient from the shared rules (§16.2 start values).</summary>
        private static double ModelProbability(ElliottModelType model) =>
            ElliottWavePatternHelper.ModelRules.TryGetValue(model, out ModelRules r)
                ? r.ProbabilityCoefficient
                : 1.0;

        /// <summary>Computes the §15.4 coverage/complexity metrics for the produced roots.</summary>
        private void FillMetrics(IReadOnlyList<TreeNode> roots, int startSeg, int endSeg)
        {
            m_Metrics.NodesCreated = m_NodesCreated;
            m_Metrics.Aborted = m_Aborted;
            m_Metrics.CompleteRoots = roots.Count;

            int rangeLen = endSeg - startSeg + 1;
            m_Metrics.Coverage = roots.Count > 0 ? 1.0 : 0.0; // single-range parse
            m_Metrics.GapCount = roots.Count > 0 ? 0 : 1;      // refined by stitching (Step 9)
            m_Metrics.RangeSegments = rangeLen;

            long keptTotal = 0;
            int cells = 0;
            foreach (KeyValuePair<(int, int, ElliottModelType), IReadOnlyList<Candidate>> e in m_Memo)
            {
                cells++;
                keptTotal += e.Value.Count;
            }

            m_Metrics.MemoCells = cells;
            m_Metrics.AvgBeamWidth = cells > 0 ? (double)keptTotal / cells : 0.0;
        }
    }

    /// <summary>Result of a markup search: the COMPLETE roots and the run metrics.</summary>
    public sealed class MarkupSearchResult
    {
        internal MarkupSearchResult(
            IReadOnlyList<TreeNode> roots, MarkupSearchMetrics metrics, TreeNode bestProjection = null)
        {
            Roots = roots;
            Metrics = metrics;
            BestProjection = bestProjection;
        }

        /// <summary>Gets the best <see cref="NodeStatus.COMPLETE"/> roots over the parsed range.</summary>
        public IReadOnlyList<TreeNode> Roots { get; }

        /// <summary>
        /// Gets the single best <see cref="NodeStatus.PROJECTED"/> continuation whose confirmed
        /// prefix reaches the current bar (§13); <c>null</c> when no partial model fits.
        /// </summary>
        public TreeNode BestProjection { get; }

        /// <summary>Gets the search metrics (§15.4).</summary>
        public MarkupSearchMetrics Metrics { get; }
    }

    /// <summary>Coverage and complexity metrics for a markup search (EW_MARKUP_v2 §15.4).</summary>
    public sealed class MarkupSearchMetrics
    {
        /// <summary>Gets or sets the number of assembled candidate blueprints.</summary>
        public int NodesCreated { get; set; }

        /// <summary>Gets or sets a value indicating whether the node cap aborted the search.</summary>
        public bool Aborted { get; set; }

        /// <summary>Gets or sets the number of COMPLETE roots returned.</summary>
        public int CompleteRoots { get; set; }

        /// <summary>Gets or sets the fraction of the parsed range covered by a COMPLETE root.</summary>
        public double Coverage { get; set; }

        /// <summary>Gets or sets the number of unmarked gaps (→ 0 target, Step 9).</summary>
        public int GapCount { get; set; }

        /// <summary>Gets or sets the number of segments in the parsed range.</summary>
        public int RangeSegments { get; set; }

        /// <summary>Gets or sets the number of memoized DP cells.</summary>
        public int MemoCells { get; set; }

        /// <summary>Gets or sets the average kept beam width across DP cells.</summary>
        public double AvgBeamWidth { get; set; }

        /// <summary>Gets the distribution of node death causes (diagnostics).</summary>
        public Dictionary<DeathReason, int> DeathByReason { get; } = new();

        /// <summary>Increments the death counter for <paramref name="reason"/>.</summary>
        internal void Count(DeathReason reason)
        {
            DeathByReason.TryGetValue(reason, out int n);
            DeathByReason[reason] = n + 1;
        }
    }
}
