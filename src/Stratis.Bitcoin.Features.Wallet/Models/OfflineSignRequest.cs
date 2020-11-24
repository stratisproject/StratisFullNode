using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities.ValidationAttributes;

namespace Stratis.Bitcoin.Features.Wallet.Models
{
    public class OfflineSignRequest
    {
        public OfflineSignRequest()
        {
            this.Utxos = new List<UtxoDescriptor>();
            this.Addresses = new List<AddressDescriptor>();
        }

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
        /// The transaction fee allocated for the transaction.
        /// This can be computed by the signer by evaluating the UTXOs and unsigned transaction's outputs, but is included for convenience.
        /// </summary>
        [MoneyFormat(isRequired: false, ErrorMessage = "The fee is not in the correct format.")]
        [JsonProperty(PropertyName = "fee")]
        public string Fee { get; set; }

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
