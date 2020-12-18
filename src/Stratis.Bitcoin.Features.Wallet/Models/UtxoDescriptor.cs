using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities.ValidationAttributes;

namespace Stratis.Bitcoin.Features.Wallet.Models
{
    public class UtxoDescriptor
    {
        [Required]
        [JsonProperty(PropertyName = "transactionId")]
        public string TransactionId { get; set; }

        [Required]
        [JsonProperty(PropertyName = "index")]
        public string Index { get; set; }

        [Required]
        [JsonProperty(PropertyName = "scriptPubKey")]
        public string ScriptPubKey { get; set; }

        [JsonProperty(PropertyName = "amount")]
        [MoneyFormat(isRequired: true, ErrorMessage = "The op return amount is not in the correct format.")]
        public string Amount { get; set; }
    }
}
