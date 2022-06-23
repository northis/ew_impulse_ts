using Newtonsoft.Json;

namespace SignalsCheckKit.Json
{
    public class TelegramHistorySignal
    {
        [JsonProperty("date")] public DateTime Date { get; set; }

        [JsonProperty("id")] public long Id { get; set; }

        [JsonProperty("text")]
        [JsonConverter(typeof(TelegramTextItemConverter))]
        public string Text { get; set; }
    }
}
