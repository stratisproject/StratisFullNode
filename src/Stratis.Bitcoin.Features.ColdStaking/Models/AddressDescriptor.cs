using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.ColdStaking.Models
{
    public class AddressDescriptor
    {
        [Required]
        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }

        [Required]
        [JsonProperty(PropertyName = "keyPath")]
        public string KeyPath { get; set; }

        [JsonProperty(PropertyName = "addressType")]
        public string AddressType { get; set; }
    }
}
