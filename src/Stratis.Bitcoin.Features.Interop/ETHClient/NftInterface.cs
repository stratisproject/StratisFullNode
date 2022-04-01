using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Contracts.ContractHandlers;
using Nethereum.Web3;

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

    [Function("tokenURI", "string")]
    public class TokenUriFunction : FunctionMessage
    {
        [Parameter("uint256", "tokenId", 1)]
        public BigInteger TokenId { get; set; }
    }

    public class NftInterface
    {
        /// <summary>
        /// If the NFT contract supports the ERC721Metadata extension, it should expose a 'tokenURI(uint256 tokenId)' method that
        /// can be interrogated to retrieve the token-specific URI.
        /// </summary>
        /// <returns>The URI for the given tokenId.</returns>
        public static async Task<string> GetTokenUriAsync(Web3 web3, string contractAddress, BigInteger tokenId)
        {
            var tokenUriFunctionMessage = new TokenUriFunction()
            {
                TokenId = tokenId
            };

            IContractQueryHandler<TokenUriFunction> balanceHandler = web3.Eth.GetContractQueryHandler<TokenUriFunction>();
            string uri = await balanceHandler.QueryAsync<string>(contractAddress, tokenUriFunctionMessage).ConfigureAwait(false);

            return uri;
        }
    }
}
