using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Controllers.Models;

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

        [JsonProperty("transferStatus")]
        public string TransferStatus { get; set; }

        [JsonProperty("tx")]
        public TransactionVerboseModel Transaction { get; set; }
    }
}
