using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoW;
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
        private readonly ISystemContractRunner runner;

        public SystemContractRule(ILogger logger, IStateRepositoryRoot stateRepositoryRoot, ISystemContractRunner runner)
        {
            this.logger = logger;
            this.stateRepositoryRoot = stateRepositoryRoot;
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
                blockRoot = SmartContractBlockDefinition.StateRootEmptyTrie;

            this.logger.LogDebug("Block hash state root '{0}'.", blockRoot);

            var blockHeader = (ISmartContractBlockHeader)block.Header;

            // TODO verify - ordering is important here.
            foreach (Transaction transaction in context.ValidationContext.BlockToValidate.Transactions)
            {
                this.logger.LogDebug("Validating transaction '{0}'.", transaction);

                // This should never be null because it's checked in a consensus rule.
                var serializedCallData = transaction.Outputs.FirstOrDefault(x => x.ScriptPubKey.IsSmartContractExec())?.ScriptPubKey.ToBytes();

                // TODO get the new (uncommitted) state repository returned by the execution
                ISystemContractExecutionResult executionResult = this.runner.Execute(null);

                IStateRepositoryRoot newState = executionResult.NewState;

                // CONSENSUS - Check that the execution resulted in an identical hashstateroot to the block header
                var mutableStateRepositoryRoot = new uint256(newState.Root);
                uint256 blockHeaderHashStateRoot = blockHeader.HashStateRoot;

                this.logger.LogDebug("Compare state roots '{0}' and '{1}'", mutableStateRepositoryRoot, blockHeaderHashStateRoot);

                if (mutableStateRepositoryRoot != blockHeaderHashStateRoot)
                    SmartContractConsensusErrors.UnequalStateRoots.Throw();

                // Push to underlying database
                newState.Commit();

                // Update the globally injected state so all services receive the updates.
                this.stateRepositoryRoot.SyncToRoot(newState.Root);
            }
        }
    }
}