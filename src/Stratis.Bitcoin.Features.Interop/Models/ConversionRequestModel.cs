using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public class ConversionRequestModel
    {
        [JsonProperty(PropertyName = "requestId")]
        public string RequestId { get; set; }

        [JsonProperty(PropertyName = "requestType")]
        public int RequestType { get; set; }

        [JsonProperty(PropertyName = "requestStatus")]
        public int RequestStatus { get; set; }

        [JsonProperty(PropertyName = "blockHeight")]
        public int BlockHeight { get; set; }

        [JsonProperty(PropertyName = "destinationAddress")]
        public string DestinationAddress { get; set; }

        [JsonProperty(PropertyName = "amount")]
        public ulong Amount { get; set; }

        [JsonProperty(PropertyName = "processed")]
        public bool Processed { get; set; }
    }
}
