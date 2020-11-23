using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.PoA.Events;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA
{
    public interface IFederationManager
    {
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

    public abstract class FederationManagerBase : IFederationManager
    {
        /// <inheritdoc />
        public bool IsFederationMember { get; private set; }

        /// <inheritdoc />
        public Key CurrentFederationKey { get; private set; }

        protected readonly IKeyValueRepository keyValueRepo;

        protected readonly ILogger logger;

        protected readonly NodeSettings settings;

        protected readonly PoANetwork network;

        private readonly ISignals signals;

        /// <summary>Key for accessing list of public keys that represent federation members from <see cref="IKeyValueRepository"/>.</summary>
        protected const string federationMembersDbKey = "fedmemberskeys";

        /// <summary>Collection of all active federation members as determined by the genesis members and all executed polls.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        protected List<IFederationMember> federationMembers;

        /// <summary>Protects access to <see cref="federationMembers"/>.</summary>
        protected readonly object locker;

        public FederationManagerBase(NodeSettings nodeSettings, Network network, ILoggerFactory loggerFactory, IKeyValueRepository keyValueRepo, ISignals signals)
        {
            this.settings = Guard.NotNull(nodeSettings, nameof(nodeSettings));
            this.network = Guard.NotNull(network as PoANetwork, nameof(network));
            this.keyValueRepo = Guard.NotNull(keyValueRepo, nameof(keyValueRepo));
            this.signals = Guard.NotNull(signals, nameof(signals));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.locker = new object();
        }

        public virtual void Initialize()
        {
            var genesisFederation = new List<IFederationMember>(this.network.ConsensusOptions.GenesisFederationMembers);

            this.logger.LogInformation("Genesis federation contains {0} members. Their public keys are: {1}", genesisFederation.Count, $"{Environment.NewLine}{string.Join(Environment.NewLine, genesisFederation)}");

            // Load federation from the db.
            this.LoadFederation();

            if (this.federationMembers == null)
            {
                this.logger.LogDebug("Federation members are not stored in the db. Loading genesis federation members.");

                this.federationMembers = genesisFederation;

                this.SaveFederation(this.federationMembers);
            }

            // Display federation.
            this.logger.LogInformation("Current federation contains {0} members. Their public keys are: {1}",
                this.federationMembers.Count, Environment.NewLine + string.Join(Environment.NewLine, this.federationMembers));

            // Load key.
            Key key = new KeyTool(this.settings.DataFolder).LoadPrivateKey();
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
                string message = "Key provided is not registered on the network!";

                this.logger.LogWarning(message);
            }

            this.logger.LogInformation("Federation key pair was successfully loaded. Your public key is: '{0}'.", this.CurrentFederationKey.PubKey);
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

                this.SaveFederation(this.federationMembers);
            }
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

        /// <remarks>Should be protected by <see cref="locker"/>.</remarks>
        protected virtual void AddFederationMemberLocked(IFederationMember federationMember)
        {
            if (this.federationMembers.Contains(federationMember))
            {
                this.logger.LogTrace("(-)[ALREADY_EXISTS]");
                return;
            }

            this.federationMembers.Add(federationMember);

            this.SaveFederation(this.federationMembers);
            this.SetIsFederationMember();

            this.logger.LogInformation("Federation member '{0}' was added!", federationMember);
        }

        public void RemoveFederationMember(IFederationMember federationMember)
        {
            lock (this.locker)
            {
                this.federationMembers.Remove(federationMember);

                this.SaveFederation(this.federationMembers);
                this.SetIsFederationMember();

                this.logger.LogInformation("Federation member '{0}' was removed!", federationMember);
            }

            this.signals.Publish(new FedMemberKicked(federationMember));
        }

        protected abstract void SaveFederation(List<IFederationMember> federation);

        /// <summary>Loads saved collection of federation members from the database.</summary>
        protected abstract void LoadFederation();

        /// <inheritdoc />
        public virtual int? GetMultisigMinersApplicabilityHeight()
        {
            return 1;
        }

        public bool IsMultisigMember(PubKey pubKey)
        {
            return this.GetFederationMembers().Any(m => m.PubKey == pubKey && m is CollateralFederationMember member && member.IsMultisigMember);
        }
    }

    public class FederationManager : FederationManagerBase
    {
        private readonly IFullNode fullNode;

        public FederationManager(NodeSettings nodeSettings, Network network, ILoggerFactory loggerFactory, IKeyValueRepository keyValueRepo, ISignals signals, IFullNode fullNode)
            : base(nodeSettings, network, loggerFactory, keyValueRepo, signals)
        {
            this.fullNode = fullNode;
        }

        protected override void LoadFederation()
        {
            VotingManager votingManager = this.fullNode.NodeService<VotingManager>();

            this.federationMembers = votingManager.GetFederationFromExecutedPolls();
        }

        protected override void SaveFederation(List<IFederationMember> federation)
        {
        }
    }
}
