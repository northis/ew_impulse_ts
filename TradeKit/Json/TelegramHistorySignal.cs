using System;
using Newtonsoft.Json;

namespace TradeKit.Json
{
    /// <summary>
    /// Entity for signal from telegram
    /// </summary>
    public class TelegramHistorySignal
    {
        /// <summary>
        /// Gets or sets the date.
        /// </summary>
        [JsonProperty("date")]
        public DateTime Date { get; set; }

        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        [JsonProperty("id")]
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the text of the message.
        /// </summary>
        [JsonProperty("text")]
        [JsonConverter(typeof(TelegramTextItemConverter))]
        public string Text { get; set; }
    }
}
