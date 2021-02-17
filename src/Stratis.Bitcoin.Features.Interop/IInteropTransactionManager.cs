using System.Numerics;
using NBitcoin;

namespace Stratis.Bitcoin.Features.Interop
{
    public interface IInteropTransactionManager
    {
        void AddVote(string requestId, BigInteger transactionId, PubKey pubKey);

        BigInteger GetAgreedTransactionId(string requestId, int quorum);

        void RemoveTransaction(string requestId);
    }
}
