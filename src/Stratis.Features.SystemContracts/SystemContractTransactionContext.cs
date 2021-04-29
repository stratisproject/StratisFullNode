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

    /// <summary>
    /// The context for the system contract call. Includes the method call data and the current state.
    /// 
    /// The <see cref="Transaction"/> in which the call is being executed is also included (though currently unused)
    /// as an example of passing block-specific context to the call.
    /// </summary>
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
