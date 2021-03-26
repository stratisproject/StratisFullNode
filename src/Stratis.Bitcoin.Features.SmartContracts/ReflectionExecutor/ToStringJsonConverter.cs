using System;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor
{
    public class ToStringJsonConverter<T> : JsonConverter
    {
        private readonly Network network;

        public ToStringJsonConverter(Network network)
        {
            this.network = network;
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(T);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException("Unnecessary because CanRead is false. The type will skip the converter.");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            JToken t = JToken.FromObject(value);

            if (t.Type != JTokenType.Object)
            {
                t.WriteTo(writer);
            }
            else
            {
                JValue v = JValue.CreateString(((T)value).ToString());
                v.WriteTo(writer);
            }
        }
    }
}
