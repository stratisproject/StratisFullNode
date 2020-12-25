using NBitcoin;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.RPC
{
    public class FundRawTransactionOptions
    {
        /// <summary>
        /// The address to receive the change.
        /// </summary>
        [JsonProperty(PropertyName = "changeAddress")]
        public string ChangeAddress
        {
            get; set;
        }

        /// <summary>
        /// The index of the change output.
        /// </summary>
        [JsonProperty(PropertyName = "changePosition")]
        public int? ChangePosition
        {
            get; set;
        }

        /// <summary>
        /// The output type to use. Only valid if changeAddress is not specified. Options are "legacy", "p2sh-segwit", and "bech32"
        /// </summary>
        [JsonProperty(PropertyName = "changeType")]
        public string ChangeType
        {
            get; set;
        }

        /// <summary>
        /// Also select inputs which are watch only.
        /// </summary>
        [JsonProperty(PropertyName = "includeWatching")]
        public bool IncludeWatching
        {
            get; set;
        }

        /// <summary>
        /// Lock selected unspent outputs.
        /// </summary>
        [JsonProperty(PropertyName = "lockUnspents")]
        public bool LockUnspents
        {
            get; set;
        }

        /// <remarks>Deprecated</remarks>
        [JsonProperty(PropertyName = "reserveChangeKey")]
        public bool? ReserveChangeKey
        {
            get; set;
        }

        /// <summary>
        /// Set a specific fee rate in coins/kB.
        /// Default (not set): makes wallet determine the fee.
        /// </summary>
        [JsonProperty(PropertyName = "feeRate")]
        public FeeRate FeeRate
        {
            get; set;
        }

        /// <summary>
        /// A json array of integers.
        /// The fee will be equally deducted from the amount of each specified output.
        /// Those recipients will receive less coins than you enter in their corresponding amount field.
        /// If no outputs are specified here, the sender pays the fee.
        /// </summary>
        [JsonProperty(PropertyName = "subtractFeeFromOutputs")]
        public int[] SubtractFeeFromOutputs
        {
            get; set;
        }
    }
}
