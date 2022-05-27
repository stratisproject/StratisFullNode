using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Features.Wallet.Models
{
    public class WalletBuildTransactionModel
    {
        /// <summary>
        /// Transaction fee amount
        /// </summary>
        [JsonProperty(PropertyName = "fee")]
        public Money Fee { get; set; }

        /// <summary>
        /// Hex-encoded transaction representation
        /// </summary>
        [JsonProperty(PropertyName = "hex")]
        public string Hex { get; set; }

        /// <summary>
        /// Transaction hash
        /// </summary>
        [JsonProperty(PropertyName = "transactionId")]
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 TransactionId { get; set; }
    }
}
