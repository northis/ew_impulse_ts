using Newtonsoft.Json;

namespace SignalsCheckKit.Json
{
    public class TelegramExportJson
    {
        [JsonProperty("messages")]
        public TelegramHistorySignal[] Messages { get; set; }
    }
}
