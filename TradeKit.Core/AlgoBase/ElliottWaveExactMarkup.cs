using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

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
        /// Maximum score multiplier applied to a candidate when all of its sub-waves
        /// (and their sub-waves, recursively) have been successfully identified.
        /// A value of 0.5 means a fully-identified markup can earn up to 1.5× its
        /// raw Fibonacci score, giving it priority over structurally equivalent
        /// candidates whose sub-waves remain as <see cref="ElliottModelType.SIMPLE_IMPULSE"/>.
        /// </summary>
        private const double DEPTH_COVERAGE_BONUS = 0.5;

        /// <summary>
        /// Maximum recursion depth for sub-wave model identification.
        /// Depth 0 = top-level pattern; depth 1 = first-level sub-waves.
        /// At depth MAX_MARKUP_DEPTH the recursion stops — sub-waves at that
        /// level are left as SIMPLE_IMPULSE segments.
        /// </summary>
        public const int MAX_MARKUP_DEPTH = 10;

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
        /// Initialises the markup engine.
        /// </summary>
        /// <param name="barsProvider">
        /// Optional.  When supplied every wave endpoint is validated against the
        /// actual High/Low price of the corresponding bar (hard rule §4.1):
        /// descending waves must finish at the bar LOW, ascending waves at the bar HIGH.
        /// Pass <c>null</c> (default) to skip this check — useful in unit tests that
        /// work with synthetic price points.
        /// </param>
        public ElliottWaveExactMarkup(IBarsProvider barsProvider = null)
        {
            m_BarsProvider = barsProvider;
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
                type == ElliottModelType.FLAT_RUNNING)
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
                if (Math.Abs(cur.Value - prev.Value) < double.Epsilon)
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
                results = results.Where(n => n.GetDepth() >= MIN_RESULT_DEPTH).ToList();
            }
            return results;
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
            bool isLastLevel = depth + 1 >= MAX_MARKUP_DEPTH;

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
                    TryCombinations(model, altPoints, 0, n - 1, k, results, fullSpan: true,
                        enforceEndpointExtrema: isLastLevel);
                }
            }

            results = results.OrderByDescending(x => x.Score).ToList();

            // Recursive sub-wave analysis: identify what sub-model fills each segment.
            // Only recurse when we have not yet reached the depth limit.
            if (depth + 1 < MAX_MARKUP_DEPTH)
            {
                // FillSubWaveModels returns false when the node must be discarded:
                // at the last markup level a sub-wave position whose valid models are
                // all motive/zigzag must either (a) be identified as one of those models
                // or (b) pass the endpoint-extremum hard rule (§4.1-endpoint).
                // Only the top MAX_CANDIDATES_FOR_SUBWAVE_FILL candidates are processed —
                // lower-ranked combinations are unlikely to win after the depth-coverage
                // re-sort and processing them wastes exponential recursive work.
                results = results
                    .Take(MAX_CANDIDATES_FOR_SUBWAVE_FILL)
                    .Where(node => FillSubWaveModels(node, points, depth + 1))
                    .ToList();

                // Re-sort after sub-wave filling: reward candidates whose sub-waves
                // were successfully identified deeper (§6.6 — depth coverage bonus).
                foreach (ExactParsedNode node in results)
                    node.Score *= (1.0 + ComputeDepthCoverage(node) * DEPTH_COVERAGE_BONUS);

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

            bool isAtLastSubLevel = depth + 1 >= MAX_MARKUP_DEPTH;
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

                List<ExactParsedNode> subResults =
                    ParseInternal(subPoints, validModels, depth);

                // §4.1-continuity (hard rule): the sub-model must span exactly the
                // sub-wave's bar range [fromBar, toBar] — no leading or trailing bars
                // may be left unaccounted for within the sub-wave.
                ExactParsedNode best = subResults.FirstOrDefault(
                    r => r.WaveCount == r.ExpectedWaves
                      && r.StartPoint.BarIndex == fromBar
                      && r.EndPoint.BarIndex == toBar);
                if (best != null)
                {
                    node.SubWaves[i] = best;
                    continue;
                }

                // Sub-wave could not be identified — it stays as SIMPLE_IMPULSE.
                //
                // Repair §4.2-endpoint-repair: a corridor breach inside a bare
                // SIMPLE_IMPULSE is physically impossible — a simple impulse must end
                // at the true OHLC extremum of its range.  Triangles and flats are
                // exempt because their internal sub-structure accounts for breaches.
                // When a breach is detected, move the endpoint (and the adjacent
                // sub-wave's startpoint) to the actual OHLC extremum so that both the
                // hard §4.1-endpoint rule and the Fibonacci score use the true price.
                // Because the update happens before sub-wave i+1 is processed, the
                // next iteration automatically uses the corrected startpoint.
                if (m_BarsProvider != null)
                {
                    bool   isUp   = sw.IsUp;
                    double bestVal = sw.EndPoint.Value;
                    int    bestBar = toBar;

                    // Start from fromBar + 1: the start bar belongs to the opposite
                    // extremum side (UP wave starts at a LOW, DOWN wave at a HIGH),
                    // so including it would risk placing the endpoint at the wrong
                    // side of the move.
                    for (int b = fromBar + 1; b <= toBar; b++)
                    {
                        if (b < 0 || b >= m_BarsProvider.Count) continue;
                        double v = isUp
                            ? m_BarsProvider.GetHighPrice(b)
                            : m_BarsProvider.GetLowPrice(b);
                        if (isUp ? v > bestVal : v < bestVal)
                        {
                            bestVal = v;
                            bestBar = b;
                        }
                    }

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
            if (anyRepaired)
            {
                var segs = new Segment[node.WaveCount];
                for (int j = 0; j < node.WaveCount; j++)
                {
                    ExactParsedNode sw = node.SubWaves[j];
                    segs[j] = new Segment { Start = sw.StartPoint, End = sw.EndPoint };
                }
                double repairedScore = CalculateFiboScore(node.ModelType, segs);
                if (repairedScore > 0)
                    node.Score = repairedScore * FULL_FIT_BONUS;
            }

            return true;
        }

        /// <summary>
        /// Returns the fraction [0.0, 1.0] of immediate sub-waves that have been identified
        /// as a real Elliott Wave model (anything other than
        /// <see cref="ElliottModelType.SIMPLE_IMPULSE"/>).
        /// 0.0 = all sub-waves are bare SIMPLE_IMPULSE segments (no sub-structure found).
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
                pts, waves, results, fullSpan, enforceEndpointExtrema);
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
            bool fullSpan, bool enforceEndpointExtrema)
        {
            if (waveIdx == k - 1)
            {
                // Last wave always ends at outerEnd (already in pts[k]).
                waves[waveIdx] = new Segment { Start = pts[waveIdx], End = pts[k] };

                // Alternation check for the last wave only (previous pairs checked incrementally).
                if (waveIdx > 0 && waves[waveIdx].IsUp == waves[waveIdx - 1].IsUp) return;

                // Full validation: remaining hard rules, candle checks, score.
                if (!CheckHardRules(model, waves)) return;
                if (!CheckDirectionEndpoints(waves)) return;
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
                pts[waveIdx + 1] = altPoints[j];
                waves[waveIdx]   = new Segment { Start = pts[waveIdx], End = pts[waveIdx + 1] };

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
                    pts, waves, results, fullSpan, enforceEndpointExtrema);
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
                    if (Math.Abs(w.End.Value - barHigh) > 1e-9) return false;
                }
                else
                {
                    // Descending wave — end must be the bar LOW
                    double barLow = m_BarsProvider.GetLowPrice(idx);
                    if (Math.Abs(w.End.Value - barLow) > 1e-9) return false;
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
                            if ( isUp && w[2].End.Value <= w[0].End.Value) return false;
                            if (!isUp && w[2].End.Value >= w[0].End.Value) return false;
                            if (w[2].Length >= w[0].Length) return false; // contracting
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

                case ElliottModelType.TRIANGLE_CONTRACTING:
                    if (waveIdx >= 1 && w[waveIdx].Length >= w[waveIdx - 1].Length) return false;
                    return true;

                case ElliottModelType.TRIANGLE_RUNNING:
                    if (waveIdx == 1)
                    {
                        if ( isUp && w[1].End.Value >= start) return false;
                        if (!isUp && w[1].End.Value <= start) return false;
                    }
                    if (waveIdx >= 2 && w[waveIdx].Length >= w[waveIdx - 1].Length) return false;
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

                        if (isUp && w3End <= w1End) return false;
                        if (!isUp && w3End >= w1End) return false;

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
                            if (isUp && w[4].End.Value <= w3End) return false;
                            if (!isUp && w[4].End.Value >= w3End) return false;
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
                        double wWEnd = w[0].End.Value;

                        if (wXLen > wWLen * 0.95) return false;

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

                case ElliottModelType.TRIANGLE_CONTRACTING:
                    if (w.Length < 5) return false;
                    {
                        for (int i = 1; i < w.Length; i++)
                            if (w[i].Length >= w[i - 1].Length) return false;

                        // E must remain on the triangle side of the start (not break out the wrong way).
                        // The eEnd >= wAEnd constraint is removed: the wave-length contracting rule
                        // (each step shorter) already enforces convergence; a price-level upper/lower
                        // bound relative to A's end is a Fibonacci preference, not a structural rule.
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

                        for (int i = 2; i < w.Length; i++)
                            if (w[i].Length >= w[i - 1].Length) return false;

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
