using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    public class ElliottWaveMarkup
    {
        private static readonly (byte weight, double ratio)[] ZIGZAG_C_TO_A = { (0, 0), (5, 0.618), (25, 0.786), (35, 0.786), (75, 1), (85, 1.618), (90, 2.618), (95, 3.618) };
        private static readonly (byte weight, double ratio)[] CONTRACTING_DIAGONAL_3_TO_1 = { (0, 0), (5, 0.5), (15, 0.618), (20, 0.786) };
        private static readonly (byte weight, double ratio)[] IMPULSE_3_TO_1 = { (0, 0), (5, 0.618), (10, 0.786), (15, 1), (25, 1.618), (60, 2.618), (75, 3.618), (90, 4.236) };
        private static readonly (byte weight, double ratio)[] IMPULSE_5_TO_1 = { (0, 0), (5, 0.382), (10, 0.618), (20, 0.786), (25, 1), (75, 1.618), (85, 2.618), (95, 3.618), (99, 4.236) };
        private static readonly (byte weight, double ratio)[] MAP_DEEP_CORRECTION = { (0, 0), (5, 0.5), (25, 0.618), (70, 0.786), (99, 0.95) };
        private static readonly (byte weight, double ratio)[] MAP_SHALLOW_CORRECTION = { (0, 0), (5, 0.236), (35, 0.382), (85, 0.5) };
        private static readonly (byte weight, double ratio)[] MAP_EX_FLAT_WAVE_C_TO_A = { (0, 0), (20, 1.618), (80, 2.618), (95, 3.618) };
        private static readonly (byte weight, double ratio)[] MAP_RUNNING_FLAT_WAVE_C_TO_A = { (0, 0), (5, 0.5), (20, 0.618), (80, 1), (90, 1.272), (95, 1.618) };
        private static readonly (byte weight, double ratio)[] MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV = { (0, 0), (5, 0.5), (20, 0.618), (80, 0.786), (90, 0.9), (95, 0.95) };

        private readonly Dictionary<MarkupCacheKey, MarkupResult> m_Cache = new();

        private readonly struct MarkupCacheKey : IEquatable<MarkupCacheKey>
        {
            public readonly int StartIndex;
            public readonly int EndIndex;
            public readonly int AllowedModelsMask;

            public MarkupCacheKey(int startIndex, int endIndex, int allowedModelsMask)
            {
                StartIndex = startIndex;
                EndIndex = endIndex;
                AllowedModelsMask = allowedModelsMask;
            }

            public bool Equals(MarkupCacheKey other)
            {
                return StartIndex == other.StartIndex && EndIndex == other.EndIndex && AllowedModelsMask == other.AllowedModelsMask;
            }

            public override bool Equals(object obj) => obj is MarkupCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + StartIndex;
                    hash = hash * 31 + EndIndex;
                    hash = hash * 31 + AllowedModelsMask;
                    return hash;
                }
            }
        }

        public List<MarkupResult> ParseSegment(BarPoint start, BarPoint end, Dictionary<int, (BarPoint Point, int Rank)> ranksDict, int maxDepth = 3)
        {
            m_Cache.Clear();
            var innerPoints = ranksDict.Values.OrderBy(x => x.Point.BarIndex).ToList();
            bool isUp = end.Value > start.Value;

            int allowedMainModelsMask = (1 << (int)ElliottModelType.IMPULSE) |
                (1 << (int)ElliottModelType.ZIGZAG) |
                (1 << (int)ElliottModelType.DOUBLE_ZIGZAG) |
                (1 << (int)ElliottModelType.FLAT_EXTENDED) |
                (1 << (int)ElliottModelType.FLAT_RUNNING) |
                (1 << (int)ElliottModelType.TRIANGLE_CONTRACTING) |
                (1 << (int)ElliottModelType.TRIANGLE_RUNNING) |
                (1 << (int)ElliottModelType.DIAGONAL_CONTRACTING_INITIAL) |
                (1 << (int)ElliottModelType.DIAGONAL_CONTRACTING_ENDING) |
                (1 << (int)ElliottModelType.SIMPLE_IMPULSE);

            List<MarkupResult> bestCombo = new List<MarkupResult>();
            double bestScore = -1;

            // 1. Main only
            var mainRes = FindBestModel(start, end, isUp, innerPoints, allowedMainModelsMask, 0, maxDepth, "MAIN", 0);
            if (mainRes != null && mainRes.Score > bestScore)
            {
                bestScore = mainRes.Score;
                bestCombo = new List<MarkupResult> { mainRes };
            }

            int minRank = innerPoints.Count > 0 ? innerPoints.Min(p => p.Rank) : 0;
            var candidates = innerPoints.Where(p => p.Rank <= minRank + 2).ToList();

            // 2. Flat Tail + Main
            if (candidates.Count >= 2)
            {
                for (int i = 0; i < candidates.Count - 1; i++)
                {
                    var p1 = candidates[i];
                    if (isUp && p1.Point.Value <= start.Value) continue;
                    if (!isUp && p1.Point.Value >= start.Value) continue;

                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        var p2 = candidates[j];
                        if (isUp && (p2.Point.Value >= p1.Point.Value || p2.Point.Value <= start.Value)) continue;
                        if (!isUp && (p2.Point.Value <= p1.Point.Value || p2.Point.Value >= start.Value)) continue;

                        double wB = GetLen(start, p1.Point); 
                        double wC = GetLen(p1.Point, p2.Point); 
                        if (wB == 0) continue;
                        double tailScore = GetFiboWeight(MAP_EX_FLAT_WAVE_C_TO_A, wC / wB); 

                        var remainingPoints = innerPoints.Where(p => p.Point.BarIndex > p2.Point.BarIndex).ToList();
                        var mainAfterTail = FindBestModel(p2.Point, end, isUp, remainingPoints, allowedMainModelsMask, 0, maxDepth, "", 0);

                        if (mainAfterTail != null)
                        {
                            double totalScore = tailScore * mainAfterTail.Score * 0.9; 
                            if (totalScore > bestScore)
                            {
                                bestScore = totalScore;
                                
                                var tailRes = new MarkupResult 
                                {
                                    ModelType = ElliottModelType.FLAT_RUNNING,
                                    Start = start,
                                    End = p2.Point,
                                    Boundaries = new List<BarPoint> { p1.Point },
                                    Score = tailScore,
                                    IsUp = isUp, 
                                    Level = 0,
                                    NodeName = ""
                                };
                                tailRes.SubWaves.Add(new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Start = start, End = p1.Point, IsUp = isUp, Level = 1, NodeName = "b", Score = 1 });
                                tailRes.SubWaves.Add(new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Start = p1.Point, End = p2.Point, IsUp = !isUp, Level = 1, NodeName = "c", Score = 1 });

                                bestCombo = new List<MarkupResult> { tailRes, mainAfterTail };
                            }
                        }
                    }
                }
            }

            // 3. Triangle Tail + Main
            if (candidates.Count >= 4)
            {
                for (int i = 0; i < candidates.Count - 3; i++)
                {
                    var p1 = candidates[i];
                    if (isUp && p1.Point.Value <= start.Value) continue;
                    if (!isUp && p1.Point.Value >= start.Value) continue;

                    for (int j = i + 1; j < candidates.Count - 2; j++)
                    {
                        var p2 = candidates[j];
                        if (isUp && (p2.Point.Value >= p1.Point.Value || p2.Point.Value <= start.Value)) continue;
                        if (!isUp && (p2.Point.Value <= p1.Point.Value || p2.Point.Value >= start.Value)) continue;

                        for (int k = j + 1; k < candidates.Count - 1; k++)
                        {
                            var p3 = candidates[k];
                            if (isUp && (p3.Point.Value <= p2.Point.Value || p3.Point.Value >= p1.Point.Value)) continue;
                            if (!isUp && (p3.Point.Value >= p2.Point.Value || p3.Point.Value <= p1.Point.Value)) continue;

                            for (int l = k + 1; l < candidates.Count; l++)
                            {
                                var p4 = candidates[l];
                                if (isUp && (p4.Point.Value >= p3.Point.Value || p4.Point.Value <= p2.Point.Value)) continue;
                                if (!isUp && (p4.Point.Value <= p3.Point.Value || p4.Point.Value >= p2.Point.Value)) continue;

                                double wB = GetLen(start, p1.Point); 
                                double wC = GetLen(p1.Point, p2.Point); 
                                double wD = GetLen(p2.Point, p3.Point); 
                                double wE = GetLen(p3.Point, p4.Point); 

                                if (wB == 0 || wC == 0 || wD == 0) continue;

                                double tailScore = GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, wC / wB) *
                                                   GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, wD / wC) *
                                                   GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, wE / wD);

                                var remainingPoints = innerPoints.Where(p => p.Point.BarIndex > p4.Point.BarIndex).ToList();
                                var mainAfterTail = FindBestModel(p4.Point, end, isUp, remainingPoints, allowedMainModelsMask, 0, maxDepth, "", 0);

                                if (mainAfterTail != null)
                                {
                                    double totalScore = tailScore * mainAfterTail.Score * 0.9;
                                    if (totalScore > bestScore)
                                    {
                                        bestScore = totalScore;
                                        
                                        var tailRes = new MarkupResult 
                                        {
                                            ModelType = ElliottModelType.TRIANGLE_CONTRACTING,
                                            Start = start,
                                            End = p4.Point,
                                            Boundaries = new List<BarPoint> { p1.Point, p2.Point, p3.Point },
                                            Score = tailScore,
                                            IsUp = isUp,
                                            Level = 0,
                                            NodeName = ""
                                        };
                                        tailRes.SubWaves.Add(new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Start = start, End = p1.Point, IsUp = isUp, Level = 1, NodeName = "b", Score = 1 });
                                        tailRes.SubWaves.Add(new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Start = p1.Point, End = p2.Point, IsUp = !isUp, Level = 1, NodeName = "c", Score = 1 });
                                        tailRes.SubWaves.Add(new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Start = p2.Point, End = p3.Point, IsUp = isUp, Level = 1, NodeName = "d", Score = 1 });
                                        tailRes.SubWaves.Add(new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Start = p3.Point, End = p4.Point, IsUp = !isUp, Level = 1, NodeName = "e", Score = 1 });

                                        bestCombo = new List<MarkupResult> { tailRes, mainAfterTail };
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (bestCombo.Count == 0)
            {
                bestCombo.Add(new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Score = 1.0, Start = start, End = end, IsUp = isUp, NodeName = "", Level = 0 });
            }

            return bestCombo;
        }

        private MarkupResult FindBestModel(BarPoint start, BarPoint end, bool isUp,
            List<(BarPoint Point, int Rank)> innerPoints,
            int allowedModelsMask, int depth, int maxDepth, string nodeName, byte level)
        {
            var cacheKey = new MarkupCacheKey(start.BarIndex, end.BarIndex, allowedModelsMask);
            if (m_Cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var validInner = innerPoints.Where(p => p.Point.BarIndex > start.BarIndex && p.Point.BarIndex < end.BarIndex).ToList();

            if (depth >= maxDepth || validInner.Count < 2)
            {
                MarkupResult simpleRes = null;
                double penalty = 1.0;
                if (validInner.Count > 0)
                {
                    penalty = 1.0 / (1L << Math.Min(60, validInner.Count));
                }

                if ((allowedModelsMask & (1 << (int)ElliottModelType.SIMPLE_IMPULSE)) != 0)
                    simpleRes = new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Score = 1.0 * penalty, Start = start, End = end, IsUp = isUp, NodeName = nodeName, Level = level };
                else if ((allowedModelsMask & (1 << (int)ElliottModelType.IMPULSE)) != 0)
                    simpleRes = new MarkupResult { ModelType = ElliottModelType.IMPULSE, Score = 0.1 * penalty, Start = start, End = end, IsUp = isUp, NodeName = nodeName, Level = level };
                else if ((allowedModelsMask & (1 << (int)ElliottModelType.ZIGZAG)) != 0)
                    simpleRes = new MarkupResult { ModelType = ElliottModelType.ZIGZAG, Score = 0.05 * penalty, Start = start, End = end, IsUp = isUp, NodeName = nodeName, Level = level };
                
                if (simpleRes != null) m_Cache[cacheKey] = simpleRes;
                return simpleRes;
            }

            MarkupResult bestResult = null;
            double bestScore = -1;

            if ((allowedModelsMask & (1 << (int)ElliottModelType.SIMPLE_IMPULSE)) != 0)
            {
                double score = 1.0; // Base score for simple impulse when validInner.Count >= 2
                if (validInner.Count > 0)
                {
                    score *= 1.0 / (1L << Math.Min(60, validInner.Count)); // Heavy penalty for ignoring inner extrema
                }
                
                if (score > bestScore)
                {
                    bestScore = score;
                    bestResult = new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Score = score, Start = start, End = end, IsUp = isUp, NodeName = nodeName, Level = level };
                }
            }

            int minRank = validInner.Min(p => p.Rank);
            var candidates = validInner.Where(p => p.Rank <= minRank + 2).ToList();

            int allowedThreeWaveMask = allowedModelsMask & ((1 << (int)ElliottModelType.ZIGZAG) | (1 << (int)ElliottModelType.DOUBLE_ZIGZAG) | (1 << (int)ElliottModelType.FLAT_EXTENDED) | (1 << (int)ElliottModelType.FLAT_RUNNING));
            if (allowedThreeWaveMask != 0 && candidates.Count >= 2)
            {
                for (int i = 0; i < candidates.Count - 1; i++)
                {
                    var p1 = candidates[i];
                    if (isUp && p1.Point.Value <= start.Value && (allowedModelsMask & ((1 << (int)ElliottModelType.FLAT_EXTENDED) | (1 << (int)ElliottModelType.FLAT_RUNNING))) == 0) continue;
                    if (!isUp && p1.Point.Value >= start.Value && (allowedModelsMask & ((1 << (int)ElliottModelType.FLAT_EXTENDED) | (1 << (int)ElliottModelType.FLAT_RUNNING))) == 0) continue;

                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        var p2 = candidates[j];
                        if (isUp && p2.Point.Value >= p1.Point.Value) continue;
                        if (!isUp && p2.Point.Value <= p1.Point.Value) continue;

                        for (int m = 0; m <= (int)ElliottModelType.SIMPLE_IMPULSE; m++)
                        {
                            if ((allowedThreeWaveMask & (1 << m)) != 0)
                            {
                                ElliottModelType model = (ElliottModelType)m;
                                double score = Evaluate3WaveModel(model, start, end, isUp, minRank, p1, p2);
                                if (score > 0)
                                {
                                    var result = CreateResult(model, start, end, isUp, new[] { p1, p2 }, validInner, score, depth, maxDepth, nodeName, level);
                                    if (result != null && result.Score > bestScore)
                                    {
                                        bestScore = result.Score;
                                        bestResult = result;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            int allowedFiveWaveMask = allowedModelsMask & ((1 << (int)ElliottModelType.IMPULSE) | (1 << (int)ElliottModelType.DIAGONAL_CONTRACTING_INITIAL) | (1 << (int)ElliottModelType.DIAGONAL_CONTRACTING_ENDING) | (1 << (int)ElliottModelType.TRIANGLE_CONTRACTING) | (1 << (int)ElliottModelType.TRIANGLE_RUNNING));
            if (allowedFiveWaveMask != 0 && candidates.Count >= 4)
            {
                for (int i = 0; i < candidates.Count - 3; i++)
                {
                    var p1 = candidates[i];
                    if (isUp && p1.Point.Value <= start.Value && (allowedModelsMask & ((1 << (int)ElliottModelType.TRIANGLE_CONTRACTING) | (1 << (int)ElliottModelType.TRIANGLE_RUNNING))) == 0) continue;
                    if (!isUp && p1.Point.Value >= start.Value && (allowedModelsMask & ((1 << (int)ElliottModelType.TRIANGLE_CONTRACTING) | (1 << (int)ElliottModelType.TRIANGLE_RUNNING))) == 0) continue;

                    for (int j = i + 1; j < candidates.Count - 2; j++)
                    {
                        var p2 = candidates[j];
                        if (isUp && p2.Point.Value >= p1.Point.Value) continue;
                        if (!isUp && p2.Point.Value <= p1.Point.Value) continue;

                        for (int k = j + 1; k < candidates.Count - 1; k++)
                        {
                            var p3 = candidates[k];
                            if (isUp && p3.Point.Value <= p2.Point.Value) continue;
                            if (!isUp && p3.Point.Value >= p2.Point.Value) continue;

                            for (int l = k + 1; l < candidates.Count; l++)
                            {
                                var p4 = candidates[l];
                                if (isUp && p4.Point.Value >= p3.Point.Value) continue;
                                if (!isUp && p4.Point.Value <= p3.Point.Value) continue;

                                for (int m = 0; m <= (int)ElliottModelType.SIMPLE_IMPULSE; m++)
                                {
                                    if ((allowedFiveWaveMask & (1 << m)) != 0)
                                    {
                                        ElliottModelType model = (ElliottModelType)m;
                                        double score = Evaluate5WaveModel(model, start, end, isUp, minRank, p1, p2, p3, p4);
                                        if (score > 0)
                                        {
                                            var result = CreateResult(model, start, end, isUp, new[] { p1, p2, p3, p4 }, validInner, score, depth, maxDepth, nodeName, level);
                                            if (result != null && result.Score > bestScore)
                                            {
                                                bestScore = result.Score;
                                                bestResult = result;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            m_Cache[cacheKey] = bestResult;
            return bestResult;
        }

        private double GetLen(BarPoint a, BarPoint b) => Math.Abs(a.Value - b.Value);

        private double GetFiboWeight((byte weight, double ratio)[] map, double ratio)
        {
            for (int i = map.Length - 1; i >= 0; i--)
            {
                if (ratio >= map[i].ratio)
                {
                    return Math.Max(0.01, map[i].weight / 100.0);
                }
            }
            return 0.01;
        }

        private double Evaluate3WaveModel(ElliottModelType model, BarPoint start, BarPoint end, bool isUp, int minRank, (BarPoint Point, int Rank) p1, (BarPoint Point, int Rank) p2)
        {
            double wA = GetLen(start, p1.Point);
            double wB = GetLen(p1.Point, p2.Point);
            double wC = GetLen(p2.Point, end);

            if (wA == 0) return 0; // Avoid division by zero

            double fiboScore = 1.0;

            if (model == ElliottModelType.ZIGZAG || model == ElliottModelType.DOUBLE_ZIGZAG)
            {
                if (isUp && p2.Point.Value <= start.Value) return 0;
                if (!isUp && p2.Point.Value >= start.Value) return 0;
                if (isUp && end.Value <= p1.Point.Value) return 0;
                if (!isUp && end.Value >= p1.Point.Value) return 0;

                fiboScore *= GetFiboWeight(ZIGZAG_C_TO_A, wC / wA);
                fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, wB / wA), GetFiboWeight(MAP_SHALLOW_CORRECTION, wB / wA));
            }
            else if (model == ElliottModelType.FLAT_EXTENDED)
            {
                if (isUp && p2.Point.Value > start.Value) return 0;
                if (!isUp && p2.Point.Value < start.Value) return 0;
                if (isUp && end.Value <= p1.Point.Value) return 0;
                if (!isUp && end.Value >= p1.Point.Value) return 0;

                fiboScore *= GetFiboWeight(MAP_EX_FLAT_WAVE_C_TO_A, wC / wA);
                fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, wB / wA), GetFiboWeight(MAP_SHALLOW_CORRECTION, wB / wA));
            }
            else if (model == ElliottModelType.FLAT_RUNNING)
            {
                if (isUp && p2.Point.Value > start.Value) return 0;
                if (!isUp && p2.Point.Value < start.Value) return 0;
                if (isUp && end.Value > p1.Point.Value) return 0;
                if (!isUp && end.Value < p1.Point.Value) return 0;

                fiboScore *= GetFiboWeight(MAP_RUNNING_FLAT_WAVE_C_TO_A, wC / wA);
                fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, wB / wA), GetFiboWeight(MAP_SHALLOW_CORRECTION, wB / wA));
            }

            fiboScore = Math.Sqrt(fiboScore); // Geometric mean of 2 relations
            
            double rankAvg = ((p1.Rank - minRank) + (p2.Rank - minRank)) / 2.0;
            double rankPenalty = Math.Pow(0.8, rankAvg);

            return fiboScore * rankPenalty * ElliottWavePatternHelper.ModelRules[model].ProbabilityCoefficient;
        }

        private double Evaluate5WaveModel(ElliottModelType model, BarPoint start, BarPoint end, bool isUp, int minRank,
            (BarPoint Point, int Rank) p1, (BarPoint Point, int Rank) p2, (BarPoint Point, int Rank) p3, (BarPoint Point, int Rank) p4)
        {
            double w1 = GetLen(start, p1.Point);
            double w2 = GetLen(p1.Point, p2.Point);
            double w3 = GetLen(p2.Point, p3.Point);
            double w4 = GetLen(p3.Point, p4.Point);
            double w5 = GetLen(p4.Point, end);

            if (w1 == 0 || w2 == 0 || w3 == 0 || w4 == 0) return 0;

            double fiboScore = 1.0;

            if (model == ElliottModelType.IMPULSE)
            {
                if (isUp && p2.Point.Value <= start.Value) return 0;
                if (!isUp && p2.Point.Value >= start.Value) return 0;
                if (isUp && p4.Point.Value <= p1.Point.Value) return 0;
                if (!isUp && p4.Point.Value >= p1.Point.Value) return 0;
                if (w3 <= w1 && w3 <= w5) return 0;

                fiboScore *= GetFiboWeight(IMPULSE_3_TO_1, w3 / w1);
                fiboScore *= GetFiboWeight(IMPULSE_5_TO_1, w5 / w1);
                fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, w2 / w1), GetFiboWeight(MAP_SHALLOW_CORRECTION, w2 / w1));
                fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, w4 / w3), GetFiboWeight(MAP_SHALLOW_CORRECTION, w4 / w3));
                
                fiboScore = Math.Pow(fiboScore, 1.0 / 4.0); // Geometric mean of 4 relations
            }
            else if (model == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL || model == ElliottModelType.DIAGONAL_CONTRACTING_ENDING)
            {
                if (isUp && p2.Point.Value <= start.Value) return 0;
                if (!isUp && p2.Point.Value >= start.Value) return 0;
                if (isUp && p4.Point.Value > p1.Point.Value) return 0;
                if (!isUp && p4.Point.Value < p1.Point.Value) return 0;
                if (w3 >= w1 || w5 >= w3) return 0;

                fiboScore *= GetFiboWeight(CONTRACTING_DIAGONAL_3_TO_1, w3 / w1);
                fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, w2 / w1), GetFiboWeight(MAP_SHALLOW_CORRECTION, w2 / w1));
                fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, w4 / w3), GetFiboWeight(MAP_SHALLOW_CORRECTION, w4 / w3));
                
                fiboScore = Math.Pow(fiboScore, 1.0 / 3.0); // Geometric mean of 3 relations
            }
            else if (model == ElliottModelType.TRIANGLE_CONTRACTING)
            {
                if (w2 >= w1) return 0;
                if (w3 >= w2 || w4 >= w3 || w5 >= w4) return 0;

                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w2 / w1);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w3 / w2);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w4 / w3);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w5 / w4);
                
                fiboScore = Math.Pow(fiboScore, 1.0 / 4.0); // Geometric mean of 4 relations
            }
            else if (model == ElliottModelType.TRIANGLE_RUNNING)
            {
                if (w2 <= w1) return 0;
                if (w3 >= w2 || w4 >= w3 || w5 >= w4) return 0;

                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w3 / w2);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w4 / w3);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w5 / w4);
                
                fiboScore = Math.Pow(fiboScore, 1.0 / 3.0); // Geometric mean of 3 relations
            }

            double rankAvg = ((p1.Rank - minRank) + (p2.Rank - minRank) + (p3.Rank - minRank) + (p4.Rank - minRank)) / 4.0;
            double rankPenalty = Math.Pow(0.8, rankAvg);

            double pc = ElliottWavePatternHelper.ModelRules[model].ProbabilityCoefficient;
            // Override IMPULSE's low default probability so it can compete with ZIGZAG during markup
            if (model == ElliottModelType.IMPULSE) pc = 2.5;

            return fiboScore * rankPenalty * pc;
        }

        private MarkupResult CreateResult(ElliottModelType model, BarPoint start, BarPoint end, bool isUp, 
            (BarPoint Point, int Rank)[] pts, List<(BarPoint Point, int Rank)> innerPoints, double baseScore, int depth, int maxDepth, string nodeName, byte level)
        {
            var rules = ElliottWavePatternHelper.ModelRules[model].Models;
            string[] keys = rules.Keys.ToArray();

            var result = new MarkupResult
            {
                ModelType = model,
                Start = start,
                End = end,
                Score = baseScore,
                IsUp = isUp,
                NodeName = nodeName,
                Level = level
            };

            BarPoint currentStart = start;
            bool currentIsUp = isUp;
            for (int i = 0; i <= pts.Length; i++)
            {
                BarPoint currentEnd = i < pts.Length ? pts[i].Point : end;
                if (i < pts.Length) result.Boundaries.Add(pts[i].Point);

                int subAllowedModelsMask = 0;
                foreach (var m in rules[keys[i]])
                {
                    if (m != ElliottModelType.DIAGONAL_EXPANDING_INITIAL &&
                        m != ElliottModelType.DIAGONAL_EXPANDING_ENDING &&
                        m != ElliottModelType.TRIANGLE_EXPANDING &&
                        m != ElliottModelType.TRIPLE_ZIGZAG &&
                        m != ElliottModelType.FLAT_REGULAR &&
                        m != ElliottModelType.COMBINATION)
                    {
                        subAllowedModelsMask |= (1 << (int)m);
                    }
                }
                subAllowedModelsMask |= (1 << (int)ElliottModelType.SIMPLE_IMPULSE);

                var subResult = FindBestModel(currentStart, currentEnd, currentIsUp, innerPoints, subAllowedModelsMask, depth + 1, maxDepth, keys[i], (byte)(level + 1));
                
                if (subResult == null) return null; // Invalid sub-wave
                
                result.SubWaves.Add(subResult);
                
                currentStart = currentEnd;
                currentIsUp = !currentIsUp;
            }

            // Geometric mean of subwave scores to avoid penalizing complex models
            if (result.SubWaves.Count > 0)
            {
                double totalSubScore = 1.0;
                foreach (var sub in result.SubWaves)
                {
                    totalSubScore *= sub.Score;
                }
                result.Score *= Math.Pow(totalSubScore, 1.0 / result.SubWaves.Count);
            }

            return result;
        }
    }
}
