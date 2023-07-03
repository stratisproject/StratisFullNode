using System.Numerics;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    public enum ContractType
    {
        /// <summary>A subset of Ethereum tokens that follow the ERC20 rules and protocols.</summary>
        /// <remarks>
        /// ERC20 tokens have a set of six functions that dictate how they can be transferred, how to access information about them, and how to manage the total supply:
        /// <list type="bullet">
        /// <item>totalSupply: Provides the total supply of the token.</item>
        /// <item>balanceOf: Returns the token balance of a specific address.</item>
        /// <item>transfer: Transfers a specified amount of tokens from the sender's address to another address.</item>
        /// <item>transferFrom: Allows a third-party to transfer tokens on behalf of the token owner, provided the token owner has granted the necessary allowance.</item>
        /// <item>approve: Allows the token owner to grant a third-party the ability to transfer tokens on their behalf, up to a specified amount.</item>
        /// <item>allowance: Provides the remaining token allowance that a third-party is allowed to transfer on behalf of the token owner.</item>
        /// </list></remarks>
        ERC20 = 0,

        /// <summary>ERC721 is another standard for tokens on the Ethereum blockchain, specifically designed for creating non-fungible tokens (NFTs).</summary>
        /// <remarks>
        /// The ERC721 standard provides a set of functions that enable developers to manage and track the ownership of these unique tokens:
        /// <list type="bullet">
        /// <item>balanceOf: Returns the number of NFTs owned by a specific address.</item>
        /// <item>ownerOf: Returns the owner of a specific NFT, identified by its unique token ID.</item>
        /// <item>safeTransferFrom: Transfers the ownership of a specific NFT from one address to another, ensuring the receiving address is capable of handling NFTs.</item>
        /// <item>transferFrom: Transfers the ownership of a specific NFT from one address to another, without the safety checks of safeTransferFrom.</item>
        /// <item>approve: Allows the NFT owner to grant a third-party the ability to transfer a specific NFT on their behalf.</item>
        /// <item>getApproved: Returns the approved address for a specific NFT, or a zero address if no approval has been granted.</item>
        /// <item>setApprovalForAll: Allows an NFT owner to grant or revoke approval for a specific operator to manage all their NFTs.</item>
        /// <item>isApprovedForAll: Returns whether an operator is approved to manage all NFTs owned by a specific address.</item>
        /// </list></remarks>
        ERC721 = 1
    }

    /// <summary>
    /// The type of transfer.
    /// </summary>
    /// <remarks>We don't currently need to make a distinction between transfers and mints.</remarks>
    public enum TransferType
    {
        /// <summary>
        /// A normal transfer.
        /// </summary>
        Transfer = 0,

        /// <summary>
        /// A transfer that burns the token.
        /// </summary>
        Burn = 1
    }

    public class TransferDetails
    {
        public ContractType ContractType { get; set; }

        public TransferType TransferType { get; set; }

        public string From { get; set; }

        public string To { get; set; }

        /// <summary>
        /// For an ERC20 transfer, the amount that was transferred.
        /// For an ERC721 transfer, this is the token identifier.
        /// </summary>
        public BigInteger Value { get; set; }

        /// <summary>
        /// For an ERC721 transfer, this is the URI of the token metadata.
        /// This field is not used for ERC20 transfers.
        /// </summary>
        public string Uri { get; set; }
    }
}
