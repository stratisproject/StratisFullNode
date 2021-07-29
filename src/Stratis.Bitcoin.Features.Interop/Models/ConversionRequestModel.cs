using Newtonsoft.Json;
using Stratis.Features.FederatedPeg.Conversion;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public class ConversionRequestModel
    {
        [JsonProperty(PropertyName = "requestId")]
        public string RequestId { get; set; }

        [JsonProperty(PropertyName = "requestType")]
        public ConversionRequestType RequestType { get; set; }

        [JsonProperty(PropertyName = "requestStatus")]
        public ConversionRequestStatus RequestStatus { get; set; }

        [JsonProperty(PropertyName = "blockHeight")]
        public int BlockHeight { get; set; }

        [JsonProperty(PropertyName = "destinationAddress")]
        public string DestinationAddress { get; set; }

        [JsonProperty(PropertyName = "destinationChain")]
        public string DestinationChain { get; set; }

        [JsonProperty(PropertyName = "amount")]
        public ulong Amount { get; set; }

        [JsonProperty(PropertyName = "processed")]
        public bool Processed { get; set; }
    }
}
