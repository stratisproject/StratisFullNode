using NBitcoin;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface ISystemContractTransactionContext
    {
        SystemContractCall CallData { get; }
        IStateRepositoryRoot State { get; }
        Transaction Transaction { get; }
    }

    public class SystemContractTransactionContext : ISystemContractTransactionContext
    {
        public SystemContractTransactionContext(
            IStateRepositoryRoot state,
            Transaction transaction,
            SystemContractCall callData)
        {
            this.State = state;
            this.Transaction = transaction;
            this.CallData = callData;
        }

        public Transaction Transaction { get; }

        public SystemContractCall CallData { get; }

        public IStateRepositoryRoot State { get; }
    }
}
