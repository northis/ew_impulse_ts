using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Impulse;
using TradeKit.Json;

namespace TradeKit.PatternGeneration
{
    public class PlainModelPattern
    {
        private readonly ModelPattern m_ModelPattern;

        public PlainModelPattern(ModelPattern modelPattern)
        {
            m_ModelPattern = modelPattern;
            Model = modelPattern.Model;
            Candles = modelPattern.Candles;
            PatternKeyPoints = new Dictionary<DateTime, List<PatternKeyPoint>>();
        }

        /// <summary>
        /// Gets the model type of the pattern.
        /// </summary>
        public ElliottModelType Model { get; }

        /// <summary>
        /// Gets the map index (from <see cref="Candles"/>)-price of the key points of the pattern.
        /// </summary>
        public Dictionary<DateTime, List<PatternKeyPoint>> PatternKeyPoints { get; }

        /// <summary>
        /// Gets the candles of the pattern.
        /// </summary>
        public List<JsonCandleExport> Candles { get; }
    }
}
