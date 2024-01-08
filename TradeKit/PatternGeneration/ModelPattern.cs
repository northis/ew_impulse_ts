using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TradeKit.Impulse;
using TradeKit.Json;

namespace TradeKit.PatternGeneration
{
    public class ModelPattern
    {
        public ModelPattern(ElliottModelType model,
            List<JsonCandleExport> candles,
            List<(DateTime, double)> patternKeyPoints = null)
        {
            Model = model;
            PatternKeyPoints = patternKeyPoints ?? new List<(DateTime, double)>();
            Candles = candles;
            ChildModelPatterns = new List<ModelPattern>();
            LengthRatios = new List<LengthRatio>();
            DurationRatios = new List<DurationRatio>();
        }

        /// <summary>
        /// Gets or sets the possible child model patterns.
        /// </summary>
        public List<ModelPattern> ChildModelPatterns { get; }

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
        /// Gets the map index (from <see cref="Candles"/>)-price of the key points of the pattern.
        /// </summary>
        public List<(DateTime, double)> PatternKeyPoints { get; }

        /// <summary>
        /// Gets the candles of the pattern.
        /// </summary>
        public List<JsonCandleExport> Candles { get; }

        /// <summary>
        /// Gets the depth level.
        /// </summary>
        public byte Level { get; set; }

        /// <summary>
        /// Converts to JSON.
        /// </summary>
        public JsonGeneratedModel ToJson(PatternGenerator pg)
        {
            var res = new JsonGeneratedModel
            {
                Model = Model,
                Candles = Candles.ToArray(),
                Waves = new JsonWave[PatternKeyPoints.Count]
            };

            string[] waveNames = PatternGenerator
                .ModelRules[Model]
                .Models.Keys
                .ToArray();
            for (int i = 0; i < PatternKeyPoints.Count; i++)
            {
                string waveName = waveNames[i];
                var kp = PatternKeyPoints[i];
                res.Waves[i] = new JsonWave
                {
                    DateTime = kp.Item1, 
                    Value = kp.Item2, 
                    WaveName = waveName
                };
            }

            res.ChildModels = ChildModelPatterns.Select(a => a.ToJson(pg)).ToArray();
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

            if (ChildModelPatterns.Count > 0)
            {
                stringBuilder.AppendLine("Child models: {");
                foreach (ModelPattern childModel in ChildModelPatterns)
                {
                    stringBuilder.AppendLine(childModel.ToString());
                }

                stringBuilder.AppendLine("}");
            }

            return stringBuilder.ToString();
        }
    }
}
