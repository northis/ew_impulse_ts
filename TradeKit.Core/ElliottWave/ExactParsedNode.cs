using System.Collections.Generic;
using TradeKit.Core.Common;
using TradeKit.Core.AlgoBase;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Represents a parsed node in the Elliott Wave hierarchical structure.
    /// </summary>
    public class ExactParsedNode
    {
        /// <summary>
        /// Gets or sets the type of the Elliott wave model.
        /// </summary>
        public ElliottModelType ModelType { get; set; }
        /// <summary>
        /// Gets or sets the current count of parsed sub-waves.
        /// </summary>
        public int WaveCount { get; set; }
        /// <summary>
        /// Gets or sets the total expected number of sub-waves for this model.
        /// </summary>
        public int ExpectedWaves { get; set; }
        
        /// <summary>
        /// Gets or sets the starting index of the wave in the original points list.
        /// </summary>
        public int StartIndex { get; set; }
        /// <summary>
        /// Gets or sets the ending index of the wave in the original points list.
        /// </summary>
        public int EndIndex { get; set; }
        
        /// <summary>
        /// Gets or sets the starting point of the wave.
        /// </summary>
        public BarPoint StartPoint { get; set; }
        /// <summary>
        /// Gets or sets the ending point of the wave.
        /// </summary>
        public BarPoint EndPoint { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether the wave is moving upwards.
        /// </summary>
        public bool IsUp { get; set; }
        /// <summary>
        /// Gets or sets the calculated score of this wave model based on rules and fibonacci ratios.
        /// </summary>
        public double Score { get; set; }
        
        /// <summary>
        /// Gets or sets the array of parsed sub-waves.
        /// </summary>
        public ExactParsedNode[] SubWaves { get; set; }
        
        /// <summary>
        /// Gets the absolute price length of the wave.
        /// </summary>
        public double Length => Math.Abs(EndPoint.Value - StartPoint.Value);
        
        /// <summary>
        /// Converts the parsed node into a markup result for visual representation.
        /// </summary>
        /// <returns>A markup result containing the boundaries and structural information.</returns>
        public MarkupResult ToMarkupResult()
        {
            MarkupResult res = new MarkupResult
            {
                ModelType = ModelType,
                Start = StartPoint,
                End = EndPoint,
                IsUp = IsUp,
                Score = Score,
                Boundaries = new List<BarPoint>(),
                SubWaves = new List<MarkupResult>(),
                Level = 0,
                NodeName = ""
            };
            
            for (int i = 0; i < WaveCount; i++)
            {
                if (SubWaves[i] != null)
                {
                    if (i < WaveCount - 1)
                        res.Boundaries.Add(SubWaves[i].EndPoint);
                    
                    MarkupResult sub = SubWaves[i].ToMarkupResult();
                    sub.NodeName = ElliottWaveExactMarkup.GetWaveKey(ModelType, i + 1);
                    res.SubWaves.Add(sub);
                }
            }
            return res;
        }

        public override string ToString()
        {
            return ModelType.ToString();
        }
    }
}