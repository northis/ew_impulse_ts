using System.Collections.Generic;
using TradeKit.Core;
using TradeKit.Impulse;

namespace TradeKit.PatternGeneration
{
    public class ModelPattern
    {
        public ModelPattern(ElliottModelType model,
            List<ICandle> candles,
            List<KeyValuePair<int, double>> patternKeyPoints = null)
        {
            Model = model;
            PatternKeyPoints = patternKeyPoints;
            Candles = candles;
            ChildModelPatterns = new List<ModelPattern>();
        }

        /// <summary>
        /// Gets or sets the possible child model patterns.
        /// </summary>
        public List<ModelPattern> ChildModelPatterns { get; set; }

        /// <summary>
        /// Gets the model type of the pattern.
        /// </summary>
        public ElliottModelType Model { get; }

        /// <summary>
        /// Gets the map index (from <see cref="Candles"/>)-price of the key points of the pattern.
        /// </summary>
        public List<KeyValuePair<int, double>> PatternKeyPoints { get; set; }

        /// <summary>
        /// Gets the candles of the pattern.
        /// </summary>
        public List<ICandle> Candles { get; }
    }
}
