using Newtonsoft.Json;

namespace TradeKit.Json
{
    /// <summary>
    /// Trade setup entity - statistic part
    /// </summary>
    public class JsonSymbolStatExport
    {
        /// <summary>
        /// Gets or sets the symbol name.
        /// </summary>
        [JsonProperty("s")]
        public string Symbol { get; set; }

        /// <summary>
        /// Gets or sets the Accuracy (digits after the dot).
        /// </summary>
        [JsonProperty("a")]
        public int Accuracy { get; set; }

        /// <summary>
        /// Gets or sets the trade result (true if TP hit, false if SL hit).
        /// </summary>
        [JsonProperty("r")]
        public bool Result { get; set; }

        /// <summary>
        /// Gets or sets the symbol.
        /// </summary>
        [JsonProperty("tf")]
        public string TimeFrame { get; set; }

        /// <summary>
        /// Gets or sets the entry.
        /// </summary>
        [JsonProperty("e")]
        public double Entry { get; set; }

        /// <summary>
        /// Gets or sets the entry bar index.
        /// </summary>
        [JsonProperty("ei")]
        public int EntryIndex { get; set; }

        /// <summary>
        /// Gets or sets the stop.
        /// </summary>
        [JsonProperty("sl")]
        public double Stop { get; set; }

        /// <summary>
        /// Gets or sets the start bar index.
        /// </summary>
        [JsonProperty("si")]
        public int StartIndex { get; set; }

        /// <summary>
        /// Gets or sets the take.
        /// </summary>
        [JsonProperty("tp")]
        public double Take { get; set; }

        /// <summary>
        /// Gets or sets the finish bar index.
        /// </summary>
        [JsonProperty("fi")]
        public int FinishIndex { get; set; }
    }
}
