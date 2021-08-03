using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public sealed class ReprocessBurnRequestModel
    {
        [JsonProperty(PropertyName = "id")]
        public string RequestId { get; set; }

        [JsonProperty(PropertyName = "height")]
        public int BlockHeight { get; set; }
    }
}
