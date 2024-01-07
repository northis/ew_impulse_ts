using System;
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
        /// Gets or sets the datetime of the wave.
        /// </summary>
        [JsonProperty("d")]
        public DateTime DateTime { get; set; }

        /// <summary>
        /// Gets or sets the value (price) of the wave.
        /// </summary>
        [JsonProperty("v")]
        public double Value { get; set; }
    }
}
