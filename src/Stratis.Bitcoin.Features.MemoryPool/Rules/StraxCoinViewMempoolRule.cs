using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.MemoryPool.Rules
{
    /// <summary>
    /// Validates the transaction with the coin view.
    /// Checks if already in coin view, and missing and unavailable inputs.
    /// </summary>
    public class StraxCoinViewMempoolRule : CheckCoinViewMempoolRule
    {
        public StraxCoinViewMempoolRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
        }

        /// <remarks>Also see <see cref="StraxCoinviewRule"/></remarks>>
        public override void CheckTransaction(MempoolValidationContext context)
        {
            base.CheckTransaction(context);

            foreach (TxIn txin in context.Transaction.Inputs)
            {
                // We expect that by this point the base rule will have checked for missing inputs.
                UnspentOutput unspentOutput = context.View.Set.AccessCoins(txin.PrevOut);
                if (unspentOutput?.Coins == null)
                {
                    context.State.MissingInputs = true;
                    this.logger.LogTrace("(-)[FAIL_MISSING_INPUTS_ACCESS_COINS]");
                    context.State.Fail(MempoolErrors.MissingOrSpentInputs).Throw();
                }

                if (unspentOutput.Coins.TxOut.ScriptPubKey == StraxCoinstakeRule.CirrusRewardScript)
                {
                    foreach (TxOut output in context.Transaction.Outputs)
                    {
                        if (output.ScriptPubKey.IsUnspendable)
                        {
                            if (output.Value != 0)
                            {
                                this.logger.LogTrace("(-)[INVALID_REWARD_OP_RETURN_SPEND]");
                                ConsensusErrors.BadTransactionScriptError.Throw();
                            }

                            continue;
                        }

                        // Every other (spendable) output must go to the multisig
                        if (output.ScriptPubKey != this.network.Federations.GetOnlyFederation().MultisigScript)
                        {
                            this.logger.LogTrace("(-)[INVALID_REWARD_SPEND_DESTINATION]");
                            ConsensusErrors.BadTransactionScriptError.Throw();
                        }
                    }
                }
            }
        }
    }
}
