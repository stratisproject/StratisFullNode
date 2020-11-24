using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.ColdStaking.Models
{
    public class OfflineSignRequest
    {
        /// <summary>
        /// The wallet containing the UTXO(s) and address(es) needed to sign the transaction offline.
        /// </summary>
        [Required]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>
        /// The account containing the UTXO(s) and address(es) needed to sign the transaction offline.
        /// </summary>
        [Required]
        [JsonProperty(PropertyName = "walletAccount")]
        public string WalletAccount { get; set; }

        /// <summary>
        /// The password to the wallet.
        /// </summary>
        [Required]
        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }

        /// <summary>
        /// The transaction that needs to be signed by the offline node.
        /// </summary>
        [Required]
        [JsonProperty(PropertyName = "unsignedTransaction")]
        public string UnsignedTransaction { get; set; }

        /// <summary>
        /// A list of UTXO metadata that will be needed by the offline node to sign the transaction and display the correct change etc.
        /// </summary>
        [Required]
        [JsonProperty(PropertyName = "utxos")]
        public List<UtxoDescriptor> Utxos { get; set; }

        /// <summary>
        /// An optional list of address metadata, in case the offline node is not expected to be recently synced & requires the exact keypaths.
        /// </summary>
        [JsonProperty(PropertyName = "addresses")]
        public List<AddressDescriptor> Addresses { get; set; }
    }
}
