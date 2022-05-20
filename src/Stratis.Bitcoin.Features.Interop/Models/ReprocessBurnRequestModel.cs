using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public sealed class ReprocessBurnRequestModel : RequestIdModel
    {
        [JsonProperty(PropertyName = "height")]
        public int BlockHeight { get; set; }
    }
}
