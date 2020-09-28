using NBitcoin;

namespace Stratis.Bitcoin.Features.Wallet
{
    public interface IAccountRestrictedKeyId
    {
        int AccountId { get; }
    }

    public class AccountRestrictedKeyId : KeyId, IAccountRestrictedKeyId
    {
        public int AccountId { get; private set; }

        public AccountRestrictedKeyId(byte[] keyBytes, int accountId) : base(keyBytes)
        {
            this.AccountId = accountId;
        }
        public AccountRestrictedKeyId(KeyId keyId, int accountId) : this(keyId.ToBytes(), accountId)
        {
        }
    }
}
