using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    [Event("Transfer")]
    public class NftTransferEventDTO : IEventDTO
    {
        /// <summary>
        /// The sender of the token.
        /// </summary>
        [Parameter("address", "_from", 1, true)]
        public string From { get; set; }

        /// <summary>
        /// The recipient of the token.
        /// </summary>
        [Parameter("address", "_to", 2, true)]
        public string To { get; set; }

        /// <summary>
        /// The unique identifier of the token that was transferred from the sender to the recipient.
        /// Note that if the sender was the zero address the token was newly minted, and if the recipient
        /// was the zero address then the token is being burned.
        /// </summary>
        /// <remarks>Note that the indexed parameter is true for an ERC721 Transfer event. This is the only difference between it
        /// and the ERC20 equivalent. However, Nethereum requires the DTO to match exactly and we therefore need two classes.</remarks>
        [Parameter("uint256", "_tokenId", 3, true)]
        public BigInteger TokenId { get; set; }
    }
}
