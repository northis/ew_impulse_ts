using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace cAlgo.Json
{
    [DataContract]
    public class JsonTimeFrame
    {
        [JsonProperty("tf")]
        public string TimeFrameName { get; set; }

        [JsonProperty("bars")]
        public JsonBar[] Bars { get; set; }
    }
}
