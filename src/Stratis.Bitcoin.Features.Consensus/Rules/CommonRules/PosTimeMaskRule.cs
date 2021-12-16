using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;

namespace Stratis.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <summary>
    /// Checks if <see cref="Block"/> has a valid PoS header.
    /// </summary>
    public class PosTimeMaskRule : PartialValidationConsensusRule
    {
        public PosFutureDriftRule FutureDriftRule { get; set; }

        public override void Initialize()
        {
            base.Initialize();

            this.FutureDriftRule = this.Parent.GetRule<PosFutureDriftRule>();
        }

        /// <inheritdoc />
        /// <exception cref="ConsensusErrors.TimeTooNew">Thrown if block' timestamp too far in the future.</exception>
        /// <exception cref="ConsensusErrors.BadVersion">Thrown if block's version is outdated.</exception>
        /// <exception cref="ConsensusErrors.BlockTimestampTooEarly"> Thrown if the block timestamp is before the previous block timestamp.</exception>
        /// <exception cref="ConsensusErrors.StakeTimeViolation">Thrown if the coinstake timestamp is invalid.</exception>
        /// <exception cref="ConsensusErrors.ProofOfWorkTooHigh">The block's height is higher than the last allowed PoW block.</exception>
        public override Task RunAsync(RuleContext context)
        {
            if (context.SkipValidation)
                return Task.CompletedTask;

            ChainedHeader chainedHeader = context.ValidationContext.ChainedHeaderToValidate;
            this.Logger.LogDebug("Height of block is {0}, block timestamp is {1}, previous block timestamp is {2}, block version is 0x{3:x}.", chainedHeader.Height, chainedHeader.Header.Time, chainedHeader.Previous?.Header.Time, chainedHeader.Header.Version);

            var posRuleContext = context as PosRuleContext;
            posRuleContext.BlockStake = BlockStake.Load(context.ValidationContext.BlockToValidate);

            if (posRuleContext.BlockStake.IsProofOfWork() && (chainedHeader.Height > this.Parent.ConsensusParams.LastPOWBlock))
            {
                this.Logger.LogTrace("(-)[POW_TOO_HIGH]");
                ConsensusErrors.ProofOfWorkTooHigh.Throw();
            }

            if (posRuleContext.BlockStake.IsProofOfStake())
            {
                // We now treat the PoS block header's timestamp as being the timestamp for every transaction in the block.
                if (!this.CheckBlockTimestamp(chainedHeader.Header.Time))
                {
                    this.Logger.LogTrace("(-)[BAD_TIME]");
                    ConsensusErrors.StakeTimeViolation.Throw();
                }
            }

            return Task.CompletedTask;
        }

        private bool CheckBlockTimestamp(long blockTime)
        {
            return (blockTime & PosConsensusOptions.StakeTimestampMask) == 0;
        }
    }
}
