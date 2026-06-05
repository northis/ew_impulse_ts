using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Step 6 of EW_MARKUP_v2.md §19 — prediction mode (§13) and the cancellation
    /// side of extensions (§11). When the input range ends at the current bar the last
    /// one or two waves of an otherwise-valid model may still be missing; such partial
    /// models are emitted directly by the DP as <see cref="NodeStatus.PROJECTED"/> roots
    /// (their confirmed prefix obeys the hard rules §7–9, the missing tail is projected
    /// from the shared Fibonacci/trendline helper §16/§13). The engine returns the single
    /// best continuation (§13), together with its price/time projections and the
    /// cancellation zone (§11.2) whose breach would kill the projection.
    /// </summary>
    public partial class ElliottWaveExactMarkupV2
    {
        /// <summary>Maximum number of missing tail waves a projection may have (§13, §3.3.2).</summary>
        public const int MAX_MISSING_WAVES = 2;

        /// <summary>
        /// Builds the beam-kept partial-model blueprints for the range
        /// <c>[startSeg..endSeg]</c> whose confirmed prefix fully covers the range and
        /// whose last 1–2 waves are still forming (projected, §13).
        /// </summary>
        private List<Candidate> BuildProjectionRoots(int startSeg, int endSeg)
        {
            var all = new List<Candidate>();

            foreach (ElliottModelType model in StartModels)
            {
                if (!S_SUPPORTED_MODELS.Contains(model))
                    continue;

                int waves = ElliottWaveExactMarkup.GetExpectedWaves(model);

                // S = K − missing confirmed waves; completeness C = S/K ∈ {(K−1)/K, (K−2)/K}.
                for (int missing = 1; missing <= MAX_MISSING_WAVES && missing < waves; missing++)
                {
                    int confirmed = waves - missing;
                    ExtendPartial(
                        model, waves, confirmed, startSeg, endSeg, 0,
                        new List<Candidate>(confirmed), all);
                    if (m_Aborted)
                        return Beam(all);
                }
            }

            return Beam(all);
        }

        /// <summary>
        /// Recursively fixes the next confirmed wave of a partial model, requiring the
        /// confirmed prefix to consume the whole range; the hard rules (§7–9) are applied
        /// only to the confirmed waves (relaxed tail, §3.4 of EW_PREDICTION.md).
        /// </summary>
        private void ExtendPartial(
            ElliottModelType model,
            int waves,
            int confirmed,
            int segStart,
            int rangeEnd,
            int waveIndex,
            List<Candidate> chosen,
            List<Candidate> results)
        {
            if (m_Aborted)
                return;

            bool isLast = waveIndex == confirmed - 1;
            int remainingAfter = confirmed - waveIndex - 1;

            for (int p = segStart; p <= rangeEnd; p++)
            {
                int waveLen = p - segStart + 1;
                if ((waveLen & 1) == 0)
                    continue; // each wave spans an odd number of alternating segments

                int rest = rangeEnd - p;
                if (rest < remainingAfter || ((rest - remainingAfter) & 1) != 0)
                    continue;
                if (isLast && p != rangeEnd)
                    continue; // the confirmed prefix must end exactly at the current bar

                foreach (Candidate child in WaveOptions(model, waveIndex, segStart, p))
                {
                    chosen.Add(child);

                    DeathReason death = CheckIncremental(model, chosen);
                    if (death != DeathReason.NONE)
                    {
                        m_Metrics.Count(death);
                        chosen.RemoveAt(chosen.Count - 1);
                        continue;
                    }

                    if (isLast)
                        FinalizePartial(model, waves, confirmed, SegFirst(chosen), rangeEnd, chosen, results);
                    else
                        ExtendPartial(model, waves, confirmed, p + 1, rangeEnd, waveIndex + 1, chosen, results);

                    chosen.RemoveAt(chosen.Count - 1);
                    if (m_Aborted)
                        return;
                }
            }
        }

        /// <summary>
        /// Materializes a confirmed-prefix sequence into a partial <see cref="Candidate"/>,
        /// scoring it by the model probability, the confirmed sub-wave scores and the
        /// completeness penalty <c>C = confirmed / waves</c> (§13).
        /// </summary>
        private void FinalizePartial(
            ElliottModelType model, int waves, int confirmed, int i, int j,
            List<Candidate> chosen, List<Candidate> results)
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

            // Completeness penalty so a projection always ranks below a comparable
            // COMPLETE model of the same Fibonacci quality (§13).
            score *= (double)confirmed / waves;

            if (++m_NodesCreated > MAX_NODES_TOTAL)
            {
                m_Aborted = true;
                return;
            }

            results.Add(new Candidate(model, null, i, j, level, score, chosen.ToArray()));
        }

        /// <summary>
        /// Materializes a partial <see cref="Candidate"/> into a
        /// <see cref="NodeStatus.PROJECTED"/> root: its confirmed sub-waves become COMPLETE
        /// children, the missing tail waves get Fibonacci/trendline projections (§13) and a
        /// cancellation zone (§11.2).
        /// </summary>
        private TreeNode MaterializeProjection(Candidate c)
        {
            int confirmed = c.Children.Count;

            var node = new TreeNode(c.Model, null, c.Level)
            {
                RangeStartSegment = c.StartSeg,
                RangeEndSegment = c.EndSeg,
                StartPivot = Pivots[c.StartSeg],
                EndPivot = Pivots[c.EndSeg + 1],
                Score = c.Score,
                ActiveFromWaveIndex = confirmed
            };

            for (int k = 0; k < c.Children.Count; k++)
            {
                Candidate withPos = c.Children[k] with
                {
                    WavePos = ElliottWaveExactMarkup.GetWaveKey(c.Model, k + 1)
                };
                node.AddChild(Materialize(withPos, isRoot: false));
            }

            (IReadOnlyList<WaveProjection> projections, WaveCancellation cancellation) =
                ComputeProjections(c.Model, node);
            node.Projections = projections;
            node.Cancellation = cancellation;

            node.MarkProjected();
            return node;
        }

        /// <summary>
        /// Projects the missing tail waves of <paramref name="node"/> using the shared
        /// Fibonacci projection helper (§16) and, for triangles/diagonals, converging
        /// trendlines (§13/§4.4 of EW_PREDICTION.md). Returns the per-wave projections and
        /// the cancellation zone of the first projected wave (§11.2).
        /// </summary>
        private static (IReadOnlyList<WaveProjection>, WaveCancellation) ComputeProjections(
            ElliottModelType model, TreeNode node)
        {
            int confirmed = node.Children.Count;
            int waves = ElliottWaveExactMarkup.GetExpectedWaves(model);
            if (confirmed < 1 || confirmed >= waves)
                return (Array.Empty<WaveProjection>(), null);

            var confSegs = new List<Segment>(confirmed);
            int totalBars = 0;
            foreach (TreeNode child in node.Children)
            {
                var seg = new Segment(child.StartPivot, child.EndPivot);
                confSegs.Add(seg);
                totalBars += seg.BarsCount;
            }

            int avgBars = Math.Max(1, totalBars / confirmed);
            double lastPrice = confSegs[confirmed - 1].End.Value;
            int lastBar = confSegs[confirmed - 1].End.BarIndex;
            bool nextIsUp = !confSegs[confirmed - 1].IsUp;

            var projections = new List<WaveProjection>(waves - confirmed);
            double cancelPrice = lastPrice;
            int cancelBar = lastBar;

            for (int w = confirmed; w < waves; w++)
            {
                WaveProjection proj = TryTrendlineProjection(
                    model, confSegs, confirmed, w, lastPrice, lastBar, nextIsUp);

                if (proj == null)
                {
                    (double ratio, double weight) = ElliottWaveExactMarkup.GetBestFibRatio(model, w);
                    if (ratio <= 0) ratio = 0.618;
                    if (weight <= 0) weight = 0.1;

                    double refLen = ReferenceWaveLength(model, w, confSegs, confirmed);
                    double projLen = refLen * ratio;
                    double projPrice = nextIsUp ? lastPrice + projLen : lastPrice - projLen;
                    int projBar = lastBar + EstimateWaveBars(model, w, confSegs, confirmed, avgBars);

                    proj = new WaveProjection(
                        projPrice, projBar, ratio.ToString("0.###"), weight,
                        ElliottWaveExactMarkup.GetWaveKey(model, w + 1));
                }

                if (w == confirmed)
                {
                    // §11.2: breaching the projected wave's origin (the last confirmed
                    // pivot), or running past its estimated bar, cancels the projection.
                    cancelPrice = lastPrice;
                    cancelBar = proj.BarIndex;
                }

                projections.Add(proj);
                lastPrice = proj.Price;
                lastBar = proj.BarIndex;
                nextIsUp = !nextIsUp;
            }

            WaveCancellation cancellation = projections.Count > 0
                ? new WaveCancellation(cancelPrice, cancelBar, DeathReason.EXTENSION_CANCELLED)
                : null;

            return (projections, cancellation);
        }

        /// <summary>
        /// Attempts a converging-trendline projection for triangles and diagonals
        /// (§4.4 of EW_PREDICTION.md); returns <c>null</c> when not applicable.
        /// </summary>
        private static WaveProjection TryTrendlineProjection(
            ElliottModelType model, IReadOnlyList<Segment> seg, int confirmed,
            int waveIndex, double lastPrice, int lastBar, bool nextIsUp)
        {
            bool isTriangle = model == ElliottModelType.TRIANGLE_CONTRACTING
                           || model == ElliottModelType.TRIANGLE_RUNNING;
            bool isDiagonal = model == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL
                           || model == ElliottModelType.DIAGONAL_CONTRACTING_ENDING;

            if (!isTriangle && !isDiagonal)
                return null;
            if (confirmed < 3)
                return null;

            int idx1, idx2;
            if (waveIndex % 2 == 0)
            {
                idx1 = 0;
                idx2 = 2;
            }
            else
            {
                if (confirmed < 4)
                    return null;
                idx1 = 1;
                idx2 = 3;
            }

            if (idx2 >= confirmed)
                return null;

            BarPoint p1 = seg[idx1].End;
            BarPoint p2 = seg[idx2].End;
            if (p2.BarIndex == p1.BarIndex)
                return null;

            double slope = (p2.Value - p1.Value) / (p2.BarIndex - p1.BarIndex);
            int refBars = Math.Abs(p2.BarIndex - p1.BarIndex);
            int estTargetBar = lastBar + Math.Max(1, refBars / 2);
            double projPrice = p2.Value + slope * (estTargetBar - p2.BarIndex);

            // Reject projections that point the wrong way for the upcoming wave.
            if (nextIsUp && projPrice <= lastPrice)
                return null;
            if (!nextIsUp && projPrice >= lastPrice)
                return null;

            return new WaveProjection(
                projPrice, estTargetBar, "trendline", 0.8,
                ElliottWaveExactMarkup.GetWaveKey(model, waveIndex + 1));
        }

        /// <summary>Reference wave length for the Fibonacci projection of wave <paramref name="waveIndex"/>.</summary>
        private static double ReferenceWaveLength(
            ElliottModelType model, int waveIndex, IReadOnlyList<Segment> seg, int confirmed)
        {
            if (confirmed < 1)
                return 1.0;

            switch (model)
            {
                case ElliottModelType.IMPULSE:
                    if (waveIndex == 3 && confirmed > 2) return seg[2].Length; // W4 ref = W3
                    return seg[0].Length;                                      // W2/W3/W5 ref = W1

                case ElliottModelType.DIAGONAL_CONTRACTING_INITIAL:
                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                    if (waveIndex == 3 && confirmed > 2) return seg[2].Length; // W4 ref = W3
                    if (waveIndex == 4 && confirmed > 2) return seg[2].Length; // W5 ref = W3
                    return seg[0].Length;

                case ElliottModelType.TRIANGLE_CONTRACTING:
                case ElliottModelType.TRIANGLE_RUNNING:
                    if (waveIndex > 0 && waveIndex - 1 < confirmed)
                        return seg[waveIndex - 1].Length; // each wave relative to the previous
                    return seg[confirmed - 1].Length;

                default:
                    return seg[0].Length; // zigzags / flats: relative to wave A / W
            }
        }

        /// <summary>Estimated bar duration of the projected wave <paramref name="waveIndex"/>.</summary>
        private static int EstimateWaveBars(
            ElliottModelType model, int waveIndex, IReadOnlyList<Segment> seg, int confirmed, int avgBars)
        {
            switch (model)
            {
                case ElliottModelType.IMPULSE:
                    if (waveIndex == 3 && confirmed > 1) return Math.Max(1, seg[1].BarsCount); // W4 ≈ W2
                    if (waveIndex == 4) return Math.Max(1, seg[0].BarsCount);                  // W5 ≈ W1
                    break;

                case ElliottModelType.TRIANGLE_CONTRACTING:
                case ElliottModelType.TRIANGLE_RUNNING:
                    if (waveIndex > 0 && waveIndex - 1 < confirmed)
                        return Math.Max(1, (int)(seg[waveIndex - 1].BarsCount * 0.786));
                    break;
            }

            return avgBars;
        }
    }

    /// <summary>
    /// The price/time zone whose breach kills a projected tail (EW_MARKUP_v2.md §11.2):
    /// a close beyond <see cref="PriceLevel"/> or past <see cref="BarLimit"/> cancels the
    /// projection with <see cref="Reason"/>.
    /// </summary>
    public sealed record WaveCancellation(double PriceLevel, int BarLimit, DeathReason Reason);
}
