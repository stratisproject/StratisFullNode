using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Utilities;
using System;
using System.Threading.Tasks;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.EventBus;

namespace Stratis.Features.Collateral
{
    public interface IImmatureCollateralKicker : IDisposable
    {

        /// <summary>
        /// Determines if a federation member should be kicked.
        /// </summary>
        /// <param name="federationMember">Member to check activity of.</param>
        /// <returns><c>True</c> if the member should be kicked and <c>false</c> otherwise.</returns>
        bool ShouldMemberBeKicked(IFederationMember federationMember, int counterChainHeight);

        /// <summary>
        /// Determine whether or not any miners should be scheduled to be kicked from the federation at the current tip.
        /// </summary>
        void OnBlockConnected(BlockConnected blockConnected);

        /// <summary>
        /// Initializes this component.
        /// </summary>
        Task InitializeAsync();
    }

    /// <summary>
    /// Automatically schedules addition of voting data that votes for kicking federation member that
    /// didn't produce a block in <see cref="PoAConsensusOptions.FederationMemberMaxIdleTimeSeconds"/>.
    /// </summary>
    public class ImmatureCollateralKicker : IImmatureCollateralKicker
    {
        private readonly Network network;

        private readonly ISignals signals;

        private readonly VotingManager votingManager;

        private readonly IFederationHistory federationHistory;

        private readonly ILogger logger;

        private readonly PoAConsensusFactory consensusFactory;

        private readonly ICollateralChecker collateralChecker;

        private readonly PollsRepository pollsRepository;

        private readonly object lockObject;

        private const int MaturityWindow = 3840; // 48 hours
        
        private SubscriptionToken blockConnectedSubscription;

        public ImmatureCollateralKicker(Network network, ISignals signals, VotingManager votingManager, IFederationHistory federationHistory, ILoggerFactory loggerFactory, ICollateralChecker collateralChecker, PollsRepository pollsRepository)
        {
            this.network = network;
            this.signals = signals;
            this.votingManager = votingManager;
            this.federationHistory = federationHistory;
            this.collateralChecker = collateralChecker;
            this.pollsRepository = pollsRepository;

            this.consensusFactory = this.network.Consensus.ConsensusFactory as PoAConsensusFactory;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.lockObject = new object();
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            this.blockConnectedSubscription = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);
        }

        /// <inheritdoc />
        public bool ShouldMemberBeKicked(IFederationMember federationMember, int counterChainHeight)
        {
            Guard.NotNull(federationMember, nameof(federationMember));

            lock (this.lockObject)
            {
                if (!(federationMember is CollateralFederationMember collateralFederationMember))
                {
                    return false;
                }

                if (collateralFederationMember.IsMultisigMember)
                {
                    return false;
                }

                int firstBlock = this.pollsRepository.GetFirstRegisteredBlock(collateralFederationMember.PubKey);

                if (firstBlock == -1)
                {
                    return false;
                }

                // Newly registered nodes get a grace period. The grace period is insufficient for the node to accrue enough rewards to offset the registration fee
                // (given a large enough federation) so the grace period cannot be abused to move collateral with any regularity.
                if ((counterChainHeight - MaturityWindow) < firstBlock)
                {
                    return false;
                }

                AddressIndexerData addressIndexerData = this.collateralChecker.GetAddressIndexerData(collateralFederationMember.CollateralMainchainAddress);

                if (addressIndexerData == null)
                {
                    return false;
                }

                return addressIndexerData.BalanceChanges.CalculateMinBalance(counterChainHeight - MaturityWindow) >= collateralFederationMember.CollateralAmount.Satoshi;
            }
        }

        /// <inheritdoc />
        public void OnBlockConnected(BlockConnected blockConnected)
        {
            lock (this.lockObject)
            {
                if (blockConnected.ConnectedBlock.ChainedHeader.Height == 0)
                    return;
                
                int collateralImmaturityActivationHeight = ((PoAConsensusOptions)this.network.Consensus.Options).CollateralImmaturityActivationHeight;
                if (blockConnected.ConnectedBlock.ChainedHeader.Height < collateralImmaturityActivationHeight)
                    return;

                try
                {
                    int counterChainHeight = this.collateralChecker.GetCounterChainConsensusHeight();

                    // Check if any fed member has insufficient collateral maturity, and schedule them to be kicked if so.
                    foreach (IFederationMember federationMember in this.federationHistory.GetFederationForBlock(blockConnected.ConnectedBlock.ChainedHeader))
                    {
                        if (!this.ShouldMemberBeKicked(federationMember, counterChainHeight))
                            continue;

                        byte[] federationMemberBytes = this.consensusFactory.SerializeFederationMember(federationMember);

                        bool alreadyKicking = this.votingManager.AlreadyVotingFor(VoteKey.KickFederationMember, federationMemberBytes);

                        if (!alreadyKicking)
                        {
                            this.logger.LogWarning("Federation member '{0}' has insufficient collateral maturity and will be scheduled to be kicked.", federationMember.PubKey);

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
            this.blockConnectedSubscription?.Dispose();
        }
    }
}
