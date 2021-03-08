namespace Stratis.Bitcoin.Features.SmartContracts.Interfaces
{
    public interface IContractCodeHashingStrategy
    {
        byte[] Hash(byte[] data);
    }
}