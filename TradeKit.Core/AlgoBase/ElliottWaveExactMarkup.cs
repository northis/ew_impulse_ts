using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Implements exact Elliott Wave markup algorithm using dynamic programming bottom-up parsing.
    /// </summary>
    public partial class ElliottWaveExactMarkup
    {
        private const int MaxHypothesesPerNode = 500;

        /// <summary>
        /// Defines the target wave models that the algorithm attempts to identify.
        /// </summary>
        public static readonly ElliottModelType[] TargetModels = {
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
            if (type == ElliottModelType.IMPULSE || type == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL || type == ElliottModelType.DIAGONAL_CONTRACTING_ENDING)
                return waveNum.ToString();
            if (type == ElliottModelType.ZIGZAG || type == ElliottModelType.FLAT_EXTENDED || type == ElliottModelType.FLAT_RUNNING)
                return waveNum == 1 ? "a" : (waveNum == 2 ? "b" : "c");
            if (type == ElliottModelType.DOUBLE_ZIGZAG)
                return waveNum == 1 ? "w" : (waveNum == 2 ? "x" : "y");
            if (type == ElliottModelType.TRIANGLE_CONTRACTING || type == ElliottModelType.TRIANGLE_RUNNING)
                return ((char)('a' + waveNum - 1)).ToString();
            return "";
        }

        /// <summary>
        /// Gets the expected total number of sub-waves for a given Elliott wave model type.
        /// </summary>
        public static int GetExpectedWaves(ElliottModelType type)
        {
            if (type == ElliottModelType.IMPULSE || type == ElliottModelType.TRIANGLE_CONTRACTING || 
                type == ElliottModelType.TRIANGLE_RUNNING || type == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL || 
                type == ElliottModelType.DIAGONAL_CONTRACTING_ENDING) return 5;
            if (type == ElliottModelType.SIMPLE_IMPULSE) return 1;
            return 3;
        }

        private bool IsAllowed(ElliottModelType parentType, int waveNum, ElliottModelType childType)
        {
            if (childType == ElliottModelType.SIMPLE_IMPULSE) return true;
            
            string key = GetWaveKey(parentType, waveNum);
            if (string.IsNullOrEmpty(key)) return false;
            
            if (ElliottWavePatternHelper.ModelRules.TryGetValue(parentType, out ModelRules rules))
            {
                if (rules.Models.TryGetValue(key, out ElliottModelType[] allowed))
                {
                    return allowed.Contains(childType);
                }
            }
            return false;
        }

        /// <summary>
        /// Calculates Fibonacci price projections for missing sub-waves in an incomplete wave node.
        /// </summary>
        /// <param name='node'>The incomplete parsed wave node.</param>
        /// <returns>A list of tuples containing projected price values, future bar indices, and projection names.</returns>
        public List<(double Value, int BarIndex, string Name)> GetProjections(ExactParsedNode node)
        {
            var projections = new List<(double Value, int BarIndex, string Name)>();
            if (node.WaveCount >= node.ExpectedWaves) return projections;

            int currentIndex = node.EndPoint.BarIndex;
            double currentValue = node.EndPoint.Value;
            bool isUp = node.SubWaves[node.WaveCount - 1].IsUp;
            bool nextIsUp = !isUp;

            double[] wavePriceLengths = new double[node.ExpectedWaves];
            int[] waveTimeLengths = new int[node.ExpectedWaves];
            
            for (int i = 0; i < node.WaveCount; i++)
            {
                wavePriceLengths[i] = node.SubWaves[i].Length;
                waveTimeLengths[i] = Math.Max(1, node.SubWaves[i].EndIndex - node.SubWaves[i].StartIndex);
            }

            int avgTimeLength = (int)waveTimeLengths.Take(node.WaveCount).Average();

            for (int w = node.WaveCount + 1; w <= node.ExpectedWaves; w++)
            {
                double projPriceLength = 0;
                
                if (node.ModelType == ElliottModelType.IMPULSE)
                {
                    if (w == 2) projPriceLength = wavePriceLengths[0] * 0.618;
                    else if (w == 3) projPriceLength = wavePriceLengths[0] * 1.618;
                    else if (w == 4) projPriceLength = wavePriceLengths[2] * 0.382;
                    else if (w == 5) projPriceLength = wavePriceLengths[0] * 1.0;
                }
                else if (node.ModelType == ElliottModelType.ZIGZAG || node.ModelType == ElliottModelType.DOUBLE_ZIGZAG)
                {
                    if (w == 2) projPriceLength = wavePriceLengths[0] * 0.618;
                    else if (w == 3) projPriceLength = wavePriceLengths[0] * 1.0;
                }
                else if (node.ModelType == ElliottModelType.FLAT_EXTENDED)
                {
                    if (w == 2) projPriceLength = wavePriceLengths[0] * 1.272;
                    else if (w == 3) projPriceLength = wavePriceLengths[0] * 1.618;
                }
                else if (node.ModelType == ElliottModelType.FLAT_RUNNING)
                {
                    if (w == 2) projPriceLength = wavePriceLengths[0] * 1.272;
                    else if (w == 3) projPriceLength = wavePriceLengths[0] * 1.0;
                }
                else if (node.ModelType == ElliottModelType.TRIANGLE_CONTRACTING)
                {
                    if (w >= 2) projPriceLength = wavePriceLengths[w - 2] * 0.618;
                }
                else
                {
                    if (w >= 2) projPriceLength = wavePriceLengths[0] * 0.618;
                }

                if (projPriceLength == 0) projPriceLength = wavePriceLengths[0] * 0.5;

                double nextValue = nextIsUp ? currentValue + projPriceLength : currentValue - projPriceLength;
                int nextIndex = currentIndex + avgTimeLength;
                string nextName = GetWaveKey(node.ModelType, w);

                projections.Add((nextValue, nextIndex, nextName));

                wavePriceLengths[w - 1] = projPriceLength;
                waveTimeLengths[w - 1] = avgTimeLength;

                currentValue = nextValue;
                currentIndex = nextIndex;
                nextIsUp = !nextIsUp;
            }

            return projections;
        }

        /// <summary>
        /// Parses a sequence of extremum points to find the most probable Elliott Wave structures.
        /// </summary>
        /// <param name='points'>The sequence of extremum price points.</param>
        /// <returns>A list of the most probable completed wave structures, ordered by score descending.</returns>
        public List<ExactParsedNode> Parse(List<BarPoint> points)
        {
            int n = points.Count - 1;
            if (n <= 0) return new List<ExactParsedNode>();

            List<ExactParsedNode>[,] dp = new List<ExactParsedNode>[n, n];
            double[,] minScore = new double[n, n];

            for (int i = 0; i < n; i++)
            {
                for (int j = i; j < n; j++)
                {
                    dp[i, j] = new List<ExactParsedNode>();
                    minScore[i, j] = -1.0;
                }
            }

            // Init length 1
            for (int i = 0; i < n; i++)
            {
                ExactParsedNode seg = new ExactParsedNode
                {
                    ModelType = ElliottModelType.SIMPLE_IMPULSE,
                    WaveCount = 1,
                    ExpectedWaves = 1,
                    StartIndex = i,
                    EndIndex = i,
                    StartPoint = points[i],
                    EndPoint = points[i + 1],
                    IsUp = points[i + 1].Value > points[i].Value,
                    Score = 1.0,
                    SubWaves = new ExactParsedNode[1]
                };
                
                dp[i, i].Add(seg);
                Promote(seg, dp[i, i], minScore, i, i);
            }

            // DP over lengths
            for (int l = 2; l <= n; l++)
            {
                for (int i = 0; i <= n - l; i++)
                {
                    int j = i + l - 1;
                    
                    for (int m = i; m < j; m++)
                    {
                        List<ExactParsedNode> leftNodes = dp[i, m];
                        List<ExactParsedNode> rightNodes = dp[m + 1, j];
                        
                        foreach (ExactParsedNode p in leftNodes)
                        {
                            if (p.WaveCount >= p.ExpectedWaves) continue;
                            
                            foreach (ExactParsedNode c in rightNodes)
                            {
                                if (c.WaveCount < c.ExpectedWaves) continue;
                                
                                ExactParsedNode lastWave = p.SubWaves[p.WaveCount - 1];
                                if (lastWave.IsUp == c.IsUp) continue;
                                
                                int nextWaveNum = p.WaveCount + 1;
                                if (!IsAllowed(p.ModelType, nextWaveNum, c.ModelType)) continue;
                                
                                if (!CheckWaveConstraints(p, c, nextWaveNum)) continue;
                                
                                double predictedScore = CalculateScore(p, c);
                                if (dp[i, j].Count >= MaxHypothesesPerNode && predictedScore <= minScore[i, j]) continue;

                                if (nextWaveNum == p.ExpectedWaves && !CheckFinalConstraints(p, c)) continue;

                                ExactParsedNode nextP = CloneAndAppend(p, c);
                                nextP.Score = predictedScore;
                                
                                dp[i, j].Add(nextP);
                                
                                if (nextP.WaveCount == nextP.ExpectedWaves)
                                {
                                    Promote(nextP, dp[i, j], minScore, i, j);
                                }
                                
                                if (dp[i, j].Count > MaxHypothesesPerNode * 2)
                                {
                                    Prune(dp[i, j], MaxHypothesesPerNode);
                                    if (dp[i, j].Count == MaxHypothesesPerNode) minScore[i, j] = dp[i, j][MaxHypothesesPerNode - 1].Score;
                                }
                            }
                        }
                    }
                    
                    Prune(dp[i, j], MaxHypothesesPerNode);
                }
            }
            
            List<ExactParsedNode> results = new List<ExactParsedNode>();
            results.AddRange(dp[0, n - 1]);

            return results.OrderByDescending(x => x.Score).ToList();
        }

        private ExactParsedNode CloneAndAppend(ExactParsedNode p, ExactParsedNode c)
        {
            ExactParsedNode next = new ExactParsedNode
            {
                ModelType = p.ModelType,
                WaveCount = p.WaveCount + 1,
                ExpectedWaves = p.ExpectedWaves,
                StartIndex = p.StartIndex,
                EndIndex = c.EndIndex,
                StartPoint = p.StartPoint,
                EndPoint = c.EndPoint,
                IsUp = p.IsUp,
                SubWaves = new ExactParsedNode[p.ExpectedWaves]
            };
            Array.Copy(p.SubWaves, next.SubWaves, p.WaveCount);
            next.SubWaves[p.WaveCount] = c;
            return next;
        }

        private void Promote(ExactParsedNode completeNode, List<ExactParsedNode> targetList, double[,] minScore = null, int i = 0, int j = 0)
        {
            // Enforce that the net movement direction matches the IsUp flag!
            // This mirrors the extrema-based logic of the old algorithm and prevents
            // "corrective" waves from having a net movement that extends the trend.
            if (completeNode.ModelType != ElliottModelType.SIMPLE_IMPULSE)
            {
                if (completeNode.IsUp && completeNode.EndPoint.Value <= completeNode.StartPoint.Value) return;
                if (!completeNode.IsUp && completeNode.EndPoint.Value >= completeNode.StartPoint.Value) return;
            }

            foreach (ElliottModelType targetModel in TargetModels)
            {
                int expected = GetExpectedWaves(targetModel);
                if (IsAllowed(targetModel, 1, completeNode.ModelType))
                {
                    ExactParsedNode partial = new ExactParsedNode
                    {
                        ModelType = targetModel,
                        WaveCount = 1,
                        ExpectedWaves = expected,
                        StartIndex = completeNode.StartIndex,
                        EndIndex = completeNode.EndIndex,
                        StartPoint = completeNode.StartPoint,
                        EndPoint = completeNode.EndPoint,
                        IsUp = completeNode.IsUp,
                        SubWaves = new ExactParsedNode[expected]
                    };
                    partial.SubWaves[0] = completeNode;
                    double pScore = CalculateScore(partial);
                    
                    if (minScore != null && targetList.Count >= MaxHypothesesPerNode && pScore <= minScore[i, j])
                    {
                        continue;
                    }
                    
                    partial.Score = pScore;
                    targetList.Add(partial);
                }
            }
        }

        private void Prune(List<ExactParsedNode> list, int maxItems)
        {
            if (list.Count <= maxItems) return;
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            list.RemoveRange(maxItems, list.Count - maxItems);
        }

        
        private double GetWaveLength(ExactParsedNode p, ExactParsedNode c, int index)
        {
            if (index < p.WaveCount) return p.SubWaves[index].Length;
            return c.Length;
        }

        private ElliottModelType GetWaveModelType(ExactParsedNode p, ExactParsedNode c, int index)
        {
            if (index < p.WaveCount) return p.SubWaves[index].ModelType;
            return c.ModelType;
        }

        private double GetWaveScore(ExactParsedNode p, ExactParsedNode c, int index)
        {
            if (index < p.WaveCount) return p.SubWaves[index].Score;
            return c.Score;
        }

private double CalculateScore(ExactParsedNode p, ExactParsedNode c = null)
        {
            double pc = 1.0;
            if (ElliottWavePatternHelper.ModelRules.TryGetValue(p.ModelType, out ModelRules rules))
            {
                pc = rules.ProbabilityCoefficient;
            }
            if (p.ModelType == ElliottModelType.IMPULSE) pc *= 5.0; // Significant bump to prefer clean impulses over nested corrections

            double subwavesScore = 1.0;
            int totalWaveCount = p.WaveCount + (c != null ? 1 : 0);
            for (int i = 0; i < totalWaveCount; i++)
            {
                if (true)
                    subwavesScore *= GetWaveScore(p, c, i);
            }
            double avgSubwaveScore = totalWaveCount > 0 ? Math.Pow(subwavesScore, 1.0 / totalWaveCount) : 1.0;

            double fiboScore = 1.0;
            int fiboCount = 0;

            if (p.ModelType == ElliottModelType.IMPULSE)
            {
                if (totalWaveCount >= 2)
                {
                    double w1 = GetWaveLength(p, c, 0);
                    double w2 = GetWaveLength(p, c, 1);
                    if (w1 > 0)
                    {
                        fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, w2 / w1), GetFiboWeight(MAP_SHALLOW_CORRECTION, w2 / w1));
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 3)
                {
                    double w1 = GetWaveLength(p, c, 0);
                    double w3 = GetWaveLength(p, c, 2);
                    if (w1 > 0)
                    {
                        fiboScore *= GetFiboWeight(IMPULSE_3_TO_1, w3 / w1);
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 4)
                {
                    double w3 = GetWaveLength(p, c, 2);
                    double w4 = GetWaveLength(p, c, 3);
                    if (w3 > 0)
                    {
                        fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, w4 / w3), GetFiboWeight(MAP_SHALLOW_CORRECTION, w4 / w3));
                        fiboCount++;
                    }
                    
                    ElliottModelType t2 = GetWaveModelType(p, c, 1);
                    ElliottModelType t4 = GetWaveModelType(p, c, 3);
                    bool isW2Deep = ElliottWavePatternHelper.DeepCorrections.Contains(t2);
                    bool isW4Deep = ElliottWavePatternHelper.DeepCorrections.Contains(t4);
                    if (isW2Deep != isW4Deep)
                    {
                        pc *= 1.2;
                    }
                }
                if (totalWaveCount >= 5)
                {
                    double w1 = GetWaveLength(p, c, 0);
                    double w5 = GetWaveLength(p, c, 4);
                    if (w1 > 0)
                    {
                        fiboScore *= GetFiboWeight(IMPULSE_5_TO_1, w5 / w1);
                        fiboCount++;
                    }
                }
            }
            else if (p.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL || p.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_ENDING)
            {
                if (totalWaveCount >= 2)
                {
                    double w1 = GetWaveLength(p, c, 0);
                    double w2 = GetWaveLength(p, c, 1);
                    if (w1 > 0)
                    {
                        fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, w2 / w1), GetFiboWeight(MAP_SHALLOW_CORRECTION, w2 / w1));
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 3)
                {
                    double w1 = GetWaveLength(p, c, 0);
                    double w3 = GetWaveLength(p, c, 2);
                    if (w1 > 0)
                    {
                        fiboScore *= GetFiboWeight(CONTRACTING_DIAGONAL_3_TO_1, w3 / w1);
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 4)
                {
                    double w3 = GetWaveLength(p, c, 2);
                    double w4 = GetWaveLength(p, c, 3);
                    if (w3 > 0)
                    {
                        fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, w4 / w3), GetFiboWeight(MAP_SHALLOW_CORRECTION, w4 / w3));
                        fiboCount++;
                    }
                }
            }
            else if (p.ModelType == ElliottModelType.ZIGZAG || p.ModelType == ElliottModelType.DOUBLE_ZIGZAG)
            {
                if (totalWaveCount >= 2)
                {
                    double wA = GetWaveLength(p, c, 0);
                    double wB = GetWaveLength(p, c, 1);
                    if (wA > 0)
                    {
                        fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, wB / wA), GetFiboWeight(MAP_SHALLOW_CORRECTION, wB / wA));
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 3 && p.ModelType == ElliottModelType.ZIGZAG)
                {
                    double wA = GetWaveLength(p, c, 0);
                    double wC = GetWaveLength(p, c, 2);
                    if (wA > 0)
                    {
                        fiboScore *= GetFiboWeight(ZIGZAG_C_TO_A, wC / wA);
                        fiboCount++;
                    }
                }
            }
            else if (p.ModelType == ElliottModelType.FLAT_EXTENDED)
            {
                if (totalWaveCount >= 2)
                {
                    double wA = GetWaveLength(p, c, 0);
                    double wB = GetWaveLength(p, c, 1);
                    if (wA > 0)
                    {
                        fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, wB / wA), GetFiboWeight(MAP_SHALLOW_CORRECTION, wB / wA));
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 3)
                {
                    double wA = GetWaveLength(p, c, 0);
                    double wC = GetWaveLength(p, c, 2);
                    if (wA > 0)
                    {
                        fiboScore *= GetFiboWeight(MAP_EX_FLAT_WAVE_C_TO_A, wC / wA);
                        fiboCount++;
                    }
                }
            }
            else if (p.ModelType == ElliottModelType.FLAT_RUNNING)
            {
                if (totalWaveCount >= 2)
                {
                    double wA = GetWaveLength(p, c, 0);
                    double wB = GetWaveLength(p, c, 1);
                    if (wA > 0)
                    {
                        fiboScore *= Math.Max(GetFiboWeight(MAP_DEEP_CORRECTION, wB / wA), GetFiboWeight(MAP_SHALLOW_CORRECTION, wB / wA));
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 3)
                {
                    double wA = GetWaveLength(p, c, 0);
                    double wC = GetWaveLength(p, c, 2);
                    if (wA > 0)
                    {
                        fiboScore *= GetFiboWeight(MAP_RUNNING_FLAT_WAVE_C_TO_A, wC / wA);
                        fiboCount++;
                    }
                }
            }
            else if (p.ModelType == ElliottModelType.TRIANGLE_CONTRACTING || p.ModelType == ElliottModelType.TRIANGLE_RUNNING)
            {
                if (totalWaveCount >= 2)
                {
                    double w1 = GetWaveLength(p, c, 0);
                    double w2 = GetWaveLength(p, c, 1);
                    if (w1 > 0)
                    {
                        fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w2 / w1);
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 3)
                {
                    double w2 = GetWaveLength(p, c, 1);
                    double w3 = GetWaveLength(p, c, 2);
                    if (w2 > 0)
                    {
                        fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w3 / w2);
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 4)
                {
                    double w3 = GetWaveLength(p, c, 2);
                    double w4 = GetWaveLength(p, c, 3);
                    if (w3 > 0)
                    {
                        fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w4 / w3);
                        fiboCount++;
                    }
                }
                if (totalWaveCount >= 5)
                {
                    double w4 = GetWaveLength(p, c, 3);
                    double w5 = GetWaveLength(p, c, 4);
                    if (w4 > 0)
                    {
                        fiboScore *= GetFiboWeight(MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV, w5 / w4);
                        fiboCount++;
                    }
                }
            }

            int expectedFiboCount = 0;
            if (p.ModelType == ElliottModelType.IMPULSE) expectedFiboCount = 4;
            else if (p.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL || p.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_ENDING) expectedFiboCount = 3;
            else if (p.ModelType == ElliottModelType.ZIGZAG || p.ModelType == ElliottModelType.DOUBLE_ZIGZAG) expectedFiboCount = 2;
            else if (p.ModelType == ElliottModelType.FLAT_EXTENDED || p.ModelType == ElliottModelType.FLAT_RUNNING) expectedFiboCount = 2;
            else if (p.ModelType == ElliottModelType.TRIANGLE_CONTRACTING || p.ModelType == ElliottModelType.TRIANGLE_RUNNING) expectedFiboCount = 4;

            double paddedFiboScore = 1.0;
            if (expectedFiboCount > 0)
            {
                int missingFibos = expectedFiboCount - fiboCount;
                // Treat missing fibos as relatively low probability so complete patterns win
                double totalFibo = fiboScore * Math.Pow(0.1, missingFibos);
                paddedFiboScore = Math.Pow(totalFibo, 1.0 / expectedFiboCount);
            }

            double score = pc * avgSubwaveScore * paddedFiboScore;

            if (totalWaveCount < p.ExpectedWaves)
            {
                score *= Math.Pow(0.6, p.ExpectedWaves - totalWaveCount);
            }

            return score;
        }
        private double GetRatioScore(double ratio)
        {
            double[] fibs = { 0.236, 0.382, 0.5, 0.618, 0.786, 1.0, 1.272, 1.618, 2.618 };
            double bestDiff = 1000;
            foreach (double f in fibs)
            {
                bestDiff = Math.Min(bestDiff, Math.Abs(ratio - f));
            }
            if (bestDiff < 0.05) return 1.2;
            if (bestDiff < 0.1) return 1.1;
            return 1.0;
        }

        private bool CheckWaveConstraints(ExactParsedNode p, ExactParsedNode c, int waveNum)
        {
            double start = p.StartPoint.Value;
            double end1 = p.SubWaves[0].EndPoint.Value;
            bool isUp = p.IsUp;
            
            switch (p.ModelType)
            {
                case ElliottModelType.IMPULSE:
                    if (waveNum == 2)
                    {
                        if (isUp && c.EndPoint.Value <= start) return false;
                        if (!isUp && c.EndPoint.Value >= start) return false;
                    }
                    else if (waveNum == 3)
                    {
                        if (isUp && c.EndPoint.Value <= end1) return false;
                        if (!isUp && c.EndPoint.Value >= end1) return false;
                    }
                    else if (waveNum == 4)
                    {
                        if (isUp && c.EndPoint.Value <= end1) return false;
                        if (!isUp && c.EndPoint.Value >= end1) return false;
                    }
                    break;
                    
                case ElliottModelType.DIAGONAL_CONTRACTING_INITIAL:
                case ElliottModelType.DIAGONAL_CONTRACTING_ENDING:
                    if (waveNum == 2)
                    {
                        if (isUp && c.EndPoint.Value <= start) return false;
                        if (!isUp && c.EndPoint.Value >= start) return false;
                    }
                    else if (waveNum == 3)
                    {
                        if (c.Length >= p.SubWaves[0].Length) return false;
                        if (isUp && c.EndPoint.Value <= end1) return false;
                        if (!isUp && c.EndPoint.Value >= end1) return false;
                    }
                    else if (waveNum == 4)
                    {
                        if (c.Length >= p.SubWaves[1].Length) return false;
                        if (isUp && c.EndPoint.Value > end1) return false;
                        if (!isUp && c.EndPoint.Value < end1) return false;
                    }
                    else if (waveNum == 5)
                    {
                        if (c.Length >= p.SubWaves[2].Length) return false;
                        double end3 = p.SubWaves[2].EndPoint.Value;
                        if (p.ModelType == ElliottModelType.DIAGONAL_CONTRACTING_INITIAL)
                        {
                            if (isUp && c.EndPoint.Value <= end3) return false;
                            if (!isUp && c.EndPoint.Value >= end3) return false;
                        }
                    }
                    break;
                    
                case ElliottModelType.ZIGZAG:
                    if (waveNum == 2)
                    {
                        if (isUp && c.EndPoint.Value <= start) return false;
                        if (!isUp && c.EndPoint.Value >= start) return false;
                    }
                    else if (waveNum == 3)
                    {
                        if (isUp && c.EndPoint.Value <= end1) return false;
                        if (!isUp && c.EndPoint.Value >= end1) return false;
                    }
                    break;
                    
                case ElliottModelType.DOUBLE_ZIGZAG:
                    if (waveNum == 2)
                    {
                        if (isUp && c.EndPoint.Value <= start) return false;
                        if (!isUp && c.EndPoint.Value >= start) return false;
                    }
                    else if (waveNum == 3)
                    {
                        if (isUp && c.EndPoint.Value <= end1) return false;
                        if (!isUp && c.EndPoint.Value >= end1) return false;
                    }
                    break;
                    
                case ElliottModelType.FLAT_EXTENDED:
                    if (waveNum == 2)
                    {
                        if (c.Length < p.SubWaves[0].Length * 0.5) return false;
                    }
                    else if (waveNum == 3)
                    {
                        if (isUp && c.EndPoint.Value <= end1) return false;
                        if (!isUp && c.EndPoint.Value >= end1) return false;
                    }
                    break;
                    
                case ElliottModelType.FLAT_RUNNING:
                    if (waveNum == 2)
                    {
                        if (isUp && c.EndPoint.Value >= start) return false;
                        if (!isUp && c.EndPoint.Value <= start) return false;
                    }
                    else if (waveNum == 3)
                    {
                        if (isUp && c.EndPoint.Value >= end1) return false;
                        if (!isUp && c.EndPoint.Value <= end1) return false;
                    }
                    break;
                    
                case ElliottModelType.TRIANGLE_CONTRACTING:
                    if (waveNum >= 2)
                    {
                        if (c.Length >= p.SubWaves[waveNum - 2].Length) return false;
                    }
                    break;
                    
                case ElliottModelType.TRIANGLE_RUNNING:
                    if (waveNum == 2)
                    {
                        if (isUp && c.EndPoint.Value >= start) return false;
                        if (!isUp && c.EndPoint.Value <= start) return false;
                    }
                    else if (waveNum >= 3)
                    {
                        if (c.Length >= p.SubWaves[waveNum - 2].Length) return false;
                    }
                    break;
            }
            
            return true;
        }

        private bool CheckFinalConstraints(ExactParsedNode p, ExactParsedNode c = null)
        {
            if (p.ModelType == ElliottModelType.IMPULSE)
            {
                double len1 = GetWaveLength(p, c, 0);
                double len3 = GetWaveLength(p, c, 2);
                double len5 = GetWaveLength(p, c, 4);
                if (len3 < len1 && len3 < len5) return false;
            }
            return true;
        }
    }
}
