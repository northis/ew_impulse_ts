using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.Json
{
    /// <summary>
    /// Generated Elliott Wave model in JSON
    /// </summary>
    public class JsonGeneratedModel
    {
        /// <summary>
        /// Gets or sets the candles array.
        /// </summary>
        [JsonProperty("candles")]
        public JsonCandleExport[] Candles { get; set; }

        /// <summary>
        /// Gets or sets the child models.
        /// </summary>
        [JsonProperty("models")]
        public JsonGeneratedModel[] ChildModels { get; set; }
        
        /// <summary>
        /// Gets or sets the model type.
        /// </summary>
        [JsonProperty("type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ElliottModelType Model { get; set; }

        /// <summary>
        /// Gets the waves entities.
        /// </summary>
        public List<JsonWave>[] Waves { get; set; }
    }
}
