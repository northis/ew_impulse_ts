using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Implements segment-based Elliott Wave markup algorithm (v1).
    /// Each sub-wave = one segment between consecutive extremum points.
    /// </summary>
    public partial class ElliottWaveExactMarkup
    {
        private const double FULL_FIT_BONUS = 1.2;

        /// <summary>
        /// Maximum number of top-scoring candidates passed to
        /// <see cref="FillSubWaveModels"/> for recursive sub-wave analysis.
        /// Lower-ranked candidates are unlikely to win after the depth-coverage
        /// re-sort, so capping avoids exponential work on mediocre combinations.
        /// </summary>
        private const int MAX_CANDIDATES_FOR_SUBWAVE_FILL = 20;

        /// <summary>
        /// Minimum number of top-scoring candidates retained per model type
        /// before the global <see cref="MAX_CANDIDATES_FOR_SUBWAVE_FILL"/> cap
        /// is applied.  Guarantees that minority models (e.g. ZIGZAG when IMPULSE
        /// dominates) always get a chance at recursive sub-wave filling.
        /// </summary>
        private const int MIN_CANDIDATES_PER_MODEL = 3;

        /// <summary>
        /// Maximum number of candidate combinations evaluated per model type
        /// inside <see cref="TryCombinationsRecurse"/>.  When the alternating-
        /// point count is large, IMPULSE (5-wave) generates O(n^4) combinations;
        /// this cap prevents the search from taking minutes on 200+ points.
        /// </summary>
        private const int MAX_ITERATIONS_PER_MODEL = 500_000;

        /// <summary>
        /// Maximum score multiplier applied to a candidate when all of its sub-waves
        /// (and their sub-waves, recursively) have been successfully identified.
        /// A value of 0.5 means a fully-identified markup can earn up to 1.5× its
        /// raw Fibonacci score, giving it priority over structurally equivalent
        /// candidates whose sub-waves remain as <see cref="ElliottModelType.SIMPLE_IMPULSE"/>.
        /// </summary>
        private const double DEPTH_COVERAGE_BONUS = 0.5;

        /// <summary>
        /// Score multiplier applied when a corrective sub-wave (b or x) is
        /// identified as a triangle (contracting or running).  Triangles in
        /// corrective positions are structurally significant — they explain
        /// unusually shallow price retracements that otherwise score poorly
        /// on the Fibonacci maps, compensating for the low top-level ratio.
        /// </summary>
        private const double TRIANGLE_CORRECTIVE_BONUS = 2.0;

        /// <summary>
        /// Maximum recursion depth for sub-wave model identification.
        /// Depth 0 = top-level pattern; depth 1 = first-level sub-waves.
        /// At depth MAX_MARKUP_DEPTH the recursion stops — sub-waves at that
        /// level are left as SIMPLE_IMPULSE segments.
        /// </summary>
        public const int MAX_MARKUP_DEPTH = 2;

        /// <summary>
        /// Minimum fraction of the reference wave's length by which the next
        /// same-direction wave must exceed the reference wave's endpoint.
        /// Prevents diagonals where W3 barely touches W1 from being accepted.
        /// </summary>
        private const double MIN_DIAGONAL_PENETRATION = 0.05;

        /// <summary>
        /// Cross-level harmony thresholds (§4.3-harmony).
        /// A sub-wave at depth N+1 whose bar count exceeds
        /// <see cref="HARMONY_HARD_RATIO"/> × the shortest wave at depth N
        /// is rejected outright.  Between <see cref="HARMONY_PESSIMIZE_RATIO"/>
        /// and the hard limit the candidate's score is multiplied by
        /// <see cref="HARMONY_PENALTY"/>.
        /// </summary>
        private const double HARMONY_PESSIMIZE_RATIO = 2.0;
        private const double HARMONY_HARD_RATIO = 3.0;
        private const double HARMONY_PENALTY = 0.5;

        /// <summary>
        /// Deviation percentage used by <see cref="SimpleExtremumFinder"/> when
        /// re-discovering finer extrema inside a sub-wave range.  A smaller value
        /// yields more intermediate points so that 5-wave models (triangles,
        /// diagonals, impulses) can be detected even when the parent-level zigzag
        /// produced too few points in the sub-wave region.
        /// </summary>
        private const double SUBWAVE_REDISCOVERY_DEVIATION = 0.03;

        /// <summary>
        /// Minimum number of alternating extrema required to attempt a 5-wave
        /// model (triangle, impulse, diagonal).  5 waves need at least 6 points.
        /// </summary>
        private const int MIN_POINTS_FOR_5_WAVE = 6;

        /// <summary>
        /// Minimum value of <see cref="ExactParsedNode.GetDepth"/> required for a
        /// top-level result to be returned from <see cref="Parse"/>.
        /// A value of <c>MAX_MARKUP_DEPTH / 2</c> guarantees that every direct
        /// sub-wave of the top-level model was itself successfully identified
        /// (i.e. none of them stayed as a bare SIMPLE_IMPULSE segment after
        /// the recursive decomposition pass).
        /// </summary>
        public const int MIN_RESULT_DEPTH = MAX_MARKUP_DEPTH / 2;

        // Optional bars provider used to enforce the direction hard rule:
        // a downward wave must end at the LOW of its end bar (not the HIGH),
        // and an upward wave must end at the HIGH of its end bar.
        private readonly IBarsProvider m_BarsProvider;

        /// <summary>
        /// Instance-level markup depth limit (defaults to <see cref="MAX_MARKUP_DEPTH"/>).
        /// </summary>
        private readonly int m_MaxMarkupDepth;

        /// <summary>
        /// Initialises the markup engine.
        /// </summary>
        /// <param name="barsProvider">
        /// Optional.  When supplied every wave endpoint is validated against the
        /// actual High/Low price of the corresponding bar (hard rule §4.1):
        /// descending waves must finish at the bar LOW, ascending waves at the bar HIGH.
        /// Pass <c>null</c> (default) to skip this check — useful in unit tests that
        /// work with synthetic price points.
        /// </param>
        /// <param name="maxMarkupDepth">
        /// Maximum recursion depth. Defaults to <see cref="MAX_MARKUP_DEPTH"/>.
        /// </param>
        public ElliottWaveExactMarkup(IBarsProvider barsProvider = null, int maxMarkupDepth = MAX_MARKUP_DEPTH)
        {
            m_BarsProvider = barsProvider;
            m_MaxMarkupDepth = maxMarkupDepth;
        }

        /// <summary>
        /// Defines the target wave models that the algorithm attempts to identify.
        /// </summary>
        public static readonly ElliottModelType[] TargetModels =
        {
            ElliottModelType.IMPULSE,
            ElliottModelType.ZIGZAG,
            ElliottModelType.DOUBLE_ZIGZAG,
            ElliottModelType.FLAT_EXTENDED,
            ElliottModelType.FLAT_RUNNING,
            ElliottModelType.FLAT_REGULAR,
            ElliottModelType.TRIANGLE_CONTRACTING,
            ElliottModelType.TRIANGLE_RUNNING,
            ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
            ElliottModelType.DIAGONAL_CONTRACTING_ENDING
        };

        // Pre-computed mapping (parentModel, waveKey) → allowed sub-models ∩ TargetModels.
        // Computed once at class load; avoids repeated LINQ Intersect in FillSubWaveModels.
        private static readonly Dictionary<(ElliottModelType, string), ElliottModelType[]>
            VALID_SUB_MODELS_CACHE = BuildValidSubModelsCache();

        private static Dictionary<(ElliottModelType, string), ElliottModelType[]> BuildValidSubModelsCache()
        {
            var cache = new Dictionary<(ElliottModelType, string), ElliottModelType[]>();
            var targetSet = new HashSet<ElliottModelType>(TargetModels);
            foreach (KeyValuePair<ElliottModelType, ModelRules> kvp in ElliottWavePatternHelper.ModelRules)
                foreach (KeyValuePair<string, ElliottModelType[]> waveEntry in kvp.Value.Models)
                    cache[(kvp.Key, waveEntry.Key)] =
                        waveEntry.Value.Where(m => targetSet.Contains(m)).ToArray();
            return cache;
        }

        /// <summary>
        /// Gets the string key representation for a specific sub-wave number within a parent model type.
        /// </summary>
        public static string GetWaveKey(ElliottModelType type, int waveNum)
        {
            if (type == ElliottModelType.IMPULSE ||
                type == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL ||
                type == ElliottModelType.DIAGONAL_CONTRACTING_ENDING)
                return waveNum.ToString();
            if (type == ElliottModelType.ZIGZAG ||
                type == ElliottModelType.FLAT_EXTENDED ||
                type == ElliottModelType.FLAT_RUNNING ||
                type == ElliottModelType.FLAT_REGULAR)
                return waveNum == 1 ? "a" : (waveNum == 2 ? "b" : "c");
            if (type == ElliottModelType.DOUBLE_ZIGZAG)
                return waveNum == 1 ? "w" : (waveNum == 2 ? "x" : "y");
            if (type == ElliottModelType.TRIANGLE_CONTRACTING ||
                type == ElliottModelType.TRIANGLE_RUNNING)
                return ((char)('a' + waveNum - 1)).ToString();
            return "";
        }

        /// <summary>
        /// Gets the expected total number of sub-waves for a given Elliott wave model type.
        /// </summary>
        public static int GetExpectedWaves(ElliottModelType type)
        {
            if (type == ElliottModelType.IMPULSE ||
                type == ElliottModelType.TRIANGLE_CONTRACTING ||
                type == ElliottModelType.TRIANGLE_RUNNING ||
                type == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL ||
                type == ElliottModelType.DIAGONAL_CONTRACTING_ENDING) return 5;
            if (type == ElliottModelType.SIMPLE_IMPULSE) return 1;
            return 3;
        }

        /// <summary>
        /// Represents a single price segment between two consecutive extremum points.
        /// </summary>
        private struct Segment
        {
            public BarPoint Start;
            public BarPoint End;
            public bool IsUp => End.Value > Start.Value;
            public double Length => Math.Abs(End.Value - Start.Value);
        }

        /// <summary>
        /// Reduces a list of points to a strictly alternating (zigzag) sequence.
        /// Consecutive same-direction moves are collapsed to keep only the most extreme point.
        /// </summary>
        private static List<BarPoint> ReduceToAlternating(List<BarPoint> points)
        {
            var result = new List<BarPoint> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                BarPoint prev = result[^1];
                BarPoint cur = points[i];
                if (Math.Abs(cur.Value - prev.Value) < prev.Value * 1e-10)
                    continue;
                if (result.Count == 1)
                {
                    result.Add(cur);
                    continue;
                }
                BarPoint prevPrev = result[^2];
                bool prevUp = prev.Value > prevPrev.Value;
                bool curUp = cur.Value > prev.Value;
                if (curUp == prevUp)
                {
                    result[^1] = curUp
                        ? (cur.Value > prev.Value ? cur : prev)
                        : (cur.Value < prev.Value ? cur : prev);
                }
                else
                {
                    result.Add(cur);
                }
            }
            return result;
        }

        /// <summary>
        /// Parses a sequence of extremum points to find the most probable Elliott Wave structures.
        /// Input points may contain multiple levels of sub-wave extrema (all collapsed to a
        /// strictly alternating sequence). For each model type the algorithm tries all combinations
        /// of intermediate alternating points between the fixed first and last input points,
        /// effectively searching for the best-scoring K-segment fit that spans the full input range.
        /// After identifying the top-level model, sub-waves are recursively analysed up to
        /// <see cref="MAX_MARKUP_DEPTH"/> levels deep.
        /// When an <see cref="IBarsProvider"/> was supplied, results with
        /// <see cref="ExactParsedNode.GetDepth"/> below
        /// <see cref="MIN_RESULT_DEPTH"/> are discarded (§3.8-depth-filter).
        /// Without a provider the filter is skipped (consistent with all other
        /// OHLC-level checks).
        /// </summary>
        public List<ExactParsedNode> Parse(List<BarPoint> points)
        {
            List<ExactParsedNode> results = ParseInternal(points, null, 0);

            if (m_BarsProvider != null)
            {
                results = results.Where(n => RepairSimpleImpulseLeaves(n)).ToList();
                results = results.Where(n => n.GetDepth() >= m_MaxMarkupDepth / 2).ToList();
            }
            return results;
        }

        /// <summary>
        /// Recursively walks the parsed tree.  For every SIMPLE_IMPULSE leaf:
        /// 1. Checks that no candle breaches the start price (hard → returns false).
        /// 2. Moves the endpoint to the true OHLC extremum in the wave direction.
        /// Returns false if the tree must be discarded (breach found).
        /// </summary>
        private bool RepairSimpleImpulseLeaves(ExactParsedNode node)
        {
            if (node?.SubWaves == null) return true;

            bool anyRepaired = false;
            // Triangle waves naturally overlap — skip breach checks for their children.
            bool isTriangleParent =
                node.ModelType == ElliottModelType.TRIANGLE_CONTRACTING
             || node.ModelType == ElliottModelType.TRIANGLE_RUNNING;

            for (int i = 0; i < node.WaveCount; i++)
            {
                ExactParsedNode sw = node.SubWaves[i];
                if (sw == null) continue;

                if (sw.ModelType == ElliottModelType.SIMPLE_IMPULSE)
                {
                    if (isTriangleParent)
                    {
                        // Triangle sub-waves overlap by definition — skip breach
                        // checks.  Still repair endpoints: scan into the next
                        // wave's bar range because the alternating-point boundary
                        // may not coincide with the actual OHLC price extreme
                        // (the extremum finder can place a premature local min/max
                        // when a brief counter-move is followed by continuation).
                        bool isUpT = sw.IsUp;
                        int fromBarT = sw.StartPoint.BarIndex;
                        int toBarT = sw.EndPoint.BarIndex;

                        // Extend scan into next wave (triangle waves overlap).
                        int scanEnd = toBarT;
                        if (i + 1 < node.WaveCount && node.SubWaves[i + 1] != null)
                            scanEnd = node.SubWaves[i + 1].EndPoint.BarIndex - 1;

                        double bestValT = isUpT ? double.MinValue : double.MaxValue;
                        int bestBarT = toBarT;

                        for (int b = fromBarT; b <= scanEnd; b++)
                        {
                            if (b < 0 || b >= m_BarsProvider.Count) continue;
                            double v = isUpT
                                ? m_BarsProvider.GetHighPrice(b)
                                : m_BarsProvider.GetLowPrice(b);
                            if (isUpT ? v > bestValT : v < bestValT)
                            {
                                bestValT = v;
                                bestBarT = b;
                            }
                        }

                        // Guard: bestBar must be strictly after startBar to
                        // prevent degenerate zero-bar waves.
                        if (bestBarT > fromBarT
                            && (Math.Abs(sw.EndPoint.Value - bestValT) > bestValT * 1e-10
                                || bestBarT != toBarT))
                        {
                            bool collapse = i + 1 < node.WaveCount
                                && node.SubWaves[i + 1] != null
                                && bestBarT >= node.SubWaves[i + 1].EndPoint.BarIndex;

                            if (!collapse)
                            {
                                var newEnd = new BarPoint(bestValT, bestBarT, m_BarsProvider);
                                sw.EndPoint = newEnd;
                                sw.EndIndex = bestBarT;

                                if (i + 1 < node.WaveCount && node.SubWaves[i + 1] != null)
                                {
                                    node.SubWaves[i + 1].StartPoint = newEnd;
                                    node.SubWaves[i + 1].StartIndex = bestBarT;
                                }

                                anyRepaired = true;
                            }
                        }
                        continue;
                    }

                    bool isUp = sw.IsUp;
                    double startPrice = sw.StartPoint.Value;
                    int fromBar = sw.StartPoint.BarIndex;
                    int toBar = sw.EndPoint.BarIndex;
                    double bestVal = isUp ? double.MinValue : double.MaxValue;
                    int bestBar = toBar;

                    for (int b = fromBar; b <= toBar; b++)
                    {
                        if (b < 0 || b >= m_BarsProvider.Count) continue;

                        // Breach check (skip fromBar — start extremum is on the opposite side).
                        if (b > fromBar)
                        {
                            if (isUp && m_BarsProvider.GetLowPrice(b) < startPrice)
                                return false;
                            if (!isUp && m_BarsProvider.GetHighPrice(b) > startPrice)
                                return false;
                        }

                        // Track the true extremum
                        double v = isUp
                            ? m_BarsProvider.GetHighPrice(b)
                            : m_BarsProvider.GetLowPrice(b);
                        if (isUp ? v > bestVal : v < bestVal)
                        {
                            bestVal = v;
                            bestBar = b;
                        }
                    }

                    // Repair endpoint to the true OHLC extremum
                    if (Math.Abs(sw.EndPoint.Value - bestVal) > bestVal * 1e-10
                        || bestBar != toBar)
                    {
                        bool collapse = i + 1 < node.WaveCount
                            && node.SubWaves[i + 1] != null
                            && bestBar >= node.SubWaves[i + 1].EndPoint.BarIndex;

                        if (!collapse)
                        {
                            var newEnd = new BarPoint(bestVal, bestBar, m_BarsProvider);
                            sw.EndPoint = newEnd;
                            sw.EndIndex = bestBar;

                            if (i + 1 < node.WaveCount && node.SubWaves[i + 1] != null)
                            {
                                node.SubWaves[i + 1].StartPoint = newEnd;
                                node.SubWaves[i + 1].StartIndex = bestBar;
                            }

                            anyRepaired = true;
                        }
                    }
                }
                else
                {
                    if (!RepairSimpleImpulseLeaves(sw))
                    {
                        // Demote structurally invalid sub-wave to SIMPLE_IMPULSE
                        // instead of discarding the entire tree — but only when the
                        // resulting leaf has no candle breach (against both start
                        // and end prices).
                        bool hasBreach = false;
                        if (m_BarsProvider != null)
                        {
                            bool swUp = sw.StartPoint.Value < sw.EndPoint.Value;
                            double sp = sw.StartPoint.Value;
                            double ep = sw.EndPoint.Value;
                            int f = Math.Min(sw.StartIndex, sw.EndIndex);
                            int t = Math.Max(sw.StartIndex, sw.EndIndex);
                            for (int b = f; b <= t; b++)
                            {
                                if (b < 0 || b >= m_BarsProvider.Count) continue;
                                double lo = m_BarsProvider.GetLowPrice(b);
                                double hi = m_BarsProvider.GetHighPrice(b);
                                if (swUp)
                                {
                                    if (lo < sp || hi > ep) { hasBreach = true; break; }
                                }
                                else
                                {
                                    if (hi > sp || lo < ep) { hasBreach = true; break; }
                                }
                            }
                        }
                        if (hasBreach) return false;
                        sw.ModelType = ElliottModelType.SIMPLE_IMPULSE;
                        sw.SubWaves = null;
                    }
                }
            }

            // After repairing leaf endpoints, re-validate the parent's hard rules
            // (endpoint shifts may have broken structural constraints).
            if (anyRepaired && node.ModelType != ElliottModelType.SIMPLE_IMPULSE)
            {
                var segs = new Segment[node.WaveCount];
                for (int j = 0; j < node.WaveCount; j++)
                {
                    ExactParsedNode sw = node.SubWaves[j];
                    segs[j] = new Segment { Start = sw.StartPoint, End = sw.EndPoint };
                }

                if (!CheckHardRules(node.ModelType, segs))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Internal recursive parse entry point.
        /// </summary>
        /// <param name="points">Original (possibly multi-level) extremum points.</param>
        /// <param name="allowedModels">Subset of models to try, or null to use <see cref="TargetModels"/>.</param>
        /// <param name="depth">Current recursion depth (0 = top level).</param>
        private List<ExactParsedNode> ParseInternal(
            List<BarPoint> points,
            ElliottModelType[] allowedModels,
            int depth)
        {
            if (points == null || points.Count < 2)
                return new List<ExactParsedNode>();

            // Collapse multi-level input to strictly alternating extrema
            List<BarPoint> altPoints = ReduceToAlternating(points);
            int n = altPoints.Count;
            if (n < 2)
                return new List<ExactParsedNode>();

            var results = new List<ExactParsedNode>();
            ElliottModelType[] modelsToSearch = allowedModels ?? TargetModels;

            // At the last markup level sub-waves are never further decomposed, so
            // motive/zigzag patterns must satisfy the endpoint-extremum rule (§4.1-endpoint).
            bool isLastLevel = depth + 1 >= m_MaxMarkupDepth;

            // §4.1-continuity (hard rule): a valid model must span the full input range —
            // from altPoints[0] to altPoints[n-1] — so that no input extremum is left
            // unaccounted for at this rank level. Partial-span windows (offset > 0) are
            // never considered; only the full-span TryCombinations search is used.

            foreach (ElliottModelType model in modelsToSearch)
            {
                int k = GetExpectedWaves(model);
                if (n - 1 < k) continue; // need at least k segments

                // Full-span search: fix start=0, end=n-1, pick K-1 intermediates from
                // altPoints[1..n-2]. Every accepted candidate starts at altPoints[0] and
                // ends at altPoints[n-1], guaranteeing no extremum is silently skipped.
                if (n - 2 >= k - 1) // at least k-1 inner points available
                {
                    int iterCount = 0;
                    TryCombinations(model, altPoints, 0, n - 1, k, results, fullSpan: true,
                        iterCount: ref iterCount, enforceEndpointExtrema: isLastLevel);
                }
            }

            results = results.OrderByDescending(x => x.Score).ToList();

            // Recursive sub-wave analysis: identify what sub-model fills each segment.
            // Only recurse when we have not yet reached the depth limit.
            if (depth + 1 < m_MaxMarkupDepth)
            {
                // FillSubWaveModels returns false when the node must be discarded:
                // at the last markup level a sub-wave position whose valid models are
                // all motive/zigzag must either (a) be identified as one of those models
                // or (b) pass the endpoint-extremum hard rule (§4.1-endpoint).
                // Model-diversity selection: guarantee each model type gets at
                // least MIN_CANDIDATES_PER_MODEL slots so that minority models
                // (e.g. ZIGZAG when IMPULSE dominates) are not completely evicted.
                results = SelectDiverseCandidates(results);
                results = results
                    .Where(node => FillSubWaveModels(node, points, depth + 1))
                    .ToList();

                // Re-sort after sub-wave filling: reward candidates whose sub-waves
                // were successfully identified deeper (§6.6 — depth coverage bonus).
                foreach (ExactParsedNode node in results)
                {
                    node.Score *= (1.0 + ComputeDepthCoverage(node) * DEPTH_COVERAGE_BONUS);

                    // §4.5-triangle-corrective-bonus: when a corrective sub-wave
                    // (b or x) is a triangle, the shallow price retracement is
                    // structurally justified — boost the candidate's score.
                    if (HasTriangleInCorrectivePosition(node))
                        node.Score *= TRIANGLE_CORRECTIVE_BONUS;
                }

                results = results.OrderByDescending(x => x.Score).ToList();
            }

            return results;
        }

        /// <summary>
        /// For each sub-wave segment in <paramref name="node"/>, collects the original
        /// input points that fall within the segment's bar range and recursively identifies
        /// the best matching sub-model.  Replaces the placeholder
        /// <see cref="ElliottModelType.SIMPLE_IMPULSE"/> node with the actual parsed result
        /// when a complete matching is found.
        /// </summary>
        /// <returns>
        /// <c>true</c> when the node remains structurally valid after sub-wave filling;
        /// <c>false</c> when the node must be discarded because at the last markup level
        /// a sub-wave position whose allowed models are all motive/zigzag
        /// could not be identified AND its segment endpoints fail the extremum check
        /// (§4.1-endpoint hard rule).
        /// </returns>
        private bool FillSubWaveModels(
            ExactParsedNode node, List<BarPoint> originalPoints, int depth)
        {
            if (node?.SubWaves == null) return true;

            bool isAtLastSubLevel = depth + 1 >= m_MaxMarkupDepth;
            bool anyRepaired      = false;

            for (int i = 0; i < node.WaveCount; i++)
            {
                ExactParsedNode sw = node.SubWaves[i];
                if (sw == null) continue;

                string waveKey = GetWaveKey(node.ModelType, i + 1);
                ElliottModelType[] validModels = GetValidSubModelsForWave(node.ModelType, waveKey);

                if (validModels.Length == 0) continue;

                int fromBar = sw.StartPoint.BarIndex;
                int toBar   = sw.EndPoint.BarIndex;

                // Collect original extremum points within this sub-wave's bar range.
                // originalPoints is sorted by BarIndex (alternating extrema in time order),
                // so binary search finds the slice in O(log n) instead of O(n).
                int lo = LowerBound(originalPoints, fromBar);
                int hi = UpperBound(originalPoints, toBar);
                var subPoints = originalPoints.GetRange(lo, hi - lo);

                // Ensure the exact segment endpoints are present
                if (subPoints.Count == 0 || subPoints[0].BarIndex != fromBar)
                    subPoints.Insert(0, sw.StartPoint);
                if (subPoints[^1].BarIndex != toBar)
                    subPoints.Add(sw.EndPoint);

                if (subPoints.Count < 2) continue;

                // §4.4-rediscovery: when the parent-level zigzag produced too few
                // alternating points in this sub-wave range for 5-wave models
                // (triangles, diagonals, impulses), re-run extremum finding with a
                // finer deviation to discover intermediate extrema.
                bool needs5Wave = subPoints.Count < MIN_POINTS_FOR_5_WAVE
                    && validModels.Any(m => GetExpectedWaves(m) == 5);
                List<BarPoint> finerPoints = null;
                if (needs5Wave && m_BarsProvider != null)
                {
                    bool isSwUp = sw.EndPoint.Value > sw.StartPoint.Value;
                    var finder = new SimpleExtremumFinder(
                        SUBWAVE_REDISCOVERY_DEVIATION, m_BarsProvider, !isSwUp);
                    finder.Calculate(fromBar, toBar);
                    finerPoints = finder.ToExtremaList()
                        .Where(p => p.BarIndex >= fromBar && p.BarIndex <= toBar)
                        .ToList();

                    if (finerPoints.All(p => p.BarIndex != fromBar))
                        finerPoints.Insert(0, sw.StartPoint);
                    if (finerPoints.All(p => p.BarIndex != toBar))
                        finerPoints.Add(sw.EndPoint);

                    // Skip RefineToCorridors: the extremum finder already produces
                    // bar-accurate H/L values; corridor refinement can distort the
                    // contracting structure needed by triangles.
                    finerPoints = ExtremumFinderBase.EndFixCorridors(finerPoints, m_BarsProvider);
                }

                // Parse with the original points first
                List<ExactParsedNode> subResults =
                    ParseInternal(subPoints, validModels, depth);

                // If finer points were found, parse those too and merge
                if (finerPoints != null && finerPoints.Count > subPoints.Count)
                {
                    List<ExactParsedNode> finerResults =
                        ParseInternal(finerPoints, validModels, depth);
                    subResults.AddRange(finerResults);
                }

                // §4.1-continuity (hard rule): the sub-model must span exactly the
                // sub-wave's bar range [fromBar, toBar] — no leading or trailing bars
                // may be left unaccounted for within the sub-wave.
                var validSubs = subResults
                    .Where(r => r.WaveCount == r.ExpectedWaves
                             && r.StartPoint.BarIndex == fromBar
                             && r.EndPoint.BarIndex == toBar)
                    .ToList();

                // §4.5-triangle-preference: in corrective wave positions (b, x)
                // triangles are preferred over other corrective models when found.
                // RUNNING is checked first: it requires B to overshoot the origin,
                // making it the more specific pattern. When both types match (RUNNING
                // on points where B overshoots, CONTRACTING on intermediates that
                // avoid the overshoot), RUNNING correctly captures the actual price
                // structure.
                ExactParsedNode best;
                bool isCorrective = waveKey == "b" || waveKey == "x";
                if (isCorrective)
                {
                    best = validSubs.FirstOrDefault(r =>
                        r.ModelType == ElliottModelType.TRIANGLE_RUNNING)
                        ?? validSubs.FirstOrDefault(r =>
                        r.ModelType == ElliottModelType.TRIANGLE_CONTRACTING)
                        ?? validSubs.FirstOrDefault();
                }
                else
                {
                    best = validSubs.FirstOrDefault();
                }
                if (best != null)
                {
                    node.SubWaves[i] = best;
                    continue;
                }

                // Sub-wave could not be identified — it stays as SIMPLE_IMPULSE.
                //
                // Hard rule: SIMPLE_IMPULSE is a simple move from min to max (or vice versa).
                // No candle within the segment may breach the start price, and the endpoint
                // must be the true OHLC extremum of the range in the wave's direction.
                //
                // Repair §4.2-endpoint-repair: when a breach is detected inside a bare
                // SIMPLE_IMPULSE, move the endpoint to the actual OHLC extremum so that
                // both the hard §4.1-endpoint rule and the Fibonacci score use the true price.
                if (m_BarsProvider != null)
                {
                    bool   isUp   = sw.IsUp;
                    double startPrice = sw.StartPoint.Value;
                    double bestVal = sw.EndPoint.Value;
                    int    bestBar = toBar;
                    bool   hasBreach = false;

                    // Start from fromBar + 1: the start bar belongs to the opposite
                    // extremum side (UP wave starts at a LOW, DOWN wave at a HIGH),
                    // so including it would risk placing the endpoint at the wrong
                    // side of the move.
                    for (int b = fromBar + 1; b <= toBar; b++)
                    {
                        if (b < 0 || b >= m_BarsProvider.Count) continue;

                        // Check candle breach: no candle in SIMPLE_IMPULSE may breach start
                        if (isUp && m_BarsProvider.GetLowPrice(b) < startPrice)
                        {
                            hasBreach = true;
                            break;
                        }
                        if (!isUp && m_BarsProvider.GetHighPrice(b) > startPrice)
                        {
                            hasBreach = true;
                            break;
                        }

                        // Track the true extremum for endpoint repair
                        double v = isUp
                            ? m_BarsProvider.GetHighPrice(b)
                            : m_BarsProvider.GetLowPrice(b);
                        if (isUp ? v > bestVal : v < bestVal)
                        {
                            bestVal = v;
                            bestBar = b;
                        }
                    }

                    if (hasBreach)
                        return false;

                    // Enforce direction endpoint: up wave must end at the High,
                    // down wave must end at the Low. If the current endpoint doesn't
                    // match, repair it to the true extremum.
                    if (bestBar != toBar)
                    {
                        // Guard: never collapse the next sub-wave to zero length.
                        bool collapse = i + 1 < node.WaveCount
                            && node.SubWaves[i + 1] != null
                            && bestBar >= node.SubWaves[i + 1].EndPoint.BarIndex;

                        if (!collapse)
                        {
                            var newEnd = new BarPoint(bestVal, bestBar, m_BarsProvider);
                            sw.EndPoint = newEnd;
                            sw.EndIndex = bestBar;

                            if (i + 1 < node.WaveCount && node.SubWaves[i + 1] != null)
                            {
                                node.SubWaves[i + 1].StartPoint = newEnd;
                                node.SubWaves[i + 1].StartIndex = bestBar;
                            }

                            anyRepaired = true;
                        }
                    }
                    else
                    {
                        // Endpoint bar is correct but verify direction match:
                        // up wave endpoint must equal the bar's High, down wave must equal Low.
                        int endIdx = sw.EndPoint.BarIndex;
                        if (endIdx >= 0 && endIdx < m_BarsProvider.Count)
                        {
                            double expected = isUp
                                ? m_BarsProvider.GetHighPrice(endIdx)
                                : m_BarsProvider.GetLowPrice(endIdx);
                            if (Math.Abs(sw.EndPoint.Value - expected) > expected * 1e-7)
                            {
                                var newEnd = new BarPoint(expected, endIdx, m_BarsProvider);
                                sw.EndPoint = newEnd;
                                sw.EndIndex = endIdx;

                                if (i + 1 < node.WaveCount && node.SubWaves[i + 1] != null)
                                {
                                    node.SubWaves[i + 1].StartPoint = newEnd;
                                    node.SubWaves[i + 1].StartIndex = endIdx;
                                }

                                anyRepaired = true;
                            }
                        }
                    }
                }

                // Hard rule §4.1-endpoint (last level, motive/zigzag positions only):
                // if ALL valid models for this position are motive/zigzag types the
                // segment endpoints MUST be the absolute price extrema of the segment.
                // After the repair above the endpoint is already the OHLC extremum, so
                // this check passes.  For unrepaired (no-breach) segments it is a safety
                // net that discards candidates whose zigzag pivot truly missed the peak.
                if (isAtLastSubLevel && validModels.All(AppliesToMotiveOrZigzag))
                {
                    var seg = new[] { new Segment { Start = sw.StartPoint, End = sw.EndPoint } };
                    if (!CheckEndpointExtremum(validModels[0], seg))
                        return false; // discard parent candidate
                }

            }

            // If any sub-wave endpoint was repaired, rebuild the parent's Fibonacci score
            // using the updated wave boundaries.  All candidates are full-span so the
            // FULL_FIT_BONUS is unconditionally re-applied.
            // After repair, re-validate diagonal hard rules because endpoint
            // shifts may have broken W3>W1 or contracting-length constraints.
            if (anyRepaired)
            {
                var segs = new Segment[node.WaveCount];
                for (int j = 0; j < node.WaveCount; j++)
                {
                    ExactParsedNode sw = node.SubWaves[j];
                    segs[j] = new Segment { Start = sw.StartPoint, End = sw.EndPoint };
                }

                if (!CheckHardRules(node.ModelType, segs))
                    return false;

                double repairedScore = CalculateFiboScore(node.ModelType, segs);
                if (repairedScore > 0)
                    node.Score = repairedScore * FULL_FIT_BONUS;
            }

            // §4.3-harmony: a sub-wave at depth N+1 must not be disproportionately
            // longer (in bars) than the shortest wave at depth N.
            // Contracting models (diagonals, triangles) are excluded because their
            // defining property — each successive wave shorter than the previous —
            // naturally creates large duration disparities between early and late waves.
            {
                bool contracting =
                    node.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL
                 || node.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_ENDING
                 || node.ModelType == ElliottModelType.TRIANGLE_CONTRACTING
                 || node.ModelType == ElliottModelType.TRIANGLE_RUNNING;

                if (!contracting)
                {
                    int minParentBars = int.MaxValue;
                    for (int i = 0; i < node.WaveCount; i++)
                    {
                        ExactParsedNode sw = node.SubWaves[i];
                        if (sw == null) continue;
                        int bars = Math.Abs(sw.EndPoint.BarIndex - sw.StartPoint.BarIndex);
                        if (bars > 0 && bars < minParentBars) minParentBars = bars;
                    }

                    if (minParentBars < int.MaxValue)
                    {
                        double maxRatio = 0;
                        for (int i = 0; i < node.WaveCount; i++)
                        {
                            ExactParsedNode sw = node.SubWaves[i];
                            if (sw?.SubWaves == null
                                || sw.ModelType == ElliottModelType.SIMPLE_IMPULSE)
                                continue;

                            // Skip harmony check for triangle sub-waves: their internal
                            // waves naturally have large duration disparities.
                            if (sw.ModelType == ElliottModelType.TRIANGLE_CONTRACTING
                             || sw.ModelType == ElliottModelType.TRIANGLE_RUNNING)
                                continue;

                            for (int j = 0; j < sw.WaveCount && j < sw.SubWaves.Length; j++)
                            {
                                ExactParsedNode child = sw.SubWaves[j];
                                if (child == null) continue;
                                int childBars = Math.Abs(
                                    child.EndPoint.BarIndex - child.StartPoint.BarIndex);
                                double ratio = (double)childBars / minParentBars;
                                if (ratio > HARMONY_HARD_RATIO) return false;
                                if (ratio > maxRatio) maxRatio = ratio;
                            }
                        }

                        if (maxRatio > HARMONY_PESSIMIZE_RATIO)
                            node.Score *= HARMONY_PENALTY;
                    }
                }
            }

            return true;
        }
        /// 1.0 = every sub-wave was successfully identified as a named model.
        /// Used to compute the depth-coverage bonus applied in <see cref="ParseInternal"/>.
        /// <para>
        /// NOTE: A recursive formulation was tried but always returned 0 because every
        /// branch eventually bottoms out at SIMPLE_IMPULSE leaves (which contribute 0).
        /// The non-recursive direct-fraction approach gives meaningful [0,1] scores and
        /// correctly rewards models whose immediate sub-waves are all identified, regardless
        /// of how deeply the recursion went.
        /// </para>
        /// </summary>
        private static double ComputeDepthCoverage(ExactParsedNode node)
        {
            if (node?.SubWaves == null || node.WaveCount == 0 || node.SubWaves.Length == 0)
                return 0.0;
            int identified = 0;
            for (int i = 0; i < node.WaveCount && i < node.SubWaves.Length; i++)
            {
                ExactParsedNode sw = node.SubWaves[i];
                if (sw != null && sw.ModelType != ElliottModelType.SIMPLE_IMPULSE)
                    identified++;
            }
            return (double)identified / node.WaveCount;
        }

        /// <summary>
        /// Returns <c>true</c> when a corrective sub-wave (wave b or x) of the given
        /// node has been identified as a triangle (contracting or running).
        /// </summary>
        private static bool HasTriangleInCorrectivePosition(ExactParsedNode node)
        {
            if (node?.SubWaves == null) return false;
            for (int i = 0; i < node.WaveCount && i < node.SubWaves.Length; i++)
            {
                ExactParsedNode sw = node.SubWaves[i];
                if (sw == null) continue;
                string key = GetWaveKey(node.ModelType, i + 1);
                if (key != "b" && key != "x") continue;
                if (sw.ModelType == ElliottModelType.TRIANGLE_CONTRACTING ||
                    sw.ModelType == ElliottModelType.TRIANGLE_RUNNING)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns <c>true</c> when a corrective sub-wave (b or x) of the given node
        /// spans at least <paramref name="threshold"/> (0–1) of the pattern's total
        /// bar range.  Used by <see cref="SelectDiverseCandidates"/> to protect
        /// candidates with long corrective waves from early elimination.
        /// </summary>
        private static bool HasSignificantCorrectiveSpan(
            ExactParsedNode node, double threshold)
        {
            if (node?.SubWaves == null) return false;
            int totalBars = Math.Abs(
                node.EndPoint.BarIndex - node.StartPoint.BarIndex);
            if (totalBars <= 0) return false;

            for (int i = 0; i < node.WaveCount && i < node.SubWaves.Length; i++)
            {
                string key = GetWaveKey(node.ModelType, i + 1);
                if (key != "b" && key != "x") continue;
                ExactParsedNode sw = node.SubWaves[i];
                if (sw == null) continue;
                int swBars = Math.Abs(
                    sw.EndPoint.BarIndex - sw.StartPoint.BarIndex);
                if ((double)swBars / totalBars >= threshold)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="model"/> belongs to the set of motive/zigzag
        /// patterns for which the endpoint-extremum rule (§4.1-endpoint) applies:
        /// IMPULSE, DIAGONAL_CONTRACTING_INITIAL, DIAGONAL_CONTRACTING_ENDING, ZIGZAG,
        /// DOUBLE_ZIGZAG.
        /// </summary>
        private static bool AppliesToMotiveOrZigzag(ElliottModelType model) =>
            model == ElliottModelType.IMPULSE
            || model == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL
            || model == ElliottModelType.DIAGONAL_CONTRACTING_ENDING
            || model == ElliottModelType.ZIGZAG
            || model == ElliottModelType.DOUBLE_ZIGZAG;

        /// <summary>
        /// Hard rule §4.1-endpoint — at the last markup level, for motive and zigzag patterns
        /// the endpoint of the full pattern must be the actual price extremum within
        /// the pattern's bar range.
        /// <list type="bullet">
        /// <item>Upward pattern: no bar <c>High</c> within [start…end] may exceed the end price
        ///       (the end price equals the highest High across all bars).</item>
        /// <item>Downward pattern: no bar <c>Low</c> within [start…end] may fall below the end price.</item>
        /// </list>
        /// The start-side extremum is already enforced by <see cref="CheckCandleBreachIncremental"/>.
        /// Returns <c>true</c> when no <see cref="IBarsProvider"/> was supplied
        /// (check skipped) or when the model is not a motive/zigzag type.
        /// </summary>
        private bool CheckEndpointExtremum(ElliottModelType model, Segment[] waves)
        {
            if (m_BarsProvider == null) return true;
            if (!AppliesToMotiveOrZigzag(model)) return true;

            bool isUp     = waves[0].IsUp;
            double endPrice = waves[^1].End.Value;
            int from = waves[0].Start.BarIndex;
            int to   = waves[^1].End.BarIndex;

            // Start from from + 1: the start bar belongs to the opposite extremum
            // side (an UP wave starts at a Low, a DOWN wave at a High), so its
            // opposite-side OHLC price should not count against the endpoint check.
            // This aligns with the §4.2-endpoint-repair scan range.
            for (int i = from + 1; i <= to; i++)
            {
                if (i < 0 || i >= m_BarsProvider.Count) continue;
                if (isUp  && m_BarsProvider.GetHighPrice(i) > endPrice) return false;
                if (!isUp && m_BarsProvider.GetLowPrice(i)  < endPrice) return false;
            }
            return true;
        }

        /// <summary>
        /// Returns the allowed sub-model types for a specific wave position within a parent model,
        /// as defined in <see cref="ElliottWavePatternHelper.ModelRules"/>.
        /// Returns an empty array when the parent model or wave key is not found.
        /// </summary>
        private static ElliottModelType[] GetValidSubModelsForWave(
            ElliottModelType parentModel, string waveKey)
        {
            return VALID_SUB_MODELS_CACHE.TryGetValue((parentModel, waveKey), out var models)
                ? models
                : Array.Empty<ElliottModelType>();
        }

        /// <summary>
        /// Selects up to <see cref="MAX_CANDIDATES_FOR_SUBWAVE_FILL"/> candidates while
        /// guaranteeing that each model type is represented by at least
        /// <see cref="MIN_CANDIDATES_PER_MODEL"/> entries (when available).
        /// Without this, a model type that generates O(n^4) combinations (IMPULSE)
        /// can fill all top slots and evict structurally correct 3-wave candidates
        /// (ZIGZAG, DOUBLE_ZIGZAG) from the sub-wave filling stage.
        /// </summary>
        private static List<ExactParsedNode> SelectDiverseCandidates(
            List<ExactParsedNode> sortedResults)
        {
            if (sortedResults.Count <= MAX_CANDIDATES_FOR_SUBWAVE_FILL)
                return sortedResults;

            var selected = new List<ExactParsedNode>();
            var usedSet  = new HashSet<ExactParsedNode>(ReferenceEqualityComparer.Instance);

            // Phase 1: guarantee MIN_CANDIDATES_PER_MODEL per model type
            foreach (var group in sortedResults.GroupBy(r => r.ModelType))
            {
                foreach (ExactParsedNode node in group.Take(MIN_CANDIDATES_PER_MODEL))
                {
                    selected.Add(node);
                    usedSet.Add(node);
                }
            }

            // Phase 1.5: protect candidates whose corrective wave (b or x) spans
            // a large fraction of the total bar range.  Such candidates often host
            // a triangle or other complex corrective structure that justifies the
            // seemingly shallow price retracement.  Without this, they are evicted
            // by candidates with better top-level Fibonacci scores before sub-wave
            // analysis can discover the triangle.
            const double MIN_CORRECTIVE_BAR_FRACTION = 0.25;
            foreach (ExactParsedNode node in sortedResults)
            {
                if (selected.Count >= MAX_CANDIDATES_FOR_SUBWAVE_FILL) break;
                if (usedSet.Contains(node)) continue;
                if (!HasSignificantCorrectiveSpan(node, MIN_CORRECTIVE_BAR_FRACTION)) continue;
                selected.Add(node);
                usedSet.Add(node);
            }

            // Phase 2: fill remaining slots with the overall best-scoring candidates
            int remaining = MAX_CANDIDATES_FOR_SUBWAVE_FILL - selected.Count;
            if (remaining > 0)
            {
                foreach (ExactParsedNode node in sortedResults)
                {
                    if (remaining <= 0) break;
                    if (usedSet.Contains(node)) continue;
                    selected.Add(node);
                    remaining--;
                }
            }

            // Return in score-descending order
            return selected.OrderByDescending(x => x.Score).ToList();
        }

        /// <summary>Returns the index of the first element whose BarIndex ≥ <paramref name="barIndex"/>.</summary>
        private static int LowerBound(List<BarPoint> points, int barIndex)
        {
            int lo = 0, hi = points.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (points[mid].BarIndex < barIndex) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>Returns the index of the first element whose BarIndex &gt; <paramref name="barIndex"/>.</summary>
        private static int UpperBound(List<BarPoint> points, int barIndex)
        {
            int lo = 0, hi = points.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (points[mid].BarIndex <= barIndex) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Tries all combinations of K-1 intermediate points from altPoints[innerStart..innerEnd-1]
        /// with fixed start=altPoints[outerStart] and end=altPoints[outerEnd].
        /// Waves are built one at a time in <see cref="TryCombinationsRecurse"/>; alternation
        /// and <see cref="CheckIncrementalHardRules"/> prune invalid branches before all K waves
        /// are chosen, reducing the effective search space vs the flat C(n,k-1) enumeration.
        /// Adds valid candidates to <paramref name="results"/>.
        /// </summary>
        private void TryCombinations(
            ElliottModelType model,
            List<BarPoint> altPoints,
            int outerStart, int outerEnd,
            int k,
            List<ExactParsedNode> results,
            bool fullSpan,
            ref int iterCount,
            bool enforceEndpointExtrema = false)
        {
            if (outerEnd - outerStart - 1 < k - 1) return; // not enough inner points

            // Allocate shared arrays once; reused across all recursive branches.
            BarPoint[] pts   = new BarPoint[k + 1];
            Segment[]  waves = new Segment[k];
            pts[0] = altPoints[outerStart];
            pts[k] = altPoints[outerEnd]; // last point is fixed; pre-set for the base case

            TryCombinationsRecurse(
                model, altPoints, outerStart, outerEnd, k, 0,
                pts, waves, results, fullSpan, enforceEndpointExtrema,
                ref iterCount);
        }

        /// <summary>
        /// Recursive core of <see cref="TryCombinations"/>.
        /// Builds wave <paramref name="waveIdx"/> (0-based), then either recurses for the
        /// next wave or (at the last wave) validates the complete candidate.
        /// </summary>
        private void TryCombinationsRecurse(
            ElliottModelType model,
            List<BarPoint> altPoints,
            int prevAltIdx, int outerEnd,
            int k, int waveIdx,
            BarPoint[] pts, Segment[] waves,
            List<ExactParsedNode> results,
            bool fullSpan, bool enforceEndpointExtrema,
            ref int iterCount)
        {
            if (waveIdx == k - 1)
            {
                // Last wave always ends at outerEnd (already in pts[k]).
                waves[waveIdx] = new Segment { Start = pts[waveIdx], End = pts[k] };

                // Every wave must span at least 2 bars.
                if (waves[waveIdx].Start.BarIndex == waves[waveIdx].End.BarIndex) return;

                // Alternation check for the last wave only (previous pairs checked incrementally).
                if (waveIdx > 0 && waves[waveIdx].IsUp == waves[waveIdx - 1].IsUp) return;

                // Full validation: remaining hard rules, candle checks, score.
                if (!CheckHardRules(model, waves)) return;
                // Direction endpoint check — skip for triangles (internal waves
                // often don't reach exact bar H/L in tight corrective ranges).
                bool isTriangle = model == ElliottModelType.TRIANGLE_CONTRACTING ||
                                  model == ElliottModelType.TRIANGLE_RUNNING;
                if (!isTriangle && !CheckDirectionEndpoints(waves)) return;
                // Candle-breach check for the last wave (all preceding waves were
                // already checked incrementally in the loop below).
                if (!CheckCandleBreachIncremental(model, waves, waveIdx)) return;
                if (enforceEndpointExtrema && !CheckEndpointExtremum(model, waves)) return;

                double score = CalculateFiboScore(model, waves);
                if (score > 0)
                {
                    if (fullSpan) score *= FULL_FIT_BONUS;
                    results.Add(BuildNode(model, waves, score, k));
                }
                return;
            }

            // Choose endpoint for wave `waveIdx`; must leave room for remaining waves.
            int remaining = k - 1 - waveIdx; // waves still to place after this one
            int hi = outerEnd - remaining;

            for (int j = prevAltIdx + 1; j <= hi; j++)
            {
                if (iterCount >= MAX_ITERATIONS_PER_MODEL) return;
                iterCount++;

                pts[waveIdx + 1] = altPoints[j];
                waves[waveIdx]   = new Segment { Start = pts[waveIdx], End = pts[waveIdx + 1] };

                // Every wave must span at least 2 bars.
                if (waves[waveIdx].Start.BarIndex == waves[waveIdx].End.BarIndex) continue;

                // Early alternation check for the wave just built.
                if (waveIdx > 0 && waves[waveIdx].IsUp == waves[waveIdx - 1].IsUp) continue;

                // Incremental hard-rule pruning — eliminates branches that already
                // violate a structural rule before the remaining waves are chosen.
                if (!CheckIncrementalHardRules(model, waves, waveIdx)) continue;

                // Incremental candle-breach pruning: reject any combination whose
                // most recently placed wave already violates the start-price rule.
                // This prunes the subtree before any further waves are chosen.
                if (!CheckCandleBreachIncremental(model, waves, waveIdx)) continue;

                TryCombinationsRecurse(
                    model, altPoints, j, outerEnd, k, waveIdx + 1,
                    pts, waves, results, fullSpan, enforceEndpointExtrema,
                    ref iterCount);
            }
        }

        /// <summary>
        /// Hard rule §4.1 — direction endpoint validation.
        /// For a descending wave the endpoint must be the LOW of its bar;
        /// for an ascending wave it must be the HIGH.
        /// Returns <c>true</c> when no <see cref="IBarsProvider"/> was supplied
        /// (check is skipped) or when every wave endpoint passes the validation.
        /// </summary>
        private bool CheckDirectionEndpoints(Segment[] waves)
        {
            if (m_BarsProvider == null) return true;
            foreach (Segment w in waves)
            {
                int idx = w.End.BarIndex;
                if (idx < 0 || idx >= m_BarsProvider.Count) continue;
                if (w.IsUp)
                {
                    // Ascending wave — end must be the bar HIGH
                    double barHigh = m_BarsProvider.GetHighPrice(idx);
                    if (w.End.Value < barHigh * (1.0 - 1e-7)) return false;
                }
                else
                {
                    // Descending wave — end must be the bar LOW
                    double barLow = m_BarsProvider.GetLowPrice(idx);
                    if (w.End.Value > barLow * (1.0 + 1e-7)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Hard rule §4.1 — candle-level breach check.
        /// Within the following patterns no individual candle bar may pierce the pattern
        /// start price:
        /// <list type="bullet">
        /// <item>IMPULSE: no candle in any of waves 1–5 goes below the impulse start
        ///       (upward) or above it (downward).</item>
        /// <item>DIAGONAL (contracting): same constraint for all five waves.</item>
        /// <item>Deep corrections — ZIGZAG, DOUBLE_ZIGZAG: same — no candle in any
        ///       wave breaches the pattern start price.</item>
        /// </list>
        /// Skipped when no <see cref="IBarsProvider"/> was supplied.
        /// </summary>
        /// <remarks>
        /// Called once per wave as it is placed in
        /// <see cref="TryCombinationsRecurse"/>, so invalid branches are pruned
        /// before any remaining waves are chosen.  The full-pattern check is
        /// therefore redundant and has been replaced by this per-wave version.
        /// </remarks>
        private bool CheckCandleBreachIncremental(
            ElliottModelType model, Segment[] waves, int waveIdx)
        {
            if (m_BarsProvider == null) return true;

            bool appliesToModel =
                model == ElliottModelType.IMPULSE
                || model == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL
                || model == ElliottModelType.DIAGONAL_CONTRACTING_ENDING
                || model == ElliottModelType.ZIGZAG
                || model == ElliottModelType.DOUBLE_ZIGZAG;

            if (!appliesToModel) return true;

            bool isUp = waves[0].IsUp;
            double startPrice = waves[0].Start.Value;
            Segment wave = waves[waveIdx];
            int from = Math.Min(wave.Start.BarIndex, wave.End.BarIndex);
            int to   = Math.Max(wave.Start.BarIndex, wave.End.BarIndex);

            for (int i = from; i <= to; i++)
            {
                if (i < 0 || i >= m_BarsProvider.Count) continue;
                if (isUp)
                {
                    if (m_BarsProvider.GetLowPrice(i) < startPrice) return false;
                }
                else
                {
                    if (m_BarsProvider.GetHighPrice(i) > startPrice) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks hard-rule conditions that involve only the most recently added wave
        /// (<paramref name="waveIdx"/>, 0-based).  Called incrementally inside
        /// <see cref="TryCombinationsRecurse"/> so that invalid partial candidates are
        /// pruned before any remaining waves are chosen.
        /// Conditions that require the complete wave array (e.g. "wave 3 not shortest")
        /// are still handled by the final <see cref="CheckHardRules"/> call.
        /// </summary>
        private static bool CheckIncrementalHardRules(
            ElliottModelType model, Segment[] w, int waveIdx)
        {
            bool isUp  = w[0].IsUp;
            double start = w[0].Start.Value;

            switch (model)
            {
                case ElliottModelType.IMPULSE:
                    switch (waveIdx)
                    {
                        case 1: // wave 2 placed
                            if ( isUp && w[1].End.Value <= start) return false;
                            if (!isUp && w[1].End.Value >= start) return false;
                            break;
                        case 2: // wave 3 placed
                            if ( isUp && w[2].End.Value <= w[0].End.Value) return false;
                            if (!isUp && w[2].End.Value >= w[0].End.Value) return false;
                            break;
                        case 3: // wave 4 placed
                            if ( isUp && w[3].End.Value <= start)          return false;
                            if (!isUp && w[3].End.Value >= start)          return false;
                            if ( isUp && w[3].End.Value <= w[0].End.Value) return false; // no overlap
                            if (!isUp && w[3].End.Value >= w[0].End.Value) return false;
                            {
                                int w2Bars = Math.Abs(w[1].End.BarIndex - w[1].Start.BarIndex);
                                int w4Bars = Math.Abs(w[3].End.BarIndex - w[3].Start.BarIndex);
                                if (w2Bars > 0 && w4Bars > 3 * w2Bars) return false;
                            }
                            break;
                    }
                    return true;

                case ElliottModelType.DIAGONAL_CONTRACTING_INITIAL:
                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                    switch (waveIdx)
                    {
                        case 1:
                            if ( isUp && w[1].End.Value <= start) return false;
                            if (!isUp && w[1].End.Value >= start) return false;
                            break;
                        case 2:
                        {
                            double pen = MIN_DIAGONAL_PENETRATION * w[0].Length;
                            if ( isUp && w[2].End.Value < w[0].End.Value + pen) return false;
                            if (!isUp && w[2].End.Value > w[0].End.Value - pen) return false;
                            if (w[2].Length >= w[0].Length) return false; // contracting
                        }
                            break;
                        case 3:
                            if ( isUp && w[3].End.Value >= w[0].End.Value) return false; // overlap
                            if (!isUp && w[3].End.Value <= w[0].End.Value) return false;
                            if (w[3].Length >= w[1].Length) return false; // contracting
                            break;
                    }
                    return true;

                case ElliottModelType.ZIGZAG:
                case ElliottModelType.DOUBLE_ZIGZAG:
                    if (waveIdx == 1)
                    {
                        if ( isUp && w[1].End.Value <= start) return false;
                        if (!isUp && w[1].End.Value >= start) return false;
                        if (w[1].Length > w[0].Length * 0.95)  return false;
                    }
                    return true;

                case ElliottModelType.FLAT_EXTENDED:
                case ElliottModelType.FLAT_RUNNING:
                    if (waveIdx == 1)
                    {
                        if ( isUp && w[1].End.Value >= start) return false; // B overshoots below start
                        if (!isUp && w[1].End.Value <= start) return false;
                    }
                    return true;

                case ElliottModelType.FLAT_REGULAR:
                    if (waveIdx == 1)
                    {
                        // Regular flat: B stays near origin, does NOT overshoot.
                        if ( isUp && w[1].End.Value < start) return false;
                        if (!isUp && w[1].End.Value > start) return false;
                    }
                    return true;

                case ElliottModelType.TRIANGLE_CONTRACTING:
                    // Same-direction convergence: C < A (waveIdx 2 vs 0), E < C (4 vs 2), D < B (3 vs 1)
                    if (waveIdx >= 2 && w[waveIdx].Length >= w[waveIdx - 2].Length) return false;
                    // B must NOT overshoot origin (that would be a running triangle).
                    if (waveIdx >= 1)
                    {
                        if ( isUp && w[1].End.Value < start) return false;
                        if (!isUp && w[1].End.Value > start) return false;
                    }
                    return true;

                case ElliottModelType.TRIANGLE_RUNNING:
                    if (waveIdx == 1)
                    {
                        if ( isUp && w[1].End.Value >= start) return false;
                        if (!isUp && w[1].End.Value <= start) return false;
                    }
                    // Running triangles have an oversized B wave, so C can be
                    // larger than A.  Only require corrective convergence (D < B)
                    // and motive convergence after B (E < C).  C < A is NOT
                    // enforced — the overshoot resets the motive reference.
                    if (waveIdx == 3 && w[3].Length >= w[1].Length) return false; // D < B
                    if (waveIdx == 4 && w[4].Length >= w[2].Length) return false; // E < C
                    return true;

                default:
                    return true;
            }
        }

        private static bool CheckHardRules(ElliottModelType model, Segment[] w)
        {
            if (w.Length == 0) return false;
            bool isUp = w[0].IsUp;
            double start = w[0].Start.Value;

            switch (model)
            {
                case ElliottModelType.IMPULSE:
                    if (w.Length < 5) return false;
                    {
                        double w1End = w[0].End.Value;
                        double w3End = w[2].End.Value;
                        double w4End = w[3].End.Value;
                        double w5End = w[4].End.Value;

                        if (isUp && w[1].End.Value <= start) return false;
                        if (!isUp && w[1].End.Value >= start) return false;

                        if (isUp && w3End <= w1End) return false;
                        if (!isUp && w3End >= w1End) return false;

                        if (isUp && w4End <= start) return false;
                        if (!isUp && w4End >= start) return false;
                        if (isUp && w4End <= w1End) return false;
                        if (!isUp && w4End >= w1End) return false;

                        if (w[2].Length < w[0].Length && w[2].Length < w[4].Length) return false;

                        if (isUp && w5End <= w4End) return false;
                        if (!isUp && w5End >= w4End) return false;

                        // W4 duration cannot exceed 3× W2 duration
                        {
                            int w2Bars = Math.Abs(w[1].End.BarIndex - w[1].Start.BarIndex);
                            int w4Bars = Math.Abs(w[3].End.BarIndex - w[3].Start.BarIndex);
                            if (w2Bars > 0 && w4Bars > 3 * w2Bars) return false;
                        }
                    }
                    return true;

                case ElliottModelType.DIAGONAL_CONTRACTING_INITIAL:
                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                    if (w.Length < 5) return false;
                    {
                        double w1End = w[0].End.Value;
                        double w3End = w[2].End.Value;
                        double w4End = w[3].End.Value;

                        if (isUp && w[1].End.Value <= start) return false;
                        if (!isUp && w[1].End.Value >= start) return false;

                        double diagPen = MIN_DIAGONAL_PENETRATION * w[0].Length;
                        if (isUp && w3End < w1End + diagPen) return false;
                        if (!isUp && w3End > w1End - diagPen) return false;

                        // W4 overlaps W1 territory
                        if (isUp && w4End >= w1End) return false;
                        if (!isUp && w4End <= w1End) return false;

                        // Contracting: each same-dir wave < previous
                        if (w[2].Length >= w[0].Length) return false;
                        if (w[4].Length >= w[2].Length) return false;
                        if (w[3].Length >= w[1].Length) return false;

                        if (isUp && w[4].End.Value <= w4End) return false;
                        if (!isUp && w[4].End.Value >= w4End) return false;

                        // Initial diagonal: W5 must exceed W3 end
                        if (model == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL)
                        {
                            double pen5 = MIN_DIAGONAL_PENETRATION * w[2].Length;
                            if (isUp && w[4].End.Value < w3End + pen5) return false;
                            if (!isUp && w[4].End.Value > w3End - pen5) return false;
                        }
                    }
                    return true;

                case ElliottModelType.ZIGZAG:
                    if (w.Length < 3) return false;
                    {
                        double wALen = w[0].Length;
                        double wBLen = w[1].Length;
                        double wCLen = w[2].Length;
                        double wAEnd = w[0].End.Value;

                        if (wBLen > wALen * 0.95) return false;
                        if (wCLen < wALen * 0.618) return false;

                        if (isUp && w[1].End.Value <= start) return false;
                        if (!isUp && w[1].End.Value >= start) return false;

                        if (isUp && w[2].End.Value <= wAEnd) return false;
                        if (!isUp && w[2].End.Value >= wAEnd) return false;
                    }
                    return true;

                case ElliottModelType.DOUBLE_ZIGZAG:
                    if (w.Length < 3) return false;
                    {
                        double wWLen = w[0].Length;
                        double wXLen = w[1].Length;
                        double wYLen = w[2].Length;
                        double wWEnd = w[0].End.Value;

                        if (wXLen > wWLen * 0.95) return false;
                        if (wYLen < wWLen * 0.618) return false;

                        if (isUp && w[1].End.Value <= start) return false;
                        if (!isUp && w[1].End.Value >= start) return false;

                        if (isUp && w[2].End.Value <= wWEnd) return false;
                        if (!isUp && w[2].End.Value >= wWEnd) return false;
                    }
                    return true;

                case ElliottModelType.FLAT_EXTENDED:
                    if (w.Length < 3) return false;
                    {
                        double wAEnd = w[0].End.Value;

                        // B must overshoot the pattern origin (opposite side from A's start).
                        // For up A: A goes up, B retraces and goes BELOW start.
                        // For down A: A goes down, B retraces and goes ABOVE start.
                        // This is exactly opposite to ZIGZAG's B rule (ZIGZAG B stays within start).
                        if (isUp && w[1].End.Value >= start) return false;
                        if (!isUp && w[1].End.Value <= start) return false;

                        // C extends beyond A's price territory (the defining feature of "extended").
                        if (isUp && w[2].End.Value <= wAEnd) return false;
                        if (!isUp && w[2].End.Value >= wAEnd) return false;
                    }
                    return true;

                case ElliottModelType.FLAT_RUNNING:
                    if (w.Length < 3) return false;
                    {
                        double wAEnd = w[0].End.Value;

                        // B must overshoot the pattern origin, same as extended flat.
                        if (isUp && w[1].End.Value >= start) return false;
                        if (!isUp && w[1].End.Value <= start) return false;

                        // C does NOT reach A's end territory (short of A — the "running" property).
                        if (isUp && w[2].End.Value >= wAEnd) return false;
                        if (!isUp && w[2].End.Value <= wAEnd) return false;
                    }
                    return true;

                case ElliottModelType.FLAT_REGULAR:
                    if (w.Length < 3) return false;
                    {
                        double wAEnd = w[0].End.Value;

                        // Regular flat: B retraces 90–100% of A, stays near origin
                        // but does NOT overshoot it (opposite of extended/running).
                        if (isUp && w[1].End.Value < start) return false;
                        if (!isUp && w[1].End.Value > start) return false;

                        // C extends beyond A's end (similar to extended flat).
                        if (isUp && w[2].End.Value <= wAEnd) return false;
                        if (!isUp && w[2].End.Value >= wAEnd) return false;
                    }
                    return true;

                case ElliottModelType.TRIANGLE_CONTRACTING:
                    if (w.Length < 5) return false;
                    {
                        // B must NOT overshoot the origin (that would be a running triangle).
                        if (isUp && w[1].End.Value < start) return false;
                        if (!isUp && w[1].End.Value > start) return false;

                        // Convergence rule: same-direction waves must contract.
                        // Motive waves: A > C > E (waves 0, 2, 4)
                        // Corrective waves: B > D (waves 1, 3)
                        if (w[2].Length >= w[0].Length) return false;
                        if (w[4].Length >= w[2].Length) return false;
                        if (w[3].Length >= w[1].Length) return false;

                        // E must remain on the triangle side of the start (not break out the wrong way).
                        double eEnd = w[4].End.Value;
                        if (isUp && eEnd <= start) return false;
                        if (!isUp && eEnd >= start) return false;
                    }
                    return true;

                case ElliottModelType.TRIANGLE_RUNNING:
                    if (w.Length < 5) return false;
                    {
                        // B overshoots the triangle origin (the "running" property).
                        if (isUp && w[1].End.Value >= start) return false;
                        if (!isUp && w[1].End.Value <= start) return false;

                        // Running triangle convergence: B is oversized (overshoots
                        // origin), so C can exceed A.  Require:
                        //   D < B  (corrective waves converge)
                        //   E < C  (motive waves converge after the overshoot)
                        if (w[3].Length >= w[1].Length) return false; // D < B
                        if (w[4].Length >= w[2].Length) return false; // E < C

                        // Same E constraint as contracting: just stay on triangle side of start.
                        double eEnd = w[4].End.Value;
                        if (isUp && eEnd <= start) return false;
                        if (!isUp && eEnd >= start) return false;
                    }
                    return true;

                default:
                    return false;
            }
        }

        private ExactParsedNode BuildNode(
            ElliottModelType model, Segment[] waves, double score, int waveCount)
        {
            int expected = GetExpectedWaves(model);
            var subWaves = new ExactParsedNode[expected];

            for (int i = 0; i < waveCount; i++)
            {
                subWaves[i] = new ExactParsedNode
                {
                    ModelType = ElliottModelType.SIMPLE_IMPULSE,
                    WaveCount = 1,
                    ExpectedWaves = 1,
                    StartIndex = waves[i].Start.BarIndex,
                    EndIndex = waves[i].End.BarIndex,
                    StartPoint = waves[i].Start,
                    EndPoint = waves[i].End,
                    IsUp = waves[i].IsUp,
                    Score = 1.0,
                    SubWaves = Array.Empty<ExactParsedNode>()
                };
            }

            return new ExactParsedNode
            {
                ModelType = model,
                WaveCount = waveCount,
                ExpectedWaves = expected,
                StartIndex = waves[0].Start.BarIndex,
                EndIndex = waves[waveCount - 1].End.BarIndex,
                StartPoint = waves[0].Start,
                EndPoint = waves[waveCount - 1].End,
                IsUp = waves[0].IsUp,
                Score = score,
                SubWaves = subWaves
            };
        }

        /// <summary>
        /// Calculates Fibonacci price projections for missing sub-waves in an incomplete wave node.
        /// </summary>
        public List<(double Value, int BarIndex, string Name)> GetProjections(ExactParsedNode node)
        {
            var projections = new List<(double Value, int BarIndex, string Name)>();
            if (node == null || node.WaveCount >= node.ExpectedWaves) return projections;

            double[] waveLengths = new double[node.ExpectedWaves];
            int[] waveTimeLengths = new int[node.ExpectedWaves];

            for (int i = 0; i < node.WaveCount; i++)
            {
                waveLengths[i] = node.SubWaves[i].Length;
                waveTimeLengths[i] = Math.Max(1,
                    node.SubWaves[i].EndIndex - node.SubWaves[i].StartIndex);
            }

            int avgTimeLen = (int)waveTimeLengths.Take(node.WaveCount).Average();
            double currentValue = node.EndPoint.Value;
            int currentIndex = node.EndPoint.BarIndex;
            bool nextIsUp = !node.SubWaves[node.WaveCount - 1].IsUp;

            for (int w = node.WaveCount + 1; w <= node.ExpectedWaves; w++)
            {
                double projLen = GetProjectedLength(node.ModelType, w, waveLengths);
                if (projLen == 0) projLen = waveLengths[0] * 0.5;

                double nextValue = nextIsUp ? currentValue + projLen : currentValue - projLen;
                int nextIndex = currentIndex + avgTimeLen;
                string name = GetWaveKey(node.ModelType, w);

                projections.Add((nextValue, nextIndex, name));
                waveLengths[w - 1] = projLen;
                currentValue = nextValue;
                currentIndex = nextIndex;
                nextIsUp = !nextIsUp;
            }

            return projections;
        }

        private static double GetProjectedLength(ElliottModelType model, int w, double[] lengths)
        {
            switch (model)
            {
                case ElliottModelType.IMPULSE:
                    if (w == 2) return lengths[0] * 0.618;
                    if (w == 3) return lengths[0] * 1.618;
                    if (w == 4) return lengths[2] * 0.382;
                    if (w == 5) return lengths[0] * 1.0;
                    break;
                case ElliottModelType.ZIGZAG:
                case ElliottModelType.DOUBLE_ZIGZAG:
                    if (w == 2) return lengths[0] * 0.618;
                    if (w == 3) return lengths[0] * 1.0;
                    break;
                case ElliottModelType.FLAT_EXTENDED:
                    if (w == 2) return lengths[0] * 1.272;
                    if (w == 3) return lengths[0] * 1.618;
                    break;
                case ElliottModelType.FLAT_RUNNING:
                    if (w == 2) return lengths[0] * 1.272;
                    if (w == 3) return lengths[0] * 1.0;
                    break;
                case ElliottModelType.TRIANGLE_CONTRACTING:
                    if (w >= 2) return lengths[w - 2] * 0.618;
                    break;
                default:
                    if (w >= 2) return lengths[0] * 0.618;
                    break;
            }
            return 0;
        }
    }
}
