using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace cAlgo.Json
{
    [DataContract]
    public class JsonHistory
    {
        [JsonProperty("s")]
        public string Symbol { get; set; }

        [JsonProperty("tfs")]
        public JsonTimeFrame[] JsonTimeFrames { get; set; }
    }
}
