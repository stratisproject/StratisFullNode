using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Stratis.Features.PoA.Voting
{
    /// <summary>
    /// A class containing the necessary parameters for a join federation request.
    /// </summary>
    public sealed class JoinFederationRequestModel
    {
        /// <summary>
        /// The collateral address.
        /// </summary>
        [Required(ErrorMessage = "The collateral address is required.")]
        [JsonProperty(PropertyName = "collateralAddress")]
        public string CollateralAddress { get; set; }

        /// <summary>The name of the wallet which supplies the collateral address.</summary>
        [Required(ErrorMessage = "The collateral wallet name is required.")]
        [JsonProperty(PropertyName = "collateralWalletName")]
        public string CollateralWalletName { get; set; }

        /// <summary>The password of the wallet which supplies the collateral address.</summary>
        [Required(ErrorMessage = "The collateral wallet password is required.")]
        [JsonProperty(PropertyName = "collateralWalletPassword")]
        public string CollateralWalletPassword { get; set; }

        /// <summary>The name of the wallet which will supply the fee on the Cirrus network.</summary>
        [Required(ErrorMessage = "The fee wallet name is required")]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>The password of the wallet which will supply the fee on the Cirrus network.</summary>
        [Required(ErrorMessage = "The fee wallet password is required")]
        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }

        /// <summary>The account of the wallet which will supply the fee on the Cirrus network.</summary>
        [Required(ErrorMessage = "The fee wallet account is required")]
        [JsonProperty(PropertyName = "walletAccount")]
        public string WalletAccount { get; set; }
    }
}
