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

        public SystemContractRule(ILogger logger, IStateRepositoryRoot stateRepositoryRoot)
        {
            this.logger = logger;
            this.stateRepositoryRoot = stateRepositoryRoot;
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

            // TODO get the new (uncommitted) state repository returned by the execution
            IStateRepositoryRoot mutableStateRepository = null;

            // CONSENSUS - Check that the execution resulted in an identical hashstateroot to the block header
            var mutableStateRepositoryRoot = new uint256(mutableStateRepository.Root);
            uint256 blockHeaderHashStateRoot = blockHeader.HashStateRoot;

            this.logger.LogDebug("Compare state roots '{0}' and '{1}'", mutableStateRepositoryRoot, blockHeaderHashStateRoot);

            if (mutableStateRepositoryRoot != blockHeaderHashStateRoot)
                SmartContractConsensusErrors.UnequalStateRoots.Throw();

            // Push to underlying database
            mutableStateRepository.Commit();

            // Update the globally injected state so all services receive the updates.
            this.stateRepositoryRoot.SyncToRoot(mutableStateRepository.Root);
        }
    }
}
