using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.PoA.Collateral;
using Stratis.Features.PoA.Collateral.CounterChain;

namespace Stratis.Bitcoin.Features.PoA
{
    public interface IFederationManager
    {
        CollateralFederationMember CollateralAddressOwner(VotingManager votingManager, VoteKey voteKey, string address);

        /// <summary><c>true</c> in case current node is a federation member.</summary>
        bool IsFederationMember { get; }

        /// <summary>Current federation member's private key. <c>null</c> if <see cref="IsFederationMember"/> is <c>false</c>.</summary>
        Key CurrentFederationKey { get; }

        void Initialize();

        /// <summary>Provides up to date list of federation members.</summary>
        /// <remarks>
        /// Blocks that are not signed with private keys that correspond
        /// to public keys from this list are considered to be invalid.
        /// </remarks>
        List<IFederationMember> GetFederationMembers(ChainedHeader chainedHeader = null);

        bool IsMultisigMember(PubKey pubKey, ChainedHeader chainedHeader = null);

        /// <summary>Gets the height at which the Strax-era started.</summary>
        /// <returns>The block height at which the Strax era started.</returns>
        /// <remarks>This is the height for which <see cref="PoANetwork.StraxMiningMultisigMembers"/> is applicable.</remarks>
        int? GetMultisigMinersApplicabilityHeight();

        /// <summary>Provides federation member of this node or <c>null</c> if <see cref="IsFederationMember"/> is <c>false</c>.</summary>
        /// <returns>The current federation member instance.</returns>
        IFederationMember GetCurrentFederationMember();
    }

    public sealed class FederationManager : IFederationManager
    {
        private VotingManager votingManager => this.fullNode.NodeService<VotingManager>();

        /// <inheritdoc />
        public bool IsFederationMember => this.GetFederationMembers(null).Any(x => x.PubKey == this.CurrentFederationKey?.PubKey);

        /// <inheritdoc />
        public Key CurrentFederationKey { get; private set; }

        /// <summary>
        /// This can be null if the side chain node is started in dev mode.
        /// </summary>
        private readonly ICounterChainSettings counterChainSettings;

        private readonly IFullNode fullNode;

        /// <summary>Protects access to <see cref="federationMembers"/>.</summary>
        private readonly object locker;
        private readonly ILogger logger;
        private readonly PoANetwork network;
        private readonly NodeSettings nodeSettings;
        private readonly ISignals signals;

        private int? multisigMinersApplicabilityHeight;
        private ChainedHeader lastBlockChecked;

        public FederationManager(
            IFullNode fullNode,
            Network network,
            NodeSettings nodeSettings,
            ICounterChainSettings counterChainSettings = null)
        {
            this.counterChainSettings = counterChainSettings;
            this.fullNode = fullNode;
            this.network = Guard.NotNull(network as PoANetwork, nameof(network));
            this.nodeSettings = Guard.NotNull(nodeSettings, nameof(nodeSettings));

            this.logger = LogManager.GetCurrentClassLogger();
            this.locker = new object();
        }

        public void Initialize()
        {
            var genesisFederation = new List<IFederationMember>(this.network.ConsensusOptions.GenesisFederationMembers);

            this.logger.Info("Genesis federation contains {0} members. Their public keys are: {1}", genesisFederation.Count, $"{Environment.NewLine}{string.Join(Environment.NewLine, genesisFederation)}");

            var federationMembers = this.GetFederationMembers();

            // Display federation.
            this.logger.Info("Current federation contains {0} members. Their public keys are: {1}", federationMembers.Count, Environment.NewLine + string.Join(Environment.NewLine, federationMembers));

            // Set the current federation member's key.
            if (!InitializeFederationMemberKey())
                return;

            // Loaded key has to be a key for current federation.
            if (!federationMembers.Any(x => x.PubKey == this.CurrentFederationKey.PubKey))
            {
                this.logger.Warn("Key provided is not registered on the network.");
            }

            // TODO This will be removed once we remove the distinction between FederationMember and CollateralFederationMember
            if (federationMembers.Any(f => f is CollateralFederationMember))
                CheckCollateralMembers();

            this.logger.Info("Federation key pair was successfully loaded. Your public key is: '{0}'.", this.CurrentFederationKey.PubKey);
        }

        private bool InitializeFederationMemberKey()
        {
            if (this.nodeSettings.DevMode == null)
            {
                // Load key.
                Key key = new KeyTool(this.nodeSettings.DataFolder).LoadPrivateKey();
                if (key == null)
                {
                    this.logger.Warn("No federation key was loaded from 'federationKey.dat'.");
                    return false;
                }

                this.CurrentFederationKey = key;

                if (this.CurrentFederationKey == null)
                {
                    this.logger.Trace("(-)[NOT_FED_MEMBER]");
                    return false;
                }

                return true;
            }
            else
            {
                var keyIndex = this.nodeSettings.ConfigReader.GetOrDefault("fedmemberindex", 0);
                this.CurrentFederationKey = this.network.FederationKeys[keyIndex];
                return true;
            }
        }

