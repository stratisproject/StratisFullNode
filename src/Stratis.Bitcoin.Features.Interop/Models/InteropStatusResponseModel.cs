using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public class InteropStatusResponseModel
    {
        [JsonProperty(PropertyName = "receivedVotes")]
        public Dictionary<string, List<string>> ReceivedVotes { get; set; }
    }
}
