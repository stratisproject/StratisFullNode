using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <summary>Context checks on a Strax POS block.</summary>
    public class StraxCoinstakeRule : PartialValidationConsensusRule
    {
        // Check that at least one of the coinstake outputs goes to the reward scriptPubKey.
        // The actual percentage of the reward sent to this script is checked within the coinview rule.
        // This is an anyone-can-spend scriptPubKey as no signature is required for the redeem script to be valid.
        // Recall that a scriptPubKey is not network-specific, only the address format that it translates into would depend on the version bytes etc. defined by the network.
        public static readonly Script CirrusRewardScript = new Script(new List<Op>() { OpcodeType.OP_TRUE }).PaymentScript;

        /// <summary>Allow access to the POS parent.</summary>
        protected PosConsensusRuleEngine PosParent;

        /// <inheritdoc />
        public override void Initialize()
        {
            this.PosParent = this.Parent as PosConsensusRuleEngine;

            Guard.NotNull(this.PosParent, nameof(this.PosParent));
        }

        /// <inheritdoc />
        /// <exception cref="ConsensusErrors.BadStakeBlock">The coinbase output (first transaction) is not empty.</exception>
        /// <exception cref="ConsensusErrors.BadStakeBlock">The second transaction is not a coinstake transaction.</exception>
        /// <exception cref="ConsensusErrors.BadMultipleCoinstake">There are multiple coinstake tranasctions in the block.</exception>
        /// <exception cref="ConsensusErrors.BlockTimeBeforeTrx">The block contains a transaction with a timestamp after the block timestamp.</exception>
        public override Task RunAsync(RuleContext context)
        {
            if (context.SkipValidation)
                return Task.CompletedTask;

            Block block = context.ValidationContext.BlockToValidate;

            if (BlockStake.IsProofOfStake(block))
            {
                Transaction coinBase = block.Transactions[0];

                // On the Stratis network, we mandated that the coinbase output must be empty if the block is proof-of-stake.
                // Here, we anticipate that the coinbase will contain the segwit witness commitment.
                // For maximum flexibility in the future we don't want to restrict what else the coinbase in a PoS block can contain, with some limitations:
                // 1. No outputs should be spendable (we could mandate that the PoS reward must be wholly contained in the coinstake, but it is sufficient that the coinbase outputs are unspendable)
                // 2. The first coinbase output must be empty

                // First output must be empty.
                if ((!coinBase.Outputs[0].IsEmpty))
                {
                    this.Logger.LogTrace("(-)[COINBASE_NOT_EMPTY]");
                    ConsensusErrors.BadStakeBlock.Throw();
                }

                // Check that the rest of the outputs are not spendable (OP_RETURN)
                foreach (TxOut txOut in coinBase.Outputs.Skip(1))
                {
                    // Only OP_RETURN scripts are allowed in coinbase.
                    if (!txOut.ScriptPubKey.IsUnspendable)
                    {
                        this.Logger.LogTrace("(-)[COINBASE_SPENDABLE]");
                        ConsensusErrors.BadStakeBlock.Throw();
                    }
                }

                Transaction coinStake = block.Transactions[1];

                // Second transaction must be a coinstake, the rest must not be.
                if (!coinStake.IsCoinStake)
                {
                    this.Logger.LogTrace("(-)[NO_COINSTAKE]");
                    ConsensusErrors.BadStakeBlock.Throw();
                }

                bool cirrusRewardOutput = false;
                foreach (var output in coinStake.Outputs)
                {
                    if (output.ScriptPubKey == CirrusRewardScript)
                    {
                        cirrusRewardOutput = true;
                    }
                }

                // TODO: Add proper Strax-specific consensus error
                if (!cirrusRewardOutput)
                {
                    this.Logger.LogTrace("(-)[MISSING_REWARD_SCRIPT_COINSTAKE_OUTPUT]");
                    ConsensusErrors.BadTransactionNoOutput.Throw();
                }

                if (block.Transactions.Skip(2).Any(t => t.IsCoinStake))
                {
                    this.Logger.LogTrace("(-)[MULTIPLE_COINSTAKE]");
                    ConsensusErrors.BadMultipleCoinstake.Throw();
                }
            }

            return Task.CompletedTask;
        }
    }
}
