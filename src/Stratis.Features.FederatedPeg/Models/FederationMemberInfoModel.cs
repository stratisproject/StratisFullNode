using Newtonsoft.Json;

namespace Stratis.Features.FederatedPeg.Models
{
    public sealed class FederationMemberInfoModel
    {
        [JsonProperty(PropertyName = "asyncLoopState")]
        public string AsyncLoopState { get; set; }

        [JsonProperty(PropertyName = "consensusHeight")]
        public int ConsensusHeight { get; set; }

        [JsonProperty(PropertyName = "cctsHeight")]
        public int CrossChainStoreHeight { get; set; }

        [JsonProperty(PropertyName = "cctsNextDepositHeight")]
        public int CrossChainStoreNextDepositHeight { get; set; }

        [JsonProperty(PropertyName = "cctsPartials")]
        public int CrossChainStorePartialTxs { get; set; }

        [JsonProperty(PropertyName = "cctsSuspended")]
        public int CrossChainStoreSuspendedTxs { get; set; }

        [JsonProperty(PropertyName = "federationWalletActive")]
        public bool FederationWalletActive { get; set; }

        [JsonProperty(PropertyName = "federationWalletBalance")]
        public decimal FederationWalletBalance { get; set; }

        [JsonProperty(PropertyName = "federationWalletHeight")]
        public int FederationWalletHeight { get; set; }

        [JsonProperty(PropertyName = "nodeVersion")]
        public string NodeVersion { get; set; }

        [JsonProperty(PropertyName = "pubKey")]
        public string PubKey { get; set; }
    }
}