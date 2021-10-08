using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public class TransactionResponseModel
    {
        [JsonProperty(PropertyName = "destination")]
        public string Destination { get; set; }

        [JsonProperty(PropertyName = "value")]
        public string Value { get; set; }

        [JsonProperty(PropertyName = "data")]
        public string Data { get; set; }

        [JsonProperty(PropertyName = "executed")]
        public bool Executed { get; set; }
    }
}
