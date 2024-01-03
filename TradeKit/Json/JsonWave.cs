using Newtonsoft.Json;
namespace TradeKit.Json
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
        /// Gets or sets the index or the wave.
        /// </summary>
        [JsonProperty("i")]
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the value (price) or the wave.
        /// </summary>
        [JsonProperty("v")]
        public double Value { get; set; }
    }
}
