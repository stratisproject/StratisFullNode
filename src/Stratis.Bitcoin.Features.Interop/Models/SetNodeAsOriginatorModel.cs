using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public sealed class SetNodeAsOriginatorModel
    {
        [JsonProperty(PropertyName = "requestId")]
        [Required]
        public string RequestId { get; set; }
    }
}
