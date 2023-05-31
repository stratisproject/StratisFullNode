using Stratis.SmartContracts;

namespace Stratis.Features.Unity3dApi
{
    public class NftContractLocalClient
    {
        public const string SupportsInterfaceMethodName = "SupportsInterface";

        private readonly ILocalCallContract localCallContract;
        private readonly string senderAddress;

        public NftContractLocalClient(ILocalCallContract localCallContract, string senderAddress)
        {
            this.localCallContract = localCallContract;
            this.senderAddress = senderAddress;
        }

        public bool SupportsInterface(ulong? blockHeight, string contractAddress, TokenInterface tokenInterfaceId)
        {
            return this.localCallContract.LocalCallSmartContract<bool>(blockHeight, this.senderAddress, contractAddress, SupportsInterfaceMethodName, (uint)tokenInterfaceId);
        }
    }

    public enum TokenInterface
    {
        ISupportsInterface = 1,
        INonFungibleToken = 2,
        INonFungibleTokenReceiver = 3,
        INonFungibleTokenMetadata = 4,
        INonFungibleTokenEnumerable = 5,
    }
}
