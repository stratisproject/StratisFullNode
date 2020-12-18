using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Wallet.Models
{
    /// <summary>
    /// The signing request to be made against the offline node.
    /// <remarks>This is intended to exactly match the structure of a <see cref="BuildOfflineSignResponse"/>, but the wallet password is needed as an additional field.</remarks>
    /// </summary>
    public class OfflineSignRequest : BuildOfflineSignResponse
    {
        public OfflineSignRequest()
        {
            this.Utxos = new List<UtxoDescriptor>();
            this.Addresses = new List<AddressDescriptor>();
        }

        /// <summary>
        /// The password to the wallet.
        /// </summary>
        [Required]
        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }
    }
}
