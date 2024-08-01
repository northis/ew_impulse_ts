using System.Text;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Json;

namespace TradeKit.Core.PatternGeneration
{
    public class ModelPattern
    {
        public ModelPattern(ElliottModelType model,
            List<JsonCandleExport> candles,
            Dictionary<DateTime, List<PatternKeyPoint>> patternKeyPoints = null)
        {
            Model = model;
            PatternKeyPoints = patternKeyPoints ?? 
                               new Dictionary<DateTime, List<PatternKeyPoint>>();
            Candles = candles;
            LengthRatios = new List<LengthRatio>();
            DurationRatios = new List<DurationRatio>();
        }

        /// <summary>
        /// Gets or sets the length relations (for ex. wave C to wave A in pips).
        /// </summary>
        public List<LengthRatio> LengthRatios { get; }

        /// <summary>
        /// Gets or sets the duration relations (for ex. wave C to wave A in bars).
        /// </summary>
        public List<DurationRatio> DurationRatios { get; }

        /// <summary>
        /// Gets the model type of the pattern.
        /// </summary>
        public ElliottModelType Model { get; }

        /// <summary>
        /// Gets the map datetime (from <see cref="Candles"/>)-price of the key points of the pattern (plain dic).
        /// </summary>
        public Dictionary<DateTime, List<PatternKeyPoint>> PatternKeyPoints { get; }

        /// <summary>
        /// Gets or sets the candles of the pattern (plain list).
        /// </summary>
        public List<JsonCandleExport> Candles { get; internal set; }

        /// <summary>
        /// Gets the depth level.
        /// </summary>
        public byte Level { get; set; }

        /// <summary>
        /// Gets or sets the pattern arguments this model was generated from.
        /// </summary>
        public PatternArgsItem PatternArgs { get; set; }

        /// <summary>
        /// Converts to JSON.
        /// </summary>
        public JsonGeneratedModel ToJson()
        {
            var res = new JsonGeneratedModel
            {
                Model = Model,
                Candles = Candles.ToArray(),
                Waves = new List<JsonWave>[PatternKeyPoints.Keys.Count]
            };

            int i = 0;
            foreach (DateTime key in PatternKeyPoints.Keys)
            {
                res.Waves[i] = PatternKeyPoints[key]
                    .Select(a => new JsonWave
                    {
                        DateTime = key,
                        Value = a.Value,
                        WaveName = a.Notation.NotationKey,
                        Level = a.Notation.Level,
                        Type = a.Notation.Type
                    }).ToList();
                i++;
            }
            return res;
        }

        public override string ToString()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendFormat("Model: {0}", Model);
            stringBuilder.AppendLine();

            if (LengthRatios.Count > 0)
            {
                stringBuilder.AppendLine("Length ratios:");
                foreach (LengthRatio lRatio in LengthRatios)
                    stringBuilder.AppendLine(lRatio.ToString());
            }

            if (DurationRatios.Count > 0)
            {
                stringBuilder.AppendLine("Duration ratios:");
                foreach (DurationRatio dRatio in DurationRatios)
                    stringBuilder.AppendLine(dRatio.ToString());
            }

            return stringBuilder.ToString();
        }
    }
}
