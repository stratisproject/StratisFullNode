using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public class InteropStatusResponseModel
    {
        [JsonProperty(PropertyName = "receivedVotes")]
        public Dictionary<string, List<string>> ReceivedVotes { get; set; }

        [JsonProperty(PropertyName = "transactionIdVotes")]
        public Dictionary<string, Dictionary<BigInteger, int>> TransactionIdVotes { get; set; }
    }
}
