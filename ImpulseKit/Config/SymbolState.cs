using Newtonsoft.Json;

namespace TradeKit.Config
{
    public class SymbolState
    {
        [JsonProperty(nameof(Symbol))]
        public string Symbol { get; set; }

        [JsonProperty(nameof(TimeFrame))]
        public string TimeFrame { get; set; }

        [JsonProperty(nameof(IsInSetup))]
        public bool IsInSetup { get; set; }

        [JsonProperty(nameof(LastSignalMessageId))]
        public int LastSignalMessageId { get; set; }
    }
}
