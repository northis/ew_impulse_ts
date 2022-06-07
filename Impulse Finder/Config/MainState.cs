using System.Collections.Generic;
using Newtonsoft.Json;

namespace cAlgo.Config
{
    public class MainState
    {
        [JsonProperty(nameof(States))]
        public Dictionary<string, SymbolState> States { get; set; }
    }
}
