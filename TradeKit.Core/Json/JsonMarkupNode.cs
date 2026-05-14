using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.Json
{
    /// <summary>
    /// Serializable representation of <see cref="ExactParsedNode"/> for JSON export.
    /// </summary>
    public class JsonMarkupNode
    {
        [JsonProperty("model")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ElliottModelType ModelType { get; set; }

        [JsonProperty("score")]
        public double Score { get; set; }

        [JsonProperty("isUp")]
        public bool IsUp { get; set; }

        [JsonProperty("waveName")]
        public string WaveName { get; set; }

        [JsonProperty("start")]
        public JsonMarkupPoint Start { get; set; }

        [JsonProperty("end")]
        public JsonMarkupPoint End { get; set; }

        [JsonProperty("subWaves", NullValueHandling = NullValueHandling.Ignore)]
        public JsonMarkupNode[] SubWaves { get; set; }

        /// <summary>
        /// Converts an <see cref="ExactParsedNode"/> tree into a serializable
        /// <see cref="JsonMarkupNode"/> tree.
        /// </summary>
        /// <param name="node">Source node.</param>
        /// <param name="waveName">Wave label for this node within its parent
        /// (e.g. "1", "a", "w"). Pass <c>null</c> for the root.</param>
        public static JsonMarkupNode FromParsedNode(ExactParsedNode node, string waveName = null)
        {
            if (node == null) return null;

            JsonMarkupNode[] children = null;
            if (node.SubWaves != null && node.WaveCount > 0)
            {
                var list = new List<JsonMarkupNode>();
                for (int i = 0; i < node.WaveCount; i++)
                {
                    if (i >= node.SubWaves.Length || node.SubWaves[i] == null)
                        continue;

                    string childName = ElliottWaveExactMarkup.GetWaveKey(node.ModelType, i + 1);
                    list.Add(FromParsedNode(node.SubWaves[i], childName));
                }
                if (list.Count > 0)
                    children = list.ToArray();
            }

            return new JsonMarkupNode
            {
                ModelType = node.ModelType,
                Score = Math.Round(node.Score, 6),
                IsUp = node.IsUp,
                WaveName = waveName,
                Start = JsonMarkupPoint.FromBarPoint(node.StartPoint),
                End = JsonMarkupPoint.FromBarPoint(node.EndPoint),
                SubWaves = children
            };
        }

        /// <summary>
        /// Converts this JSON node back into an <see cref="ExactParsedNode"/>,
        /// resolving bar indices from timestamps via the given bars provider.
        /// </summary>
        /// <param name="provider">Bars provider used to resolve time → bar index.</param>
        /// <returns>Reconstructed node, or <c>null</c> if conversion fails.</returns>
        public ExactParsedNode ToParsedNode(IBarsProvider provider)
        {
            if (Start == null || End == null)
                return null;

            int startIdx = provider.GetIndexByTime(Start.Time);
            int endIdx = provider.GetIndexByTime(End.Time);

            var startPoint = new BarPoint(Start.Value, Start.Time, provider.TimeFrame, startIdx);
            var endPoint = new BarPoint(End.Value, End.Time, provider.TimeFrame, endIdx);

            ExactParsedNode[] subWaves = null;
            int waveCount = 0;
            if (SubWaves != null && SubWaves.Length > 0)
            {
                subWaves = new ExactParsedNode[SubWaves.Length];
                for (int i = 0; i < SubWaves.Length; i++)
                {
                    subWaves[i] = SubWaves[i]?.ToParsedNode(provider);
                }
                waveCount = subWaves.Length;
            }

            return new ExactParsedNode
            {
                ModelType = ModelType,
                Score = Score,
                IsUp = IsUp,
                StartPoint = startPoint,
                EndPoint = endPoint,
                SubWaves = subWaves,
                WaveCount = waveCount,
                ExpectedWaves = ElliottWaveExactMarkup.GetExpectedWaves(ModelType)
            };
        }
    }

    /// <summary>
    /// Serializable bar extremum point.
    /// </summary>
    public class JsonMarkupPoint
    {
        [JsonProperty("time")]
        public DateTime Time { get; set; }

        [JsonProperty("value")]
        public double Value { get; set; }

        [JsonProperty("barIndex")]
        public int BarIndex { get; set; }

        public static JsonMarkupPoint FromBarPoint(Common.BarPoint bp)
        {
            if (bp == null) return null;
            return new JsonMarkupPoint
            {
                Time = bp.OpenTime,
                Value = bp.Value,
                BarIndex = bp.BarIndex
            };
        }
    }
}
