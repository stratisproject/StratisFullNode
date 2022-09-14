using System.Numerics;
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
        public BigInteger Amount { get; set; }

        [JsonProperty(PropertyName = "processed")]
        public bool Processed { get; set; }

        [JsonProperty(PropertyName = "status")]
        public string Status { get; set; }

        [JsonProperty(PropertyName = "tokenContract")]
        public string TokenContract { get; set; }

        [JsonProperty(PropertyName = "externalChainBlockHeight")]
        public int ExternalChainBlockHeight { get; set; }

        [JsonProperty(PropertyName = "externalChainTxEventId")]
        public string ExternalChainTxEventId { get; set; }

        [JsonProperty(PropertyName = "externalChainTxHash")]
        public string ExternalChainTxHash { get; internal set; }

        [JsonProperty(PropertyName = "message")]
        public string Message { get; set; }
    }
}
