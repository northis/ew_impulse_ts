using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SignalsCheckKit.Json
{
    internal class TelegramTextItemConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(List<TelegramTextItemConverter>) || objectType == typeof(string);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            if (token.Type == JTokenType.Array)
            {
                TelegramTextItem val = null;
                try
                {
                    val = token.ToObject<List<TelegramTextItem>>().FirstOrDefault();
                }
                catch (Exception)
                {
                    ;
                }
                return val?.Text;
            }

            return token.Value<string>();
        }

        public override bool CanWrite
        {
            get
            {
                return false;
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