        private void CheckCollateralMembers()
        {
            IEnumerable<CollateralFederationMember> collateralFederationMembers = this.GetFederationMembers().Cast<CollateralFederationMember>().Where(x => x.CollateralAmount != null && x.CollateralAmount > 0);

            if (collateralFederationMembers.Any(x => x.CollateralMainchainAddress == null))
            {
                throw new Exception("Federation can't contain members with non-zero collateral requirement but null collateral address.");
            }

            int distinctCount = collateralFederationMembers.Select(x => x.CollateralMainchainAddress).Distinct().Count();

            if (distinctCount != collateralFederationMembers.Count())
            {
                throw new Exception("Federation can't contain members with duplicated collateral addresses.");
            }
        }

        public CollateralFederationMember CollateralAddressOwner(VotingManager votingManager, VoteKey voteKey, string address)
        {
            CollateralFederationMember member = (this.GetFederationMembers().Cast<CollateralFederationMember>().FirstOrDefault(x => x.CollateralMainchainAddress == address));
            if (member != null)
                return member;

            List<Poll> approvedPolls = votingManager.GetApprovedPolls();

            member = approvedPolls
                .Where(x => !x.IsExecuted && x.VotingData.Key == voteKey)
                .Select(x => this.GetMember(x.VotingData))
                .FirstOrDefault(x => x.CollateralMainchainAddress == address);

            if (member != null)
                return member;

            List<Poll> pendingPolls = votingManager.GetPendingPolls();

            member = pendingPolls
                .Where(x => x.VotingData.Key == voteKey)
                .Select(x => this.GetMember(x.VotingData))
                .FirstOrDefault(x => x.CollateralMainchainAddress == address);

            if (member != null)
                return member;

            List<VotingData> scheduledVotes = votingManager.GetScheduledVotes();

            member = scheduledVotes
                .Where(x => x.Key == voteKey)
                .Select(x => this.GetMember(x))
                .FirstOrDefault(x => x.CollateralMainchainAddress == address);

            return member;
        }

        private CollateralFederationMember GetMember(VotingData votingData)
        {
            if (!(this.network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory collateralPoAConsensusFactory))
                return null;

            if (!(collateralPoAConsensusFactory.DeserializeFederationMember(votingData.Data) is CollateralFederationMember collateralFederationMember))
                return null;

            return collateralFederationMember;
        }

        /// <inheritdoc />
        public List<IFederationMember> GetFederationMembers(ChainedHeader chainedHeader = null)
        {
            lock (this.locker)
            {
                if (chainedHeader == null)
                    return this.votingManager.GetLastKnownFederation();

                return this.votingManager.GetModifiedFederation(chainedHeader);
            }
        }

        /// <inheritdoc />
        public IFederationMember GetCurrentFederationMember()
        {
            lock (this.locker)
            {
                return this.GetFederationMembers().SingleOrDefault(x => x.PubKey == this.CurrentFederationKey.PubKey);
            }
        }

        /// <inheritdoc />
        public int? GetMultisigMinersApplicabilityHeight()
        {
            IConsensusManager consensusManager = this.fullNode.NodeService<IConsensusManager>();
            ChainedHeader fork = (this.lastBlockChecked == null) ? null : consensusManager.Tip.FindFork(this.lastBlockChecked);

            if (this.multisigMinersApplicabilityHeight != null && fork?.HashBlock == this.lastBlockChecked?.HashBlock)
                return this.multisigMinersApplicabilityHeight;

            this.lastBlockChecked = fork;
            this.multisigMinersApplicabilityHeight = null;
            var commitmentHeightEncoder = new CollateralHeightCommitmentEncoder();

            ChainedHeader[] headers = consensusManager.Tip.EnumerateToGenesis().TakeWhile(h => h != this.lastBlockChecked && h.Height >= this.network.CollateralCommitmentActivationHeight).Reverse().ToArray();

            ChainedHeader first = BinarySearch.BinaryFindFirst<ChainedHeader>(headers, (chainedHeader) =>
            {
                ChainedHeaderBlock block = consensusManager.GetBlockData(chainedHeader.HashBlock);
                if (block == null)
                    return null;

                // Finding the height of the first STRAX collateral commitment height.
                (int? commitmentHeight, uint? magic) = commitmentHeightEncoder.DecodeCommitmentHeight(block.Block.Transactions.First());
                if (commitmentHeight == null)
                    return null;

                return magic == this.counterChainSettings.CounterChainNetwork.Magic;
            });

            this.lastBlockChecked = headers.LastOrDefault();
            this.multisigMinersApplicabilityHeight = first?.Height;

            return this.multisigMinersApplicabilityHeight;
        }

        public bool IsMultisigMember(PubKey pubKey, ChainedHeader chainedHeader = null)
        {
            return this.GetFederationMembers(chainedHeader).Any(m => m.PubKey == pubKey && m is CollateralFederationMember member && member.IsMultisigMember);
        }
    }
}
