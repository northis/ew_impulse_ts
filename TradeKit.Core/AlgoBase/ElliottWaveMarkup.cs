using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    public class MarkupResult
    {
        public ElliottModelType ModelType { get; set; }
        public List<BarPoint> Boundaries { get; set; } = new List<BarPoint>();
        public List<MarkupResult> SubWaves { get; set; } = new List<MarkupResult>();
        public double Score { get; set; }
        public BarPoint Start { get; set; }
        public BarPoint End { get; set; }
        public bool IsUp { get; set; }
        public string NodeName { get; set; }
        public byte Level { get; set; }

        public IEnumerable<MarkupResult> Flatten()
        {
            yield return this;
            foreach (var child in SubWaves.SelectMany(w => w.Flatten()))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// Implements Elliott Wave markup generation inside zigzag segments based on ranking.
    /// </summary>
    public class ElliottWaveMarkup
    {
        private static readonly SortedDictionary<byte, double> ZIGZAG_C_TO_A = new() { { 0, 0 }, { 5, 0.618 }, { 25, 0.786 }, { 35, 0.786 }, { 75, 1 }, { 85, 1.618 }, { 90, 2.618 }, { 95, 3.618 } };
        private static readonly SortedDictionary<byte, double> CONTRACTING_DIAGONAL_3_TO_1 = new() { { 0, 0 }, { 5, 0.5 }, { 15, 0.618 }, { 20, 0.786 } };
        private static readonly SortedDictionary<byte, double> IMPULSE_3_TO_1 = new() { { 0, 0 }, { 5, 0.618 }, { 10, 0.786 }, { 15, 1 }, { 25, 1.618 }, { 60, 2.618 }, { 75, 3.618 }, { 90, 4.236 } };
        private static readonly SortedDictionary<byte, double> IMPULSE_5_TO_1 = new() { { 0, 0 }, { 5, 0.382 }, { 10, 0.618 }, { 20, 0.786 }, { 25, 1 }, { 75, 1.618 }, { 85, 2.618 }, { 95, 3.618 }, { 99, 4.236 } };
        private static readonly SortedDictionary<byte, double> MAP_DEEP_CORRECTION = new() { { 0, 0 }, { 5, 0.5 }, { 25, 0.618 }, { 70, 0.786 }, { 99, 0.95 } };
        private static readonly SortedDictionary<byte, double> MAP_SHALLOW_CORRECTION = new() { { 0, 0 }, { 5, 0.236 }, { 35, 0.382 }, { 85, 0.5 } };
        private static readonly SortedDictionary<byte, double> MAP_EX_FLAT_WAVE_C_TO_A = new() { { 0, 0 }, { 20, 1.618 }, { 80, 2.618 }, { 95, 3.618 } };
        private static readonly SortedDictionary<byte, double> MAP_RUNNING_FLAT_WAVE_C_TO_A = new() { { 0, 0 }, { 5, 0.5 }, { 20, 0.618 }, { 80, 1 }, { 90, 1.272 }, { 95, 1.618 } };
        private static readonly SortedDictionary<byte, double> MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV = new() { { 0, 0 }, { 5, 0.5 }, { 20, 0.618 }, { 80, 0.786 }, { 90, 0.9 }, { 95, 0.95 } };

        private readonly Dictionary<string, MarkupResult> m_Cache = new();

        public List<MarkupResult> ParseSegment(BarPoint start, BarPoint end, Dictionary<int, (BarPoint Point, int Rank)> ranksDict)
        {
            m_Cache.Clear();
            var innerPoints = ranksDict.Values.OrderBy(x => x.Point.BarIndex).ToList();
            bool isUp = end.Value > start.Value;

            var allowedMainModels = new List<ElliottModelType>
            {
                ElliottModelType.IMPULSE,
                ElliottModelType.ZIGZAG,
                ElliottModelType.DOUBLE_ZIGZAG,
                ElliottModelType.FLAT_EXTENDED,
                ElliottModelType.FLAT_RUNNING,
                ElliottModelType.TRIANGLE_CONTRACTING,
                ElliottModelType.TRIANGLE_RUNNING,
                ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
                ElliottModelType.DIAGONAL_CONTRACTING_ENDING,
                ElliottModelType.SIMPLE_IMPULSE
            };

            List<MarkupResult> bestCombo = new List<MarkupResult>();
            double bestScore = -1;

            // 1. Main only
            var mainRes = FindBestModel(start, end, isUp, innerPoints, allowedMainModels, 0, "", 0);
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
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        var p1 = candidates[i];
                        var p2 = candidates[j];

                        if (isUp)
                        {
                            if (p1.Point.Value <= start.Value) continue;
                            if (p2.Point.Value >= p1.Point.Value || p2.Point.Value <= start.Value) continue;
                        }
                        else
                        {
                            if (p1.Point.Value >= start.Value) continue;
                            if (p2.Point.Value <= p1.Point.Value || p2.Point.Value >= start.Value) continue;
                        }

                        double wB = GetLen(start, p1.Point); 
                        double wC = GetLen(p1.Point, p2.Point); 
                        if (wB == 0) continue;
                        double tailScore = GetFiboWeight(MAP_EX_FLAT_WAVE_C_TO_A, wC / wB); 

                        var remainingPoints = innerPoints.Where(p => p.Point.BarIndex > p2.Point.BarIndex).ToList();
                        var mainAfterTail = FindBestModel(p2.Point, end, isUp, remainingPoints, allowedMainModels, 0, "", 0);

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
                    for (int j = i + 1; j < candidates.Count - 2; j++)
                    {
                        for (int k = j + 1; k < candidates.Count - 1; k++)
                        {
                            for (int l = k + 1; l < candidates.Count; l++)
                            {
                                var p1 = candidates[i];
                                var p2 = candidates[j];
                                var p3 = candidates[k];
                                var p4 = candidates[l];

                                if (isUp)
                                {
                                    if (p1.Point.Value <= start.Value) continue;
                                    if (p2.Point.Value >= p1.Point.Value || p2.Point.Value <= start.Value) continue;
                                    if (p3.Point.Value <= p2.Point.Value || p3.Point.Value >= p1.Point.Value) continue;
                                    if (p4.Point.Value >= p3.Point.Value || p4.Point.Value <= p2.Point.Value) continue;
                                }
                                else
                                {
                                    if (p1.Point.Value >= start.Value) continue;
                                    if (p2.Point.Value <= p1.Point.Value || p2.Point.Value >= start.Value) continue;
                                    if (p3.Point.Value >= p2.Point.Value || p3.Point.Value <= p1.Point.Value) continue;
                                    if (p4.Point.Value <= p3.Point.Value || p4.Point.Value >= p2.Point.Value) continue;
                                }

                                double wB = GetLen(start, p1.Point); 
                                double wC = GetLen(p1.Point, p2.Point); 
                                double wD = GetLen(p2.Point, p3.Point); 
                                double wE = GetLen(p3.Point, p4.Point); 

                                if (wB == 0 || wC == 0 || wD == 0) continue;

                                double tailScore = GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, wC / wB) *
                                                   GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, wD / wC) *
                                                   GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, wE / wD);

                                var remainingPoints = innerPoints.Where(p => p.Point.BarIndex > p4.Point.BarIndex).ToList();
                                var mainAfterTail = FindBestModel(p4.Point, end, isUp, remainingPoints, allowedMainModels, 0, "", 0);

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
            List<ElliottModelType> allowedModels, int depth, string nodeName, byte level)
        {
            string cacheKey = $"{start.BarIndex}_{end.BarIndex}_{string.Join(",", allowedModels.Select(m => (int)m))}";
            if (m_Cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var validInner = innerPoints.Where(p => p.Point.BarIndex > start.BarIndex && p.Point.BarIndex < end.BarIndex).ToList();

            if (depth >= 6 || validInner.Count < 2)
            {
                MarkupResult simpleRes = null;
                if (allowedModels.Contains(ElliottModelType.SIMPLE_IMPULSE))
                    simpleRes = new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Score = 1.0, Start = start, End = end, IsUp = isUp, NodeName = nodeName, Level = level };
                else if (allowedModels.Contains(ElliottModelType.IMPULSE))
                    simpleRes = new MarkupResult { ModelType = ElliottModelType.IMPULSE, Score = 0.1, Start = start, End = end, IsUp = isUp, NodeName = nodeName, Level = level };
                else if (allowedModels.Contains(ElliottModelType.ZIGZAG))
                    simpleRes = new MarkupResult { ModelType = ElliottModelType.ZIGZAG, Score = 0.05, Start = start, End = end, IsUp = isUp, NodeName = nodeName, Level = level };
                
                if (simpleRes != null) m_Cache[cacheKey] = simpleRes;
                return simpleRes;
            }

            MarkupResult bestResult = null;
            double bestScore = -1;

            if (allowedModels.Contains(ElliottModelType.SIMPLE_IMPULSE))
            {
                double score = ElliottWavePatternHelper.ModelRules[ElliottModelType.SIMPLE_IMPULSE].ProbabilityCoefficient;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestResult = new MarkupResult { ModelType = ElliottModelType.SIMPLE_IMPULSE, Score = score, Start = start, End = end, IsUp = isUp, NodeName = nodeName, Level = level };
                }
            }

            int minRank = validInner.Min(p => p.Rank);
            var candidates = validInner.Where(p => p.Rank <= minRank + 2).ToList();

            var threeWaveModels = allowedModels.Intersect(new[] { ElliottModelType.ZIGZAG, ElliottModelType.DOUBLE_ZIGZAG, ElliottModelType.FLAT_EXTENDED, ElliottModelType.FLAT_RUNNING }).ToList();
            if (threeWaveModels.Count > 0 && candidates.Count >= 2)
            {
                for (int i = 0; i < candidates.Count - 1; i++)
                {
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        var p1 = candidates[i];
                        var p2 = candidates[j];

                        if (isUp)
                        {
                            if (p1.Point.Value <= start.Value && !allowedModels.Contains(ElliottModelType.FLAT_EXTENDED) && !allowedModels.Contains(ElliottModelType.FLAT_RUNNING)) continue;
                            if (p2.Point.Value >= p1.Point.Value) continue;
                        }
                        else
                        {
                            if (p1.Point.Value >= start.Value && !allowedModels.Contains(ElliottModelType.FLAT_EXTENDED) && !allowedModels.Contains(ElliottModelType.FLAT_RUNNING)) continue;
                            if (p2.Point.Value <= p1.Point.Value) continue;
                        }

                        foreach (var model in threeWaveModels)
                        {
                            double score = Evaluate3WaveModel(model, start, end, isUp, minRank, p1, p2);
                            if (score > 0)
                            {
                                var result = CreateResult(model, start, end, isUp, new[] { p1, p2 }, validInner, score, depth, nodeName, level);
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

            var fiveWaveModels = allowedModels.Intersect(new[] { ElliottModelType.IMPULSE, ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, ElliottModelType.DIAGONAL_CONTRACTING_ENDING, ElliottModelType.TRIANGLE_CONTRACTING, ElliottModelType.TRIANGLE_RUNNING }).ToList();
            if (fiveWaveModels.Count > 0 && candidates.Count >= 4)
            {
                for (int i = 0; i < candidates.Count - 3; i++)
                {
                    for (int j = i + 1; j < candidates.Count - 2; j++)
                    {
                        for (int k = j + 1; k < candidates.Count - 1; k++)
                        {
                            for (int l = k + 1; l < candidates.Count; l++)
                            {
                                var p1 = candidates[i];
                                var p2 = candidates[j];
                                var p3 = candidates[k];
                                var p4 = candidates[l];

                                if (isUp)
                                {
                                    if (p1.Point.Value <= start.Value && !allowedModels.Contains(ElliottModelType.TRIANGLE_CONTRACTING) && !allowedModels.Contains(ElliottModelType.TRIANGLE_RUNNING)) continue;
                                    if (p2.Point.Value >= p1.Point.Value) continue;
                                    if (p3.Point.Value <= p2.Point.Value) continue;
                                    if (p4.Point.Value >= p3.Point.Value) continue;
                                }
                                else
                                {
                                    if (p1.Point.Value >= start.Value && !allowedModels.Contains(ElliottModelType.TRIANGLE_CONTRACTING) && !allowedModels.Contains(ElliottModelType.TRIANGLE_RUNNING)) continue;
                                    if (p2.Point.Value <= p1.Point.Value) continue;
                                    if (p3.Point.Value >= p2.Point.Value) continue;
                                    if (p4.Point.Value <= p3.Point.Value) continue;
                                }

                                foreach (var model in fiveWaveModels)
                                {
                                    double score = Evaluate5WaveModel(model, start, end, isUp, minRank, p1, p2, p3, p4);
                                    if (score > 0)
                                    {
                                        var result = CreateResult(model, start, end, isUp, new[] { p1, p2, p3, p4 }, validInner, score, depth, nodeName, level);
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

            m_Cache[cacheKey] = bestResult;
            return bestResult;
        }

        private double GetLen(BarPoint a, BarPoint b) => Math.Abs(a.Value - b.Value);

        private double GetFiboWeight(SortedDictionary<byte, double> map, double ratio)
        {
            byte maxWeight = 1;
            foreach (var kvp in map)
            {
                if (ratio >= kvp.Value)
                {
                    if (kvp.Key > maxWeight)
                        maxWeight = kvp.Key;
                }
            }
            return Math.Max(0.01, maxWeight / 100.0);
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
            }
            else if (model == ElliottModelType.FLAT_RUNNING)
            {
                if (isUp && p2.Point.Value > start.Value) return 0;
                if (!isUp && p2.Point.Value < start.Value) return 0;
                if (isUp && end.Value > p1.Point.Value) return 0;
                if (!isUp && end.Value < p1.Point.Value) return 0;

                fiboScore *= GetFiboWeight(MAP_RUNNING_FLAT_WAVE_C_TO_A, wC / wA);
            }

            double rankPenalty = Math.Pow(0.8, p1.Rank - minRank) * Math.Pow(0.8, p2.Rank - minRank);
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
            }
            else if (model == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL || model == ElliottModelType.DIAGONAL_CONTRACTING_ENDING)
            {
                if (isUp && p2.Point.Value <= start.Value) return 0;
                if (!isUp && p2.Point.Value >= start.Value) return 0;
                if (isUp && p4.Point.Value > p1.Point.Value) return 0;
                if (!isUp && p4.Point.Value < p1.Point.Value) return 0;
                if (w3 >= w1 || w5 >= w3) return 0;

                fiboScore *= GetFiboWeight(CONTRACTING_DIAGONAL_3_TO_1, w3 / w1);
            }
            else if (model == ElliottModelType.TRIANGLE_CONTRACTING)
            {
                if (w2 >= w1) return 0;
                if (w3 >= w2 || w4 >= w3 || w5 >= w4) return 0;

                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w2 / w1);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w3 / w2);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w4 / w3);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w5 / w4);
            }
            else if (model == ElliottModelType.TRIANGLE_RUNNING)
            {
                if (w2 <= w1) return 0;
                if (w3 >= w2 || w4 >= w3 || w5 >= w4) return 0;

                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w3 / w2);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w4 / w3);
                fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w5 / w4);
            }

            double rankPenalty = Math.Pow(0.8, p1.Rank - minRank) * Math.Pow(0.8, p2.Rank - minRank) * Math.Pow(0.8, p3.Rank - minRank) * Math.Pow(0.8, p4.Rank - minRank);
            return fiboScore * rankPenalty * ElliottWavePatternHelper.ModelRules[model].ProbabilityCoefficient;
        }

        private MarkupResult CreateResult(ElliottModelType model, BarPoint start, BarPoint end, bool isUp, 
            (BarPoint Point, int Rank)[] pts, List<(BarPoint Point, int Rank)> innerPoints, double baseScore, int depth, string nodeName, byte level)
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
            for (int i = 0; i <= pts.Length; i++)
            {
                BarPoint currentEnd = i < pts.Length ? pts[i].Point : end;
                if (i < pts.Length) result.Boundaries.Add(pts[i].Point);

                var allowedModels = rules[keys[i]].ToList();
                
                allowedModels.RemoveAll(m => 
                    m == ElliottModelType.DIAGONAL_EXPANDING_INITIAL ||
                    m == ElliottModelType.DIAGONAL_EXPANDING_ENDING ||
                    m == ElliottModelType.TRIANGLE_EXPANDING ||
                    m == ElliottModelType.TRIPLE_ZIGZAG ||
                    m == ElliottModelType.FLAT_REGULAR ||
                    m == ElliottModelType.COMBINATION);
                    
                if (allowedModels.Contains(ElliottModelType.IMPULSE))
                    allowedModels.Add(ElliottModelType.SIMPLE_IMPULSE);

                var subResult = FindBestModel(currentStart, currentEnd, !isUp, innerPoints, allowedModels, depth + 1, keys[i], (byte)(level + 1));
                
                if (subResult == null) return null; // Invalid sub-wave
                
                result.SubWaves.Add(subResult);
                result.Score *= subResult.Score;
                
                currentStart = currentEnd;
                isUp = !isUp;
            }

            return result;
        }
    }
}
