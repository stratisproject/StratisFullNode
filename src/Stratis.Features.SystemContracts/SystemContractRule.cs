using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.Bitcoin.Features.SmartContracts.PoW;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Consensus rule for executing system contracts. Executes the contract and checks that the state repository root is the same as what's in the block header.
    /// </summary>
    public class SystemContractRule : FullValidationConsensusRule
    {
        private readonly ILogger logger;
        private readonly IStateRepositoryRoot stateRepositoryRoot;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly IWhitelistedHashChecker whitelistedHashChecker;
        private readonly ISystemContractRunner runner;

        public SystemContractRule(
            ILoggerFactory loggerFactory,
            IStateRepositoryRoot stateRepositoryRoot,
            ICallDataSerializer callDataSerializer,
            IWhitelistedHashChecker whitelistedHashChecker,
            ISystemContractRunner runner)
        {
            this.logger = loggerFactory.CreateLogger(typeof(SystemContractRule).FullName);
            this.stateRepositoryRoot = stateRepositoryRoot;
            this.callDataSerializer = callDataSerializer;
            this.whitelistedHashChecker = whitelistedHashChecker;
            this.runner = runner;
        }

        public override async Task RunAsync(RuleContext context)
        {
            Block block = context.ValidationContext.BlockToValidate;
            this.logger.LogDebug("Block to validate '{0}'", block.GetHash());

            // Get a IStateRepositoryRoot we can alter without affecting the injected one which is used elsewhere.
            BlockHeader prevHeader = context.ValidationContext.ChainedHeaderToValidate.Previous.Header;
            uint256 blockRoot;
            if (!(prevHeader is PosBlockHeader posHeader) || posHeader.HasSmartContractFields)
                blockRoot = ((ISmartContractBlockHeader)prevHeader).HashStateRoot;
            else
                blockRoot = SmartContractPosBlockDefinition.StateRootEmptyTrie;

            this.logger.LogDebug("Block hash state root '{0}'.", blockRoot);

            var blockHeader = (ISmartContractBlockHeader)block.Header;

            // Get the snapshot to which we will apply all our block changes.
            IStateRepositoryRoot state = this.stateRepositoryRoot.GetSnapshotTo(blockRoot.ToBytes());

            // TODO verify - ordering is important here.
            foreach (Transaction transaction in context.ValidationContext.BlockToValidate.Transactions)
            {
                this.logger.LogDebug("Validating transaction '{0}'.", transaction);

                // This should never be null because it's checked in a consensus rule.
                var serializedCallData = transaction.Outputs.FirstOrDefault(x => x.ScriptPubKey.IsSmartContractExec())?.ScriptPubKey.ToBytes();

                // This should always be successful because it's checked in a consensus rule.
                // TODO we can consider changing the call data serialization scheme to something that suits our needs better?
                ContractTxData callData = this.callDataSerializer.Deserialize(serializedCallData).Value;

                var systemContractCall = new SystemContractCall(callData.ContractAddress, callData.MethodName, callData.MethodParameters, callData.VmVersion);

                // TODO is it correct to check the whitelist with the "identifier" here?
                if (!this.whitelistedHashChecker.CheckHashWhitelisted(systemContractCall.Identifier.ToBytes()))
                {
                    this.logger.LogDebug("Contract is not whitelisted '{0}'.", systemContractCall.Identifier);

                    // Continue to next transaction.
                    continue;
                }

                var systemContractContext = new SystemContractTransactionContext(state, transaction, systemContractCall);

                // TODO get the new (uncommitted) state repository returned by the execution
                ISystemContractRunnerResult executionResult = this.runner.Execute(systemContractContext);

                IStateRepositoryRoot newState = executionResult.NewState;

                // CONSENSUS - Check that the execution resulted in an identical hashstateroot to the block header
                var mutableStateRepositoryRoot = new uint256(newState.Root);
                uint256 blockHeaderHashStateRoot = blockHeader.HashStateRoot;

                this.logger.LogDebug("Compare state roots '{0}' and '{1}'", mutableStateRepositoryRoot, blockHeaderHashStateRoot);

                // TODO - this will prevent validation of all remaining transactions in the block?
                if (mutableStateRepositoryRoot != blockHeaderHashStateRoot)
                    new ConsensusError("invalid-state-roots", "contract state root not matching after block execution").Throw();

                // Push to underlying database
                // TODO is this necessary before the block is done?
                newState.Commit();

                // Update the state root for the next transaction.
                state.SyncToRoot(newState.Root);
            }

            // Commit the block.
            state.Commit();
        }
    }
}