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
                // TODO: We may want to remove this limitation to allow future flexibility, or more close alignment with Bitcoin Core's segwit implementation.
                // Coinbase output should be empty if proof-of-stake block.
                if ((block.Transactions[0].Outputs.Count != 1) || !block.Transactions[0].Outputs[0].IsEmpty)
                {
                    this.Logger.LogTrace("(-)[COINBASE_NOT_EMPTY]");
                    ConsensusErrors.BadStakeBlock.Throw();
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

                // TODO: Add proper Strax-speciific consensus error
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

            // TODO: Remove this with the nTime field removal
            // Check transactions.
            foreach (Transaction transaction in block.Transactions)
            {
                // Check transaction timestamp.
                if (block.Header.Time < transaction.Time)
                {
                    this.Logger.LogDebug("Block contains transaction with timestamp {0}, which is greater than block's timestamp {1}.", transaction.Time, block.Header.Time);
                    this.Logger.LogTrace("(-)[TX_TIME_MISMATCH]");
                    ConsensusErrors.BlockTimeBeforeTrx.Throw();
                }
            }

            return Task.CompletedTask;
        }
    }
}
