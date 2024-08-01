using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.Json
{
    /// <summary>
    /// Generated one Elliott Wave in JSON
    /// </summary>
    public class JsonWave
    {
        /// <summary>
        /// Gets or sets the candles array.
        /// </summary>
        [JsonProperty("n")]
        public string WaveName { get; set; }
        
        /// <summary>
        /// Gets or sets the datetime of the wave.
        /// </summary>
        [JsonProperty("d")]
        public DateTime DateTime { get; set; }

        /// <summary>
        /// Gets or sets the value (price) of the wave.
        /// </summary>
        [JsonProperty("v")]
        public double Value { get; set; }

        /// <summary>
        /// Gets or sets the wave type.
        /// </summary>
        [JsonProperty("type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ElliottModelType Type { get; set; }

        /// <summary>
        /// Gets or sets the level (depth) of the wave.
        /// </summary>
        [JsonProperty("l")]
        public byte Level { get; set; }
    }
}
