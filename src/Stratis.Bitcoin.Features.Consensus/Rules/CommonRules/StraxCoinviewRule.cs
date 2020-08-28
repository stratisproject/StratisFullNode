using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;

namespace Stratis.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <summary>
    /// Proof of stake override for the coinview rules - BIP68, MaxSigOps and BlockReward checks.
    /// </summary>
    public sealed class StraxCoinviewRule : PosCoinviewRule
    {
        // (Provisionally) 2% of the block reward should be assigned to the reward script.
        // This has to be within the coinview rule because we need access to the coinstake input value to determine the size of the block reward.
        public static readonly int CirrusRewardPercentage = 2;

        // TODO: We further need to check that any transactions that spend outputs from the reward script only go to the cross-chain multisig.

        /// <inheritdoc />
        public override void CheckBlockReward(RuleContext context, Money fees, int height, Block block)
        {
            if (BlockStake.IsProofOfStake(block))
            {
                var posRuleContext = context as PosRuleContext;
                Money stakeReward = block.Transactions[1].TotalOut - posRuleContext.TotalCoinStakeValueIn;
                Money calcStakeReward = fees + this.GetProofOfStakeReward(height);

                this.Logger.LogDebug("Block stake reward is {0}, calculated reward is {1}.", stakeReward, calcStakeReward);
                if (stakeReward > calcStakeReward)
                {
                    this.Logger.LogTrace("(-)[BAD_COINSTAKE_AMOUNT]");
                    ConsensusErrors.BadCoinstakeAmount.Throw();
                }

                // Compute the total reward amount sent to the reward script.
                // We only mandate that at least x% of the reward is sent there, there are no other constraints on what gets done with the rest of the reward.
                Money rewardScriptTotal = Money.Coins(0.0m);

                foreach (TxOut output in block.Transactions[1].Outputs)
                {
                    // TODO: Double check which rule we have the negative output (and overflow) amount check inside; we assume that has been done before this check
                    if (output.ScriptPubKey == StraxCoinstakeRule.CirrusRewardScript)
                        rewardScriptTotal += output.Value;
                }

                // It must be x% of the maximum possible reward. It must not be possible to short-change it by deliberately sacrificing the rest of the claimed reward.
                // TODO: Create a distinct consensus error for this?
                if ((calcStakeReward * CirrusRewardPercentage / 100) > rewardScriptTotal)
                {
                    this.Logger.LogTrace("(-)[BAD_COINSTAKE_REWARD_SCRIPT_AMOUNT]");
                    ConsensusErrors.BadCirrusRewardAmount.Throw();
                }

                // TODO: Perhaps we should limit it to a single output to prevent unnecessary UTXO set bloating
            }
            else
            {
                Money blockReward = fees + this.GetProofOfWorkReward(height);
                this.Logger.LogDebug("Block reward is {0}, calculated reward is {1}.", block.Transactions[0].TotalOut, blockReward);
                if (block.Transactions[0].TotalOut > blockReward)
                {
                    this.Logger.LogTrace("(-)[BAD_COINBASE_AMOUNT]");
                    ConsensusErrors.BadCoinbaseAmount.Throw();
                }

                // TODO: Should the reward split apply to blocks in the POW phase of the network too?
            }
        }
    }
}
