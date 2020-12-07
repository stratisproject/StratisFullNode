using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Wallet.Models
{
    public class BuildOfflineSignResponse
    {
        /// <summary>
        /// The wallet containing the UTXO(s) and address(es) needed to sign the transaction offline.
        /// </summary>
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>
        /// The account containing the UTXO(s) and address(es) needed to sign the transaction offline.
        /// </summary>
        [JsonProperty(PropertyName = "walletAccount")]
        public string WalletAccount { get; set; }

        /// <summary>
        /// The transaction that needs to be signed by the offline node.
        /// </summary>
        [JsonProperty(PropertyName = "unsignedTransaction")]
        public string UnsignedTransaction { get; set; }

        /// <summary>
        /// The transaction fee allocated for the transaction.
        /// This can be computed by the signer by evaluating the UTXOs and unsigned transaction's outputs, but is included for convenience.
        /// </summary>
        [JsonProperty(PropertyName = "fee")]
        public string Fee { get; set; }

        /// <summary>
        /// A list of UTXO metadata that will be needed by the offline node to sign the transaction and display the correct change etc.
        /// </summary>
        [JsonProperty(PropertyName = "utxos")]
        public List<UtxoDescriptor> Utxos { get; set; }

        /// <summary>
        /// A list of address metadata, in case the offline node is not expected to be recently synced & requires the exact keypaths.
        /// </summary>
        [JsonProperty(PropertyName = "addresses")]
        public List<AddressDescriptor> Addresses { get; set; }
    }
}
