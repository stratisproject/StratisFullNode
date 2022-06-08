using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public sealed class PushManualVoteForRequest : RequestIdModel
    {
        [JsonProperty(PropertyName = "voteId")]
        public string EventId { get; set; }
    }
}
