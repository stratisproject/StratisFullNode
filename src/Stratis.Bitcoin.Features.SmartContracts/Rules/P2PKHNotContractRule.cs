using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Bitcoin.Features.SmartContracts.Rules
{
    /// <summary>
    /// Used to check that people don't try and send funds to contracts via P2PKH.
    /// </summary>
    public class P2PKHNotContractRule : FullValidationConsensusRule
    {
        private readonly IStateRepositoryRoot stateRepositoryRoot;
        private readonly int? lastCheckPointHeight;

        public P2PKHNotContractRule(IStateRepositoryRoot stateRepositoryRoot, Network network)
        {
            this.stateRepositoryRoot = stateRepositoryRoot;
            this.lastCheckPointHeight = network.Checkpoints.OrderByDescending(c => c.Key).Select(c => c.Key).FirstOrDefault();
        }

        /// <inheritdoc/>
        public override Task RunAsync(RuleContext context)
        {
            if (context.ValidationContext.ChainedHeaderToValidate.Height <= this.lastCheckPointHeight)
                return Task.CompletedTask;

            Block block = context.ValidationContext.BlockToValidate;

            foreach (Transaction transaction in block.Transactions)
            {
                CheckTransaction(this.stateRepositoryRoot, transaction);
            }

            return Task.CompletedTask;
        }

        public static void CheckTransaction(IStateRepositoryRoot stateRepositoryRoot, Transaction transaction)
        {
            foreach(TxOut output in transaction.Outputs)
            {
                if (PayToPubkeyHashTemplate.Instance.CheckScriptPubKey(output.ScriptPubKey))
                {
                    KeyId p2pkhParams = PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(output.ScriptPubKey);
                    var to = new uint160(p2pkhParams.ToBytes());
                    if (stateRepositoryRoot.GetAccountState(to) != null)
                        new ConsensusError("p2pkh-to-contract", "attempted send directly to contract address. use OP_CALL instead.").Throw();
                }
            }
        }
    }
}
