using System;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public interface IIdleFederationMembersKicker : IDisposable
    {

        /// <summary>
        /// Determines if a federation member should be kicked.
        /// </summary>
        /// <param name="federationMember">Member to check activity of.</param>
        /// <param name="chainedHeader">Block at which to check for past activity.</param>
        /// <param name="consensusTip">Typically the poll repository tip.</param>
        /// <param name="inactiveForSeconds">Number of seconds member was inactive for.</param>
        /// <returns><c>True</c> if the member should be kicked and <c>false</c> otherwise.</returns>
        bool ShouldMemberBeKicked(IFederationMember federationMember, ChainedHeader chainedHeader, ChainedHeader consensusTip, out uint inactiveForSeconds);

        /// <summary>
        /// Determine whether or not any miners should be scheduled to be kicked from the federation at the current tip.
        /// </summary>
        /// <param name="consensusTip">The current consenus tip.</param>
        void Execute(ChainedHeader consensusTip);

        /// <summary>
        /// Initializes this component.
        /// </summary>
        void Initialize();
    }

    /// <summary>
    /// Automatically schedules addition of voting data that votes for kicking federation member that
    /// didn't produce a block in <see cref="PoAConsensusOptions.FederationMemberMaxIdleTimeSeconds"/>.
    /// </summary>
    public class IdleFederationMembersKicker : IIdleFederationMembersKicker
    {
        private readonly Network network;

        private readonly VotingManager votingManager;

        private readonly IFederationHistory federationHistory;

        private readonly ILogger logger;

        private readonly uint federationMemberMaxIdleTimeSeconds;

        private readonly PoAConsensusFactory consensusFactory;

        private readonly object lockObject;

        public IdleFederationMembersKicker(Network network, IKeyValueRepository keyValueRepository, IConsensusManager consensusManager, IAsyncProvider asyncProvider,
            IFederationManager federationManager, VotingManager votingManager, IFederationHistory federationHistory, ILoggerFactory loggerFactory)
        {
            this.network = network;
            this.votingManager = votingManager;
            this.federationHistory = federationHistory;

            this.consensusFactory = this.network.Consensus.ConsensusFactory as PoAConsensusFactory;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.federationMemberMaxIdleTimeSeconds = ((PoAConsensusOptions)network.Consensus.Options).FederationMemberMaxIdleTimeSeconds;
            this.lockObject = new object();
        }

        /// <inheritdoc />
        public void Initialize()
        {
        }

        /// <inheritdoc />
        public bool ShouldMemberBeKicked(IFederationMember federationMember, ChainedHeader blockHeader, ChainedHeader currentTip, out uint inactiveForSeconds)
        {
            Guard.NotNull(federationMember, nameof(federationMember));

            lock (this.lockObject)
            {
                PubKey pubKey = federationMember.PubKey;

                uint lastActiveTime = this.network.GenesisTime;
                if (this.federationHistory.GetLastActiveTime(federationMember, currentTip, out lastActiveTime))
                    lastActiveTime = (lastActiveTime != default) ? lastActiveTime : this.network.GenesisTime;

                uint blockTime = blockHeader.Header.Time;

                inactiveForSeconds = blockTime - lastActiveTime;

                // This might happen in test setup scenarios.
                if (blockTime < lastActiveTime)
                    inactiveForSeconds = 0;

                return inactiveForSeconds > this.federationMemberMaxIdleTimeSeconds && !(federationMember is CollateralFederationMember collateralFederationMember && collateralFederationMember.IsMultisigMember);
            }
        }

        /// <inheritdoc />
        public void Execute(ChainedHeader consensusTip)
        {
            lock (this.lockObject)
            {
                // No member can be kicked at genesis.
                if (consensusTip.Height == 0)
                    return;

                // Federation member kicking is not yet enabled.
                var federationMemberActivationTime = ((PoAConsensusOptions)this.network.Consensus.Options).FederationMemberActivationTime;
                if (federationMemberActivationTime != null &&
                    federationMemberActivationTime > 0 &&
                    consensusTip.Header.Time < federationMemberActivationTime)
                    return;

                try
                {
                    // Check if any fed member was idle for too long. Use the timestamp of the mined block.
                    foreach ((IFederationMember federationMember, uint lastActiveTime) in this.federationHistory.GetFederationMembersByLastActiveTime())
                    {
                        if (this.ShouldMemberBeKicked(federationMember, consensusTip, consensusTip, out uint inactiveForSeconds))
                        {
                            byte[] federationMemberBytes = this.consensusFactory.SerializeFederationMember(federationMember);

                            bool alreadyKicking = this.votingManager.AlreadyVotingFor(VoteKey.KickFederationMember, federationMemberBytes);

                            if (!alreadyKicking)
                            {
                                this.logger.LogWarning("Federation member '{0}' was inactive for {1} seconds and will be scheduled to be kicked.", federationMember.PubKey, inactiveForSeconds);

                                this.votingManager.ScheduleVote(new VotingData()
                                {
                                    Key = VoteKey.KickFederationMember,
                                    Data = federationMemberBytes
                                });
                            }
                            else
                            {
                                this.logger.LogDebug("Skipping because kicking is already voted for.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, ex.ToString());
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
