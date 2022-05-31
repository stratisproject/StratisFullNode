using System.ComponentModel.DataAnnotations;

namespace Stratis.Bitcoin.Features.BlockStore.Models
{
    /// <summary>
    /// A class containing the necessary parameters for a block search request.
    /// </summary>
    public class SearchByHeightRequest : RequestBase
    {
        /// <summary>
        /// The height of the required block(s).
        /// </summary>
        [Required]
        public int Height { get; set; }

        /// <summary>
        /// The maximum number of the blocks to return.
        /// </summary>
        [Required]
        public int NumberOfBlocks { get; set; }

        /// <summary>
        /// A flag that indicates whether to return each block transaction complete with details
        /// or simply return transaction hashes (TX IDs).
        /// </summary>
        /// <remarks>This flag is not used when <see cref="RequestBase.OutputJson"/> is set to false.</remarks>
        public bool ShowTransactionDetails { get; set; }
    }
}
