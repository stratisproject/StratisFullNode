using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.PoA.Models
{
    public sealed class KickFederationMemberModel
    {
        [JsonProperty("pubkey")]
        public string PubKey { get; set; }
    }
}