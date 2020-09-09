using System.ComponentModel.DataAnnotations;

namespace Stratis.Bitcoin.Features.BlockStore.Models
{
    /// <summary>
    /// A class containing the necessary parameters for a block search request.
    /// </summary>
    public class JoinFederationResponseModel
    {
        /// <summary>
        /// The collateral address.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string CollateralAddress { get; set; }
    }
}
