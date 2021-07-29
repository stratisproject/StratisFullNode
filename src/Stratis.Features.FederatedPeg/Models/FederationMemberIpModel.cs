using Newtonsoft.Json;

namespace Stratis.Features.FederatedPeg.Models
{
    public class FederationMemberIpModel
    {
        [JsonProperty(PropertyName = "endpoint")]
        public string EndPoint { get; set; }
    }

    public sealed class ReplaceFederationMemberIpModel : FederationMemberIpModel
    {
        [JsonProperty(PropertyName = "endpointtouse")]
        public string EndPointToUse { get; set; }
    }
}