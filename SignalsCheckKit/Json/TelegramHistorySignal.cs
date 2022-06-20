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

    public class TelegramTextItem
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }

}
