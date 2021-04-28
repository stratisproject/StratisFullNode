using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.Consensus.Interfaces;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Mining;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.SystemContracts;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SmartContracts
{
    /// <summary>
    /// Derives from <see cref="PosBlockDefinition"/>. Amends a few things for system contracts.
    /// </summary>
    public class SmartContractPosBlockDefinition : PosBlockDefinition
    {
        public static uint256 StateRootEmptyTrie = new uint256("21B463E3B52F6201C0AD6C991BE0485B6EF8C092E64583FFA655CC1B171FE856");

        private readonly IStateRepositoryRoot stateRoot;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly ISystemContractRunner runner;
        private readonly ILogger logger;
        private IStateRepositoryRoot blockStateSnapshot;

        public SmartContractPosBlockDefinition(
            IConsensusManager consensusManager,
            IDateTimeProvider dateTimeProvider,
            ILoggerFactory loggerFactory,
            ITxMempool mempool,
            MempoolSchedulerLock mempoolLock,
            Network network,
            MinerSettings minerSettings,
            IStakeChain stakeChain,
            IStakeValidator stakeValidator,
            NodeDeployments nodeDeployments,
            IStateRepositoryRoot stateRoot,
            ICallDataSerializer callDataSerializer,
            ISystemContractRunner runner)
                : base(consensusManager, dateTimeProvider, loggerFactory, mempool, mempoolLock, minerSettings, network, stakeChain, stakeValidator, nodeDeployments)
        {
            this.stateRoot = stateRoot;
            this.callDataSerializer = callDataSerializer;
            this.runner = runner;
            this.logger = loggerFactory.CreateLogger(typeof(SmartContractPosBlockDefinition).FullName);
        }

        /// <summary>
        /// Overrides the <see cref="AddToBlock(TxMempoolEntry)"/> behaviour of <see cref="BlockDefinition"/>.
        /// <para>
        /// Determine whether or not the mempool entry contains smart contract execution
        /// code. If not, then add to the block as per normal. Else extract and deserialize
        /// the smart contract code from the TxOut's ScriptPubKey.
        /// </para>
        /// </summary>
        /// <param name="mempoolEntry">The mempool entry containing the transactions to include.</param>
        public override void AddToBlock(TxMempoolEntry mempoolEntry)
        {
            // Executes the smart contract within a transaction, if any. Updates the block's state snapshot accordingly.
            TxOut smartContractTxOut = mempoolEntry.Transaction.TryGetSmartContractCallTxOut();

            if (smartContractTxOut != null)
            {
                this.logger.LogDebug("Transaction contains smart contract information.");

                SystemContractTransactionContext systemContractContext = GetContext(this.block, mempoolEntry.Transaction, this.blockStateSnapshot);

                // Execute contract
                ISystemContractExecutionResult result = this.runner.Execute(systemContractContext);

                // Update the block state snapshot.
                this.blockStateSnapshot.SyncToRoot(result.NewState.Root);
            }
            else
            {
                this.logger.LogDebug("Transaction does not contain smart contract information.");
            }

            base.AddToBlock(mempoolEntry);
        }

        private SystemContractTransactionContext GetContext(Block block, Transaction transaction, IStateRepositoryRoot state)
        {
            // This should never be null because it's checked in a consensus rule.
            var serializedCallData = transaction.Outputs.FirstOrDefault(x => x.ScriptPubKey.IsSmartContractExec())?.ScriptPubKey.ToBytes();

            // This should always be successful because it's checked in a consensus rule.
            // TODO we can consider changing the call data serialization scheme to something that suits our needs better?
            ContractTxData callData = this.callDataSerializer.Deserialize(serializedCallData).Value;

            var systemContractCall = new SystemContractCall(callData.ContractAddress, callData.MethodName, callData.MethodParameters, callData.VmVersion);

            return new SystemContractTransactionContext(state, block, transaction, systemContractCall);
        }

        /// <inheritdoc/>
        public override BlockTemplate Build(ChainedHeader chainTip, Script scriptPubKeyIn)
        {
            // We're at the start of the block so get the right state root and sync it.
            if (this.ConsensusManager.Tip.Header is PosBlockHeader posBlockHeader && !posBlockHeader.HasSmartContractFields)
            {
                uint256 rootHash = StateRootEmptyTrie;
                this.stateRoot.SyncToRoot(rootHash.ToBytes());
                this.blockStateSnapshot = this.stateRoot.GetSnapshotTo(rootHash.ToBytes());
            }
            else
            {
                this.blockStateSnapshot = this.stateRoot.GetSnapshotTo(((ISmartContractBlockHeader)this.ConsensusManager.Tip.Header).HashStateRoot.ToBytes());
            }

            // The execution flow here is hard to follow due to inheritance mess but it goes like this:
            // * base.OnBuild
            // -> this.ComputeBlockVersion
            // -> base.AddTransactions -> this.AddToBlock
            // -> this.UpdateHeaders
            base.Build(chainTip, scriptPubKeyIn);

            return this.BlockTemplate;
        }

        /// <inheritdoc/>
        public override void UpdateHeaders()
        {
            base.UpdateHeaders();

            // Add the hash state root.
            if (this.block.Header is ISmartContractBlockHeader scHeader && scHeader.HasSmartContractFields)
            {
                scHeader.HashStateRoot = new uint256(this.blockStateSnapshot.Root);
            }
        }

        /// <inheritdoc/>
        protected override void ComputeBlockVersion()
        {
            base.ComputeBlockVersion();
            this.block.Header.Version |= PosBlockHeader.ExtendedHeaderBit;
        }
    }
}