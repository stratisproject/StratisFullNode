using NBitcoin;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface ISystemContractTransactionContext
    {
        Block Block { get; }
        SystemContractCall CallData { get; }
        IStateRepositoryRoot State { get; }
        Transaction Transaction { get; }
    }

    public class SystemContractTransactionContext : ISystemContractTransactionContext
    {
        public SystemContractTransactionContext(
            IStateRepositoryRoot state,
            Block block,
            Transaction transaction,
            SystemContractCall callData)
        {
            this.State = state;
            this.Block = block;
            this.Transaction = transaction;
            this.CallData = callData;
            this.contractTxOut = transaction.Outputs.FirstOrDefault(x => x.ScriptPubKey.IsSmartContractExec());

        }

        public Block Block { get; }

        public Transaction Transaction { get; }

        public SystemContractCall CallData { get; }

        public IStateRepositoryRoot State { get; }

        /// <inheritdoc />
        public byte[] Data
        {
            get { return this.contractTxOut.ScriptPubKey.ToBytes(); }
        }


    }
}
