using System.Collections.Generic;
using Newtonsoft.Json;

namespace TradeKit.Config
{
    public class MainState
    {
        [JsonProperty(nameof(States))]
        public Dictionary<string, SymbolState> States { get; set; }
    }
}
