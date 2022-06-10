using Newtonsoft.Json;
using Stratis.Features.FederatedPeg.Conversion;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public sealed class SetConversionRequestStateModel
    {
        [JsonProperty(PropertyName = "id")]
        public string RequestId { get; set; }

        [JsonProperty(PropertyName = "status")]
        public ConversionRequestStatus Status { get; set; }

        [JsonProperty(PropertyName = "processed")]
        public bool Processed { get; set; }
    }
}
