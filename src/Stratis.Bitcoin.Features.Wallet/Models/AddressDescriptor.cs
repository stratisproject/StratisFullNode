using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Wallet.Models
{
    public class AddressDescriptor
    {
        [Required]
        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }

        [Required]
        [JsonProperty(PropertyName = "keyPath")]
        public string KeyPath { get; set; }

        /// <summary>
        /// Currently valid types are "p2pkh" and "p2wpkh", of which "p2pkh" is the default.
        /// </summary>
        [JsonProperty(PropertyName = "addressType")]
        public string AddressType { get; set; }
    }
}
