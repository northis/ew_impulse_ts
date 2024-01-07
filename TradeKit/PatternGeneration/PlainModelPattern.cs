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
            Candles = modelPattern.Candles.Cast<JsonCandleExport>().ToList();
            PatternKeyPoints = new Dictionary<DateTime, List<PatternKeyPoint>>();
        }

        void ProcessModels(ModelPattern modelPattern, int offset = 0)
        {
            int modelsCount = modelPattern.ChildModelPatterns.Count;
            if (modelsCount == 0)
            {
                //PatternKeyPoints.Add(new (0,0d,null));
                //new NotationItem()
                //var cd = modelPattern.Candles.Cast<JsonCandleExport>().ToArray();
                //foreach ((int, double) patternKeyPoint in modelPattern.PatternKeyPoints)
                //{
                //    DateTime? dt = cd[patternKeyPoint.Item1].OpenDate;
                //    if (!dt.HasValue)
                //        throw new ApplicationException($"{nameof(PlainModelPattern)}: cannot do it without the dates");

                //    //if (PatternKeyPoints.ContainsKey(dt.Value))
                //    //    PatternKeyPoints[dt.Value];
                //}

                //PatternKeyPoints = modelPattern.PatternKeyPoints
                //    .GroupBy(a => cd[a.Item1].OpenDate.GetValueOrDefault())
                //    .ToDictionary(a => a.Key,
                //        b => b.Select(c =>
                //                new PatternKeyPoint(c.Item1, c.Item2,
                //                    new NotationItem(modelPattern.Model, 0, "", "", 1)))
                //            .ToArray());
            }

            for (int i = 0; i < modelsCount; i++)
            {
                ModelPattern model = modelPattern.ChildModelPatterns[i];
                int nextOffset = offset + model.Candles.Count;

            }
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
