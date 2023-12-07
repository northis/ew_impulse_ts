using Newtonsoft.Json;
using System;

namespace TradeKit.Core
{
    /// <summary>
    /// market candle in JSON
    /// </summary>
    public class JsonCandleExport
    {
        /// <summary>
        /// Gets or sets the date.
        /// </summary>
        [JsonProperty("d")]
        public DateTime OpenDate { get; set; }

        /// <summary>
        /// Gets or sets the bar index.
        /// </summary>
        [JsonProperty("i")]
        public int BarIndex { get; set; }

        /// <summary>
        /// Gets or sets the open value.
        /// </summary>
        [JsonProperty("o")]
        public double Open { get; set; }

        /// <summary>
        /// Gets or sets the close value.
        /// </summary>
        [JsonProperty("c")]
        public double Close { get; set; }
        
        /// <summary>
        /// Gets or sets the high value.
        /// </summary>
        [JsonProperty("h")]
        public double H { get; set; }

        /// <summary>
        /// Gets or sets the low value.
        /// </summary>
        [JsonProperty("l")]
        public double L { get; set; }
    }
}
