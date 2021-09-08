using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public sealed class PushManualVoteForRequest
    {
        [JsonProperty(PropertyName = "requestId")]
        public string RequestId { get; set; }

        [JsonProperty(PropertyName = "voteId")]
        public string EventId { get; set; }
    }
}
