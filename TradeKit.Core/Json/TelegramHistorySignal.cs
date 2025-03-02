using Newtonsoft.Json;

namespace TradeKit.Core.Json
{
    /// <summary>
    /// Entity for signal from telegram
    /// </summary>
    public class TelegramHistoryMessage
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
        /// Gets or sets the reply identifier.
        /// </summary>
        [JsonProperty("reply_to_message_id")]
        public long? ReplyId { get; set; }

        /// <summary>
        /// Gets or sets the text of the message.
        /// </summary>
        [JsonProperty("text")]
        [JsonConverter(typeof(TelegramTextItemConverter))]
        public string Text { get; set; }
    }
}
