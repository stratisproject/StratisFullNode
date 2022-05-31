using Newtonsoft.Json;
using Stratis.Bitcoin.Features.Wallet;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public sealed class ResetScanHeightModel
    {
        [JsonProperty(PropertyName = "destinationChain")]
        public DestinationChain DestinationChain { get; set; }

        [JsonProperty(PropertyName = "height")]
        public int Height { get; set; }
    }
}
