using System;
using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Bitcoin.Features.PoA
{
    public class PoAConsensusOptions : ConsensusOptions
    {
        /// <summary>Public keys and other federation members related information at the start of the chain.</summary>
        /// <remarks>
        /// Do not use this list anywhere except for at the initialization of the chain.
        /// Actual collection of the federation members can be changed with time.
        /// Use <see cref="IFederationManager.GetFederationMembers"/> as a source of
        /// up to date federation keys.
        /// </remarks>
        public List<IFederationMember> GenesisFederationMembers { get; protected set; }

        /// <summary>
        /// The number of elapsed seconds required between mined block.
        /// </summary>
        public uint TargetSpacingSeconds { get; protected set; }

        /// <summary>Adds capability of voting for adding\kicking federation members and other things.</summary>
        public bool VotingEnabled { get; protected set; }

        /// <summary>Makes federation members kick idle members.</summary>
        /// <remarks>Requires voting to be enabled to be set <c>true</c>.</remarks>
        public bool AutoKickIdleMembers { get; set; }

        /// <summary>Time that federation member has to be idle to be kicked by others in case <see cref="AutoKickIdleMembers"/> is enabled.</summary>
        public uint FederationMemberMaxIdleTimeSeconds { get; protected set; }

        /// <summary>
        /// This currently only applies to  Cirrus Main Net.
        /// </summary>
        public uint? FederationMemberActivationTime { get; set; }

        /// <summary>
        /// The height at which a federation members will be resolved via the <see cref="FederationHistory"/> class.
        /// <para>
        /// A poll was incorrectly executed at block 1476880 because the legacy GetFederationMemberForTimestamp incorrectly
        /// derived a federation member for a mined block.
        /// </para>
        /// <para>
        /// After this block height, federation member votes will derived using the <see cref="FederationHistory.GetFederationMemberForBlock(ChainedHeader)"/>
        /// method which resolves the pubkey from the signature directly.
        /// </para>
        /// </summary>
        public int VotingManagerV2ActivationHeight { get; set; }

        /// <summary>
        /// This is the height on the main chain at which the dynamic fees paid to the multsig for interop conversion requests will activate.
        /// </summary>
        public int InterFluxV2MainChainActivationHeight { get; set; }

        /// <summary>
        /// Logic related to release 1.1.0.0 will activate at this height, this includes Poll Expiry and the Join Federation Voting Request consensus rule.
        /// </summary>
        public int Release1100ActivationHeight { get; set; }

        /// <summary>
        /// Polls are expired once the tip reaches a block this far beyond the poll start block.
        /// I.e. if (Math.Max(startblock + PollExpiryBlocks, PollExpiryActivationHeight) <= tip) (See IsPollExpiredAt)
        /// </summary>
        public int PollExpiryBlocks { get; set; }

        /// <summary>Initializes values for networks that use block size rules.</summary>
        /// <param name="maxBlockBaseSize">See <see cref="ConsensusOptions.MaxBlockBaseSize"/>.</param>
        /// <param name="maxStandardVersion">See <see cref="ConsensusOptions.MaxStandardVersion"/>.</param>
        /// <param name="maxStandardTxWeight">See <see cref="ConsensusOptions.MaxStandardTxWeight"/>.</param>
        /// <param name="maxBlockSigopsCost">See <see cref="ConsensusOptions.MaxBlockSigopsCost"/>.</param>
        /// <param name="maxStandardTxSigopsCost">See <see cref="ConsensusOptions.MaxStandardTxSigopsCost"/>.</param>
        /// <param name="genesisFederationMembers">See <see cref="GenesisFederationMembers"/>.</param>
        /// <param name="targetSpacingSeconds">See <see cref="TargetSpacingSeconds"/>.</param>
        /// <param name="votingEnabled">See <see cref="VotingEnabled"/>.</param>
        /// <param name="autoKickIdleMembers">See <see cref="AutoKickIdleMembers"/>.</param>
        /// <param name="federationMemberMaxIdleTimeSeconds">See <see cref="FederationMemberMaxIdleTimeSeconds"/>.</param>
        public PoAConsensusOptions(
            uint maxBlockBaseSize,
            int maxStandardVersion,
            int maxStandardTxWeight,
            int maxBlockSigopsCost,
            int maxStandardTxSigopsCost,
            List<IFederationMember> genesisFederationMembers,
            uint targetSpacingSeconds,
            bool votingEnabled,
            bool autoKickIdleMembers,
            uint federationMemberMaxIdleTimeSeconds = 60 * 60 * 24 * 7)
                : base(maxBlockBaseSize, maxStandardVersion, maxStandardTxWeight, maxBlockSigopsCost, maxStandardTxSigopsCost, witnessScaleFactor: 1)
        {
            this.GenesisFederationMembers = genesisFederationMembers;
            this.TargetSpacingSeconds = targetSpacingSeconds;
            this.VotingEnabled = votingEnabled;
            this.AutoKickIdleMembers = autoKickIdleMembers;
            this.FederationMemberMaxIdleTimeSeconds = federationMemberMaxIdleTimeSeconds;
            this.InterFluxV2MainChainActivationHeight = 0;

            if (this.AutoKickIdleMembers && !this.VotingEnabled)
                throw new ArgumentException("Voting should be enabled for automatic kicking to work.");
        }
    }
}
