using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.RPC.Models
{
    public class TransactionsSinceBlockModel
    {
        /// <summary>
        /// All transactions.</summary>
        [JsonProperty(Order = 1, PropertyName = "transactions")]
        public TransactionInfoModel[] Transactions { get; set; }

        /// <summary>
        /// The hash of the block since which we got transactions.</summary>
        [JsonProperty(PropertyName = "lastblock")]
        public string LastBlock { get; set; }
    }
}
