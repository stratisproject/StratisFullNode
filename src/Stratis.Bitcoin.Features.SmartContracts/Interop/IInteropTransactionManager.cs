using System.Numerics;

namespace Stratis.Bitcoin.Features.SmartContracts.Interop
{
    public interface IInteropTransactionManager
    {
        void AddVote(string requestId, BigInteger transactionId);

        BigInteger GetAgreedTransactionId(string requestId, int quorum);

        void RemoveTransaction(string requestId);
    }
}
