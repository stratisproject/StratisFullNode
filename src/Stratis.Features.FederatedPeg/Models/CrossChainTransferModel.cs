using NBitcoin;
using Newtonsoft.Json;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Features.FederatedPeg.Models
{
    public sealed class CrossChainTransferModel
    {
        [JsonProperty("depositAmount")]
        public Money DepositAmount { get; internal set; }

        [JsonProperty("depositId")]
        public uint256 DepositId { get; internal set; }

        [JsonProperty("depositHeight")]
        public int? DepositHeight { get; internal set; }

        [JsonProperty("status")]
        public CrossChainTransferStatus TransferStatus { get; set; }

        [JsonProperty("tx")]
        public string Transaction { get; set; }
    }
}
