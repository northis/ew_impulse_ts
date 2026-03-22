using System.Collections.Generic;
using System.Linq;
using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Represents the visual markup result of a parsed Elliott Wave structure.
    /// </summary>
    public class MarkupResult
    {
        /// <summary>
        /// Gets or sets the type of the Elliott wave model.
        /// </summary>
        public ElliottModelType ModelType { get; set; }
        /// <summary>
        /// Gets or sets the list of internal boundary points (extremums) of the wave.
        /// </summary>
        public List<BarPoint> Boundaries { get; set; } = new List<BarPoint>();
        /// <summary>
        /// Gets or sets the child sub-waves of this markup.
        /// </summary>
        public List<MarkupResult> SubWaves { get; set; } = new List<MarkupResult>();
        /// <summary>
        /// Gets or sets the score of the wave structure based on Elliott Wave rules.
        /// </summary>
        public double Score { get; set; }
        /// <summary>
        /// Gets or sets the starting point of the wave structure.
        /// </summary>
        public BarPoint Start { get; set; }
        /// <summary>
        /// Gets or sets the ending point of the wave structure.
        /// </summary>
        public BarPoint End { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the wave is a bullish (upward) wave.
        /// </summary>
        public bool IsUp { get; set; }
        /// <summary>
        /// Gets or sets the specific string name or degree of the wave node (e.g., '1', 'A', 'III').
        /// </summary>
        public string NodeName { get; set; }
        /// <summary>
        /// Gets or sets the depth level (degree) of the wave in the nested structure.
        /// </summary>
        public byte Level { get; set; }

        /// <summary>
        /// Flattens the hierarchical structure into a linear sequence of markup results.
        /// </summary>
        /// <returns>An enumerable sequence of all nodes including this node and its recursive sub-waves.</returns>
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
}