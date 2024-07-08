using Newtonsoft.Json;

namespace TradeKit.Core.Json
{
    /// <summary>
    /// Trade setup entity - the data part
    /// </summary>
    public class JsonSymbolDataExport
    {
        /// <summary>
        /// Gets or sets the candles array.
        /// </summary>
        [JsonProperty("candles")]
        public JsonCandleExport[] Candles { get; set; }
    }
}
