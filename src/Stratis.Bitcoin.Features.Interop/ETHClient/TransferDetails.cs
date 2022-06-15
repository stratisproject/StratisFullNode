using System.Numerics;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    public enum ContractType
    {
        ERC20 = 0,
        ERC721 = 1
    }

    public enum TransferType
    {
        // We don't currently need to make a distinction between transfers and mints.
        Transfer = 0,
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
    }
}
