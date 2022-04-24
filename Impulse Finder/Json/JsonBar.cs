using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace cAlgo.Json
{
    [DataContract]
    public class JsonBar
    {
        [JsonProperty("i")]
        public int Index { get; set; }

        [JsonProperty("dt")]
        public DateTime OpenTime { get; set; }

        [JsonProperty("h")]
        public double High { get; set; }

        [JsonProperty("l")]
        public double Low { get; set; }
    }
}
