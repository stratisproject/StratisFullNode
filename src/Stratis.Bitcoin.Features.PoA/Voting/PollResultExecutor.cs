using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.PoA.Events;
using Stratis.Bitcoin.Signals;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public interface IPollResultExecutor
    {
        /// <summary>Applies effect of <see cref="VotingData"/>.</summary>
        void ApplyChange(VotingData data, List<IFederationMember> modifiedFederation);

        /// <summary>Reverts effect of <see cref="VotingData"/>.</summary>
        void RevertChange(VotingData data, List<IFederationMember> modifiedFederation);

        /// <summary>Converts <see cref="VotingData"/> to a human readable format.</summary>
        string ConvertToString(VotingData data);
    }

    public class PollResultExecutor : IPollResultExecutor
    {
        private readonly IWhitelistedHashesRepository whitelistedHashesRepository;

        private readonly ISignals signals;

        private readonly PoAConsensusFactory consensusFactory;

        private readonly ILogger logger;

        public PollResultExecutor(ILoggerFactory loggerFactory, IWhitelistedHashesRepository whitelistedHashesRepository, Network network, ISignals signals)
        {
            this.whitelistedHashesRepository = whitelistedHashesRepository;
            this.consensusFactory = network.Consensus.ConsensusFactory as PoAConsensusFactory;
            this.signals = signals;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public void ApplyChange(VotingData data, List<IFederationMember> modifiedFederation)
        {
            switch (data.Key)
            {
                case VoteKey.AddFederationMember:
                    this.AddFederationMember(data.Data, modifiedFederation);
                    break;

                case VoteKey.KickFederationMember:
                    this.RemoveFederationMember(data.Data, modifiedFederation);
                    break;

                case VoteKey.WhitelistHash:
                    this.AddHash(data.Data);
                    break;

                case VoteKey.RemoveHash:
                    this.RemoveHash(data.Data);
                    break;
            }
        }

        /// <inheritdoc />
        public void RevertChange(VotingData data, List<IFederationMember> modifiedFederation)
        {
            switch (data.Key)
            {
                case VoteKey.AddFederationMember:
                    this.RemoveFederationMember(data.Data, modifiedFederation);
                    break;

                case VoteKey.KickFederationMember:
                    this.AddFederationMember(data.Data, modifiedFederation);
                    break;

                case VoteKey.WhitelistHash:
                    this.RemoveHash(data.Data);
                    break;

                case VoteKey.RemoveHash:
                    this.AddHash(data.Data);
                    break;
            }
        }

        /// <inheritdoc />
        public string ConvertToString(VotingData data)
        {
            string action = $"Action:'{data.Key}'";

            switch (data.Key)
            {
                case VoteKey.AddFederationMember:
                case VoteKey.KickFederationMember:
                    IFederationMember federationMember = this.consensusFactory.DeserializeFederationMember(data.Data);
                    return $"{action},FederationMember:'{federationMember}'";

                case VoteKey.WhitelistHash:
                case VoteKey.RemoveHash:
                    var hash = new uint256(data.Data);
                    return $"{action},Hash:'{hash}'";
            }

            return "unknown (not supported voting data key)";
        }

        /// <summary>Should be protected by <see cref="locker"/>.</summary>
        private void AddFederationMember(byte[] federationMemberBytes, List<IFederationMember> modifiedFederation)
        {
            IFederationMember federationMember = this.consensusFactory.DeserializeFederationMember(federationMemberBytes);

            if (modifiedFederation.Contains(federationMember))
            {
                this.logger.LogDebug("(-)[FEDERATION_MEMBER_ALREADY_EXISTS]");
                return;
            }

            if (federationMember is CollateralFederationMember collateralFederationMember)
            {
                if (modifiedFederation.Cast<CollateralFederationMember>().Any(x => x.CollateralMainchainAddress == collateralFederationMember.CollateralMainchainAddress))
                {
                    this.logger.LogDebug("(-)[DUPLICATED_COLLATERAL_ADDR]");
                    return;
                }

                if (modifiedFederation.Contains(federationMember))
                {
                    this.logger.LogDebug("(-)[ALREADY_EXISTS]");
                    return;
                }
            }

            modifiedFederation.Add(federationMember);

            this.logger.LogDebug("Federation member '{0}' was added.", federationMember);

            this.signals.Publish(new FedMemberAdded(federationMember));
        }

        public void RemoveFederationMember(byte[] federationMemberBytes, List<IFederationMember> modifiedFederation)
        {
            IFederationMember federationMember = this.consensusFactory.DeserializeFederationMember(federationMemberBytes);

            modifiedFederation.Remove(federationMember);

            this.logger.LogDebug("Federation member '{0}' was removed.", federationMember);

            this.signals.Publish(new FedMemberKicked(federationMember));
        }

        private void AddHash(byte[] hashBytes)
        {
            try
            {
                var hash = new uint256(hashBytes);

                this.whitelistedHashesRepository.AddHash(hash);
            }
            catch (FormatException e)
            {
                this.logger.LogWarning("Hash had incorrect format: '{0}'.", e.ToString());
            }
        }

        private void RemoveHash(byte[] hashBytes)
        {
            try
            {
                var hash = new uint256(hashBytes);

                this.whitelistedHashesRepository.RemoveHash(hash);
            }
            catch (FormatException e)
            {
                this.logger.LogWarning("Hash had incorrect format: '{0}'.", e.ToString());
            }
        }
    }
}
