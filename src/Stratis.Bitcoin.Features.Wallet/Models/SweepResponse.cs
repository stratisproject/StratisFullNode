using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Wallet.Models
{
    public class SweepResponse
    {
        /// <summary>
        /// The list of built sweep transactions.
        /// </summary>
        [JsonProperty(PropertyName = "transactions")]
        public List<string> Transactions { get; set; }

        /// <summary>
        /// The list of errors, if any, encountered during building the sweep transactions.
        /// </summary>
        [JsonProperty(PropertyName = "errors")]
        public List<string> Errors { get; set; }
    }
}
