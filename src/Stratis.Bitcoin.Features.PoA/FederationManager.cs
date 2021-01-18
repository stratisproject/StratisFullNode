﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.PoA.Events;
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

        /// <summary>This method updates the <see cref="CollateralFederationMember.IsMultisigMember"/> flags from 
        /// <see cref="PoANetwork.StraxMiningMultisigMembers"/> or <see cref="PoAConsensusOptions.GenesisFederationMembers"/>
        /// depending on whether the Cirrus chain has reached the STRAX-era blocks.</summary>
        /// <param name="straxEra">This is set to <c>true</c> for the Strax-era flag values and <c>false</c> for the Stratis era.</param>
        void UpdateMultisigMiners(bool straxEra);

        void Initialize();

        /// <summary>Provides up to date list of federation members.</summary>
        /// <remarks>
        /// Blocks that are not signed with private keys that correspond
        /// to public keys from this list are considered to be invalid.
        /// </remarks>
        List<IFederationMember> GetFederationMembers();

        bool IsMultisigMember(PubKey pubKey);

        void AddFederationMember(IFederationMember federationMember);

        void RemoveFederationMember(IFederationMember federationMember);

        /// <summary>Gets the height at which the Strax-era started.</summary>
        /// <remarks>This is the height for which <see cref="PoANetwork.StraxMiningMultisigMembers"/> is applicable.</remarks>
        int? GetMultisigMinersApplicabilityHeight();

        /// <summary>Provides federation member of this node or <c>null</c> if <see cref="IsFederationMember"/> is <c>false</c>.</summary>
        IFederationMember GetCurrentFederationMember();
    }

    public sealed class FederationManager : IFederationManager
    {
        /// <inheritdoc />
        public bool IsFederationMember { get; private set; }

        /// <inheritdoc />
        public Key CurrentFederationKey { get; private set; }

        private readonly ICounterChainSettings counterChainSettings;

        /// <summary>Collection of all active federation members as determined by the genesis members and all executed polls.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private List<IFederationMember> federationMembers;

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
            ICounterChainSettings counterChainSettings,
            IFullNode fullNode,
            Network network,
            NodeSettings nodeSettings,
            ILoggerFactory loggerFactory,
            ISignals signals)
        {
            this.counterChainSettings = counterChainSettings;
            this.fullNode = fullNode;
            this.network = Guard.NotNull(network as PoANetwork, nameof(network));
            this.nodeSettings = Guard.NotNull(nodeSettings, nameof(nodeSettings));
            this.signals = Guard.NotNull(signals, nameof(signals));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.locker = new object();
        }

        public void Initialize()
        {
            var genesisFederation = new List<IFederationMember>(this.network.ConsensusOptions.GenesisFederationMembers);

            this.logger.LogInformation("Genesis federation contains {0} members. Their public keys are: {1}", genesisFederation.Count, $"{Environment.NewLine}{string.Join(Environment.NewLine, genesisFederation)}");

            // Load federation from the db.
            this.LoadFederation();

            if (this.federationMembers == null)
            {
                this.logger.LogDebug("Federation members are not stored in the db. Loading genesis federation members.");
                this.federationMembers = genesisFederation;
            }

            // Display federation.
            this.logger.LogInformation("Current federation contains {0} members. Their public keys are: {1}",
                this.federationMembers.Count, Environment.NewLine + string.Join(Environment.NewLine, this.federationMembers));

            // Load key.
            Key key = new KeyTool(this.nodeSettings.DataFolder).LoadPrivateKey();
            if (key == null)
            {
                this.logger.LogWarning("No federation key was loaded from 'federationKey.dat'.");
                return;
            }

            this.CurrentFederationKey = key;
            this.SetIsFederationMember();

            if (this.CurrentFederationKey == null)
            {
                this.logger.LogTrace("(-)[NOT_FED_MEMBER]");
                return;
            }

            // Loaded key has to be a key for current federation.
            if (!this.federationMembers.Any(x => x.PubKey == this.CurrentFederationKey.PubKey))
            {
                this.logger.LogWarning("Key provided is not registered on the network.");
            }

            // TODO This will be removed once we remove the distinction between FederationMember and CollateralFederationMember
            CheckCollateralMembers();

            this.logger.LogInformation("Federation key pair was successfully loaded. Your public key is: '{0}'.", this.CurrentFederationKey.PubKey);
        }

        private void CheckCollateralMembers()
        {
            if (!this.federationMembers.Any(f => f is CollateralFederationMember))
                return;

            IEnumerable<CollateralFederationMember> collateralFederationMembers = this.federationMembers.Cast<CollateralFederationMember>().Where(x => x.CollateralAmount != null && x.CollateralAmount > 0);

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
            CollateralFederationMember member = (this.federationMembers.Cast<CollateralFederationMember>().FirstOrDefault(x => x.CollateralMainchainAddress == address));
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

        public void UpdateMultisigMiners(bool straxEra)
        {
            if (this.network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory)
            {
                // Update member types by using the multisig mining keys supplied on the command-line. Don't add/remove members.
                foreach (CollateralFederationMember federationMember in this.federationMembers)
                {
                    if (straxEra)
                    {
                        federationMember.IsMultisigMember = this.network.StraxMiningMultisigMembers.Contains(federationMember.PubKey);
                    }
                    else
                    {
                        federationMember.IsMultisigMember = ((PoAConsensusOptions)this.network.Consensus.Options).GenesisFederationMembers
                            .Any(m => m.PubKey == federationMember.PubKey && ((CollateralFederationMember)m).IsMultisigMember);
                    }
                }
            }
        }

        private CollateralFederationMember GetMember(VotingData votingData)
        {
            if (!(this.network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory collateralPoAConsensusFactory))
                return null;

            if (!(collateralPoAConsensusFactory.DeserializeFederationMember(votingData.Data) is CollateralFederationMember collateralFederationMember))
                return null;

            return collateralFederationMember;
        }

        private void SetIsFederationMember()
        {
            this.IsFederationMember = this.federationMembers.Any(x => x.PubKey == this.CurrentFederationKey?.PubKey);
        }

        /// <inheritdoc />
        public List<IFederationMember> GetFederationMembers()
        {
            lock (this.locker)
            {
                return new List<IFederationMember>(this.federationMembers);
            }
        }

        /// <inheritdoc />
        public IFederationMember GetCurrentFederationMember()
        {
            lock (this.locker)
            {
                return this.federationMembers.SingleOrDefault(x => x.PubKey == this.CurrentFederationKey.PubKey);
            }
        }

        public void AddFederationMember(IFederationMember federationMember)
        {
            lock (this.locker)
            {
                this.AddFederationMemberLocked(federationMember);
            }

            this.signals.Publish(new FedMemberAdded(federationMember));
        }

        /// <summary>Should be protected by <see cref="locker"/>.</summary>
        private void AddFederationMemberLocked(IFederationMember federationMember)
        {
            if (federationMember is CollateralFederationMember collateralFederationMember)
            {
                if (this.federationMembers.Cast<CollateralFederationMember>().Any(x => x.CollateralMainchainAddress == collateralFederationMember.CollateralMainchainAddress))
                {
                    this.logger.LogTrace("(-)[DUPLICATED_COLLATERAL_ADDR]");
                    return;
                }

                if (this.federationMembers.Contains(federationMember))
                {
                    this.logger.LogTrace("(-)[ALREADY_EXISTS]");
                    return;
                }
            }

            this.federationMembers.Add(federationMember);

            this.SetIsFederationMember();

            this.logger.LogInformation("Federation member '{0}' was added.", federationMember);
        }

        public void RemoveFederationMember(IFederationMember federationMember)
        {
            lock (this.locker)
            {
                this.federationMembers.Remove(federationMember);

                this.SetIsFederationMember();

                this.logger.LogInformation("Federation member '{0}' was removed.", federationMember);
            }

            this.signals.Publish(new FedMemberKicked(federationMember));
        }

        /// <summary>Loads saved collection of federation members from the database.</summary>
        private void LoadFederation()
        {
            VotingManager votingManager = this.fullNode.NodeService<VotingManager>();
            this.federationMembers = votingManager.GetFederationFromExecutedPolls();
            this.UpdateMultisigMiners(this.multisigMinersApplicabilityHeight != null);
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

            this.UpdateMultisigMiners(first != null);

            return this.multisigMinersApplicabilityHeight;
        }

        public bool IsMultisigMember(PubKey pubKey)
        {
            return this.GetFederationMembers().Any(m => m.PubKey == pubKey && m is CollateralFederationMember member && member.IsMultisigMember);
        }
    }
}
