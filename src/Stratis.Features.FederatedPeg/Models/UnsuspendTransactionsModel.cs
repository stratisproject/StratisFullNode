using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Features.FederatedPeg.Models
{
    public class TransactionToUnsuspend
    {
        [JsonProperty(PropertyName = "depositId")]
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 DepositId { get; set; }

        [JsonProperty(PropertyName = "blockHashContainingSpentUtxo")]
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 BlockHashContainingSpentUtxo { get; set; }

        /// <summary>
        /// Not required, this field is only used if the partial transaction in the CCTS is empty and the sending output needs to be recreated.
        /// </summary>
        [JsonProperty(PropertyName = "counterChainDestination")]
        public string CounterChainDestination { get; set; }

        /// <summary>
        /// Not required, this field is only used if the partial transaction in the CCTS is empty and the sending output needs to be recreated.
        /// </summary>
        [JsonProperty(PropertyName = "amountToSend")]
        public string AmountToSend { get; set; }
    }

    public class UnsuspendTransactionsModel
    {
        [JsonProperty(PropertyName = "toUnsuspend")]
        public List<TransactionToUnsuspend> ToUnsuspend { get; set; }
    }
}
