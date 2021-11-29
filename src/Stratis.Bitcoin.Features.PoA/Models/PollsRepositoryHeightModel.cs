using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.PoA.Models
{
    public sealed class PollsRepositoryHeightModel
    {
        [JsonProperty(PropertyName = "tipHeight")]
        public int TipHeight { get; set; }

        [JsonProperty(PropertyName = "tipHeightPercentage")]
        public int TipHeightPercentage { get; set; }
    }
}
