using System;
using System.Collections.Generic;
using System.Linq;
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
        private const double TRUNCATION_SCORE_MULT = 0.05;

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
                BarPoint prev = result[result.Count - 1];
                BarPoint cur = points[i];
                if (Math.Abs(cur.Value - prev.Value) < double.Epsilon)
                    continue;
                if (result.Count == 1)
                {
                    result.Add(cur);
                    continue;
                }
                BarPoint prevPrev = result[result.Count - 2];
                bool prevUp = prev.Value > prevPrev.Value;
                bool curUp = cur.Value > prev.Value;
                if (curUp == prevUp)
                {
                    result[result.Count - 1] = curUp
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
        /// </summary>
        public List<ExactParsedNode> Parse(List<BarPoint> points)
        {
            if (points == null || points.Count < 2)
                return new List<ExactParsedNode>();

            // Collapse multi-level input to strictly alternating extrema
            List<BarPoint> altPoints = ReduceToAlternating(points);
            int n = altPoints.Count;
            if (n < 2)
                return new List<ExactParsedNode>();

            var results = new List<ExactParsedNode>();

            // The correct Elliott Wave pattern always spans from altPoints[0] to altPoints[n-1].
            // For each model with K waves we need to pick K-1 intermediate points from
            // altPoints[1..n-2] such that the resulting K segments pass hard rules.
            // Additionally, we allow trying sub-spans (offset>0) with a reduced score bonus.

            foreach (ElliottModelType model in TargetModels)
            {
                int k = GetExpectedWaves(model);
                if (n - 1 < k) continue; // need at least k segments

                // --- Full-span search: fix start=0, end=n-1, pick K-1 intermediates ---
                if (n - 2 >= k - 1) // at least k-1 inner points available
                {
                    TryCombinations(model, altPoints, 0, n - 1, k, results, fullSpan: true);
                }

                // --- Fallback: try all contiguous k-segment windows (for edge cases) ---
                int s = n - 1; // total segments
                for (int offset = 0; offset <= s - k; offset++)
                {
                    // Only try windows not already covered by full-span search
                    if (offset == 0 && offset + k == s) continue; // full-span already done
                    if (offset == 0 && offset + k == n - 1) continue;

                    Segment[] waves = new Segment[k];
                    bool valid = true;
                    for (int i = 0; i < k; i++)
                    {
                        int pi = offset + i;
                        if (pi + 1 >= n) { valid = false; break; }
                        waves[i] = new Segment { Start = altPoints[pi], End = altPoints[pi + 1] };
                    }
                    if (!valid) continue;

                    if (!CheckAlternation(waves)) continue;
                    if (!CheckHardRules(model, waves)) continue;

                    double fiboScore = CalculateFiboScore(model, waves);
                    if (fiboScore <= 0) continue;

                    // Penalize windows that don't span the full input
                    int windowBarSpan = Math.Max(1,
                        waves[k - 1].End.BarIndex - waves[0].Start.BarIndex);
                    int totalBarSpan = Math.Max(1,
                        altPoints[n - 1].BarIndex - altPoints[0].BarIndex);
                    double barCoverage = (double)windowBarSpan / totalBarSpan;
                    fiboScore *= barCoverage * barCoverage * barCoverage * barCoverage;

                    results.Add(BuildNode(model, waves, fiboScore, k));
                }
            }

            return results.OrderByDescending(x => x.Score).ToList();
        }

        /// <summary>
        /// Tries all combinations of K-1 intermediate points from altPoints[innerStart..innerEnd-1]
        /// with fixed start=altPoints[outerStart] and end=altPoints[outerEnd].
        /// Adds valid candidates to <paramref name="results"/>.
        /// </summary>
        private void TryCombinations(
            ElliottModelType model,
            List<BarPoint> altPoints,
            int outerStart, int outerEnd,
            int k,
            List<ExactParsedNode> results,
            bool fullSpan)
        {
            // We need k+1 points: outerStart + (k-1 from inner) + outerEnd
            // inner range: indices [outerStart+1 .. outerEnd-1]
            int innerCount = outerEnd - outerStart - 1;
            if (innerCount < k - 1) return; // not enough inner points

            int[] chosen = new int[k - 1]; // indices into altPoints of the k-1 intermediate points
            // Initialize: choose first k-1 inner indices
            for (int i = 0; i < k - 1; i++)
                chosen[i] = outerStart + 1 + i;

            // Enumerate all C(innerCount, k-1) combinations in order
            while (true)
            {
                // Build k+1 point array: [outerStart, chosen[0], ..., chosen[k-2], outerEnd]
                BarPoint[] pts = new BarPoint[k + 1];
                pts[0] = altPoints[outerStart];
                for (int i = 0; i < k - 1; i++)
                    pts[i + 1] = altPoints[chosen[i]];
                pts[k] = altPoints[outerEnd];

                // Build segments and check alternation
                Segment[] waves = new Segment[k];
                for (int i = 0; i < k; i++)
                    waves[i] = new Segment { Start = pts[i], End = pts[i + 1] };

                if (CheckAlternation(waves) && CheckHardRules(model, waves))
                {
                    double score = CalculateFiboScore(model, waves);
                    if (score > 0)
                    {
                        if (fullSpan) score *= FULL_FIT_BONUS;
                        results.Add(BuildNode(model, waves, score, k));
                    }
                }

                // Advance to next combination (standard combinatorial next)
                int pos = k - 2;
                while (pos >= 0 && chosen[pos] >= outerEnd - 1 - (k - 2 - pos))
                    pos--;
                if (pos < 0) break;
                chosen[pos]++;
                for (int i = pos + 1; i < k - 1; i++)
                    chosen[i] = chosen[i - 1] + 1;
            }
        }

        private static List<Segment> BuildSegments(List<BarPoint> pts)
        {
            var segs = new List<Segment>(pts.Count - 1);
            for (int i = 0; i < pts.Count - 1; i++)
                segs.Add(new Segment { Start = pts[i], End = pts[i + 1] });
            return segs;
        }

        private ExactParsedNode TryBuildNode(
            ElliottModelType model, List<Segment> segments, int offset, int count,
            bool isFullFit, double totalPriceRange, int totalBarSpan)
        {
            Segment[] waves = new Segment[count];
            for (int i = 0; i < count; i++)
                waves[i] = segments[offset + i];

            if (!CheckAlternation(waves)) return null;
            if (!CheckHardRules(model, waves)) return null;

            double score = CalculateFiboScore(model, waves);
            if (score <= 0) return null;

            // Bar-span coverage: fraction of the full input bar span that this window covers.
            // Cubic scaling strongly rewards windows that span the full input.
            int windowBarSpan = Math.Max(1, waves[count - 1].End.BarIndex - waves[0].Start.BarIndex);
            double barCoverage = (double)windowBarSpan / totalBarSpan;
            score *= barCoverage * barCoverage * barCoverage;

            // Price coverage bonus: also reward by price range fraction
            double windowPriceRange = Math.Abs(waves[count - 1].End.Value - waves[0].Start.Value);
            double priceCoverage = totalPriceRange > 0 ? windowPriceRange / totalPriceRange : 1.0;
            score *= priceCoverage * priceCoverage;

            if (isFullFit) score *= FULL_FIT_BONUS;

            return BuildNode(model, waves, score, count);
        }

        private ExactParsedNode TryBuildTruncated(
            ElliottModelType model, List<Segment> segments, int offset, int k,
            double totalPriceRange, int totalBarSpan)
        {
            if (segments.Count < offset + k) return null;

            int kMinus1 = k - 1;
            Segment[] wavesK1 = new Segment[kMinus1];
            for (int i = 0; i < kMinus1; i++)
                wavesK1[i] = segments[offset + i];

            if (!CheckAlternation(wavesK1)) return null;
            if (!CheckHardRulesPartial(model, wavesK1)) return null;

            Segment candidate = segments[offset + kMinus1];
            bool modelIsUp = wavesK1[0].IsUp;

            if (candidate.IsUp != modelIsUp) return null;
            if (candidate.Length >= wavesK1[2].Length) return null;
            if (candidate.Length < wavesK1[0].Length * 0.1) return null;

            Segment[] allWaves = new Segment[k];
            Array.Copy(wavesK1, allWaves, kMinus1);
            allWaves[kMinus1] = candidate;

            if (!CheckHardRules(model, allWaves)) return null;

            double score = CalculateFiboScore(model, allWaves) * TRUNCATION_SCORE_MULT;
            if (score <= 0) return null;

            int windowBarSpan = Math.Max(1, allWaves[k - 1].End.BarIndex - allWaves[0].Start.BarIndex);
            double barCoverage = (double)windowBarSpan / totalBarSpan;
            score *= barCoverage * barCoverage * barCoverage;

            double windowPriceRange = Math.Abs(allWaves[k - 1].End.Value - allWaves[0].Start.Value);
            double priceCoverage = totalPriceRange > 0 ? windowPriceRange / totalPriceRange : 1.0;
            score *= priceCoverage * priceCoverage;

            return BuildNode(model, allWaves, score, k);
        }

        private static bool CheckAlternation(Segment[] waves)
        {
            if (waves.Length < 2) return true;
            for (int i = 0; i < waves.Length - 1; i++)
                if (waves[i].IsUp == waves[i + 1].IsUp) return false;
            return true;
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

        private static bool CheckHardRulesPartial(ElliottModelType model, Segment[] w)
        {
            if (w.Length == 0) return false;
            bool isUp = w[0].IsUp;
            double start = w[0].Start.Value;

            switch (model)
            {
                case ElliottModelType.IMPULSE:
                    if (w.Length < 4) return false;
                    {
                        double w1End = w[0].End.Value;
                        double w3End = w[2].End.Value;

                        if (isUp && w[1].End.Value <= start) return false;
                        if (!isUp && w[1].End.Value >= start) return false;

                        if (isUp && w3End <= w1End) return false;
                        if (!isUp && w3End >= w1End) return false;

                        if (isUp && w[3].End.Value <= start) return false;
                        if (!isUp && w[3].End.Value >= start) return false;
                        if (isUp && w[3].End.Value <= w1End) return false;
                        if (!isUp && w[3].End.Value >= w1End) return false;
                    }
                    return true;

                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                    if (w.Length < 4) return false;
                    {
                        double w1End = w[0].End.Value;
                        double w3End = w[2].End.Value;

                        if (isUp && w[1].End.Value <= start) return false;
                        if (!isUp && w[1].End.Value >= start) return false;

                        if (isUp && w3End <= w1End) return false;
                        if (!isUp && w3End >= w1End) return false;

                        if (w[2].Length >= w[0].Length) return false;
                        if (w[3].Length >= w[1].Length) return false;

                        if (isUp && w[3].End.Value >= w1End) return false;
                        if (!isUp && w[3].End.Value <= w1End) return false;
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
