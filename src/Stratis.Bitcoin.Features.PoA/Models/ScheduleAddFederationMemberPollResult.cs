using NBitcoin;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.PoA.Models
{
    public sealed class ScheduleAddFederationMemberPollResult
    {
        [JsonProperty("pubkey")]
        public PubKey PubKey { get; set; }
    }
}
