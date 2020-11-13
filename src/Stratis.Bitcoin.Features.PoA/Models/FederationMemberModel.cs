using System;
using NBitcoin;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.PoA.Models
{
    public sealed class FederationMemberModel
    {
        [JsonProperty("pubkey")]
        public PubKey PubKey { get; set; }

        [JsonProperty("lastActiveTime")]
        public DateTime LastActiveTime { get; set; }

        [JsonProperty("lastActiveTimeSpan")]
        public TimeSpan LastActiveTimeSpan { get; set; }
    }
}
