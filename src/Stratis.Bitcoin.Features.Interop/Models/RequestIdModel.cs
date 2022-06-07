using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public class RequestIdModel
    {
        [JsonProperty(PropertyName = "id")]
        public string RequestId { get; set; }
    }
}
