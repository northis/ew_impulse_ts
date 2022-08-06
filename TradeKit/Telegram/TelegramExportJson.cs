using Newtonsoft.Json;

namespace TradeKit.Telegram
{
    /// <summary>
    /// "Export from chat" in Telegram entity
    /// </summary>
    public class TelegramExportJson
    {
        /// <summary>
        /// Gets or sets the messages array.
        /// </summary>
        [JsonProperty("messages")]
        public TelegramHistorySignal[] Messages { get; set; }
    }
}
