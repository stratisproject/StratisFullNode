namespace Stratis.Bitcoin.Features.SmartContracts.Interfaces
{
    public interface IWhitelistedHashChecker
    {
        bool CheckHashWhitelisted(byte[] hash);
    }
}
