using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.BlockStore.Models
{
    /// <summary>
    /// A class containing the necessary parameters for a block search request.
    /// </summary>
    public class JoinFederationRequestModel
    {
        /// <summary>
        /// The collateral address.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        [JsonProperty(PropertyName = "collateralAddress")]
        public string CollateralAddress { get; set; }

        /// <summary>The name of the wallet which supplies the collateral address.</summary>
        [Required(ErrorMessage = "The name of the wallet is missing.")]
        public string CollateralWalletName { get; set; }

        /// <summary>The password of the wallet which supplies the collateral address.</summary>
        [Required(ErrorMessage = "A password is required.")]
        public string CollateralWalletPassword { get; set; }

        /// <summary>The name of the wallet which will supply the fee on the Cirrus network.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>The password of the wallet which will supply the fee on the Cirrus network.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }

        /// <summary>The account of the wallet which will supply the fee on the Cirrus network.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletAccount")]
        public string WalletAccount { get; set; }
    }
}
