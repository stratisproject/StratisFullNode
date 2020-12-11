using System.ComponentModel.DataAnnotations;

namespace Stratis.Features.PoA.Voting
{
    /// <summary>
    /// A class containing the necessary parameters for a block search request.
    /// </summary>
    public sealed class JoinFederationResponseModel
    {
        /// <summary>
        /// The collateral address.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string MinerPublicKey { get; set; }
    }
}
