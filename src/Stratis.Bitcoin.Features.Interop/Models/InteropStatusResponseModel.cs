using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public class InteropStatusResponseModel
    {
        [JsonProperty(PropertyName = "requests")]
        public List<ConversionRequestModel> Requests { get; set; }

        [JsonProperty(PropertyName = "receivedVotes")]
        public Dictionary<string, List<string>> ReceivedVotes { get; set; }
    }
}
