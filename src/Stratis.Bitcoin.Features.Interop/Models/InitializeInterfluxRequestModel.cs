using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public class InitializeInterfluxRequestModel
    {
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }

        [JsonProperty(PropertyName = "accountName")]
        public string AccountName { get; set; }
    }
}
