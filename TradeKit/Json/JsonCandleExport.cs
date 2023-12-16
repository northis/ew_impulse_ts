using Newtonsoft.Json;
using System;
using TradeKit.Core;

namespace TradeKit.Json
{
    /// <summary>
    /// market candle in JSON
    /// </summary>
    public class JsonCandleExport : ICandle
    {
        /// <summary>
        /// Gets or sets the date.
        /// </summary>
        [JsonProperty("d")]
        public DateTime? OpenDate { get; set; }

        /// <summary>
        /// Gets or sets the bar index.
        /// </summary>
        [JsonProperty("i")]
        public int? BarIndex { get; set; }

        /// <summary>
        /// Gets or sets the open value.
        /// </summary>
        [JsonProperty("o")]
        public double O { get; set; }

        /// <summary>
        /// Gets or sets the close value.
        /// </summary>
        [JsonProperty("c")]
        public double C { get; set; }

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
