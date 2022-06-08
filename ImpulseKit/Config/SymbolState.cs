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

        [JsonProperty(nameof(SetupStartIndex))]
        public int SetupStartIndex { get; set; }

        [JsonProperty(nameof(SetupEndIndex))]
        public int SetupEndIndex { get; set; }

        [JsonProperty(nameof(SetupStartPrice))]
        public double SetupStartPrice { get; set; }

        [JsonProperty(nameof(SetupEndPrice))]
        public double SetupEndPrice { get; set; }

        [JsonProperty(nameof(TriggerLevel))]
        public double TriggerLevel { get; set; }

        [JsonProperty(nameof(TriggerBarIndex))]
        public int TriggerBarIndex { get; set; }

        [JsonProperty(nameof(LastSignalMessageId))]
        public int LastSignalMessageId { get; set; }
    }
}
