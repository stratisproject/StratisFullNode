using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Features.Wallet.Controllers;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.PoA.Features.Voting;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral.CounterChain;

namespace Stratis.Features.Collateral
{
    public class CollateralFederationManager : FederationManagerBase
    {
        private readonly ICounterChainSettings counterChainSettings;
        private readonly ILoggerFactory loggerFactory;
        private readonly IFullNode fullNode;
        private readonly IHttpClientFactory httpClientFactory;
        private int? multisigMinersApplicabilityHeight;
        private ChainedHeader lastBlockChecked;

        public CollateralFederationManager(NodeSettings nodeSettings, Network network, ILoggerFactory loggerFactory, IKeyValueRepository keyValueRepo, ISignals signals,
            ICounterChainSettings counterChainSettings, IFullNode fullNode, IHttpClientFactory httpClientFactory)
            : base(nodeSettings, network, loggerFactory, keyValueRepo, signals)
        {
            this.counterChainSettings = counterChainSettings;
            this.loggerFactory = loggerFactory;
            this.fullNode = fullNode;
            this.httpClientFactory = httpClientFactory;
        }

        public override void Initialize()
        {
            base.Initialize();

            IEnumerable<CollateralFederationMember> collateralMembers = this.federationMembers.Cast<CollateralFederationMember>().Where(x => x.CollateralAmount != null && x.CollateralAmount > 0);

            if (collateralMembers.Any(x => x.CollateralMainchainAddress == null))
            {
                throw new Exception("Federation can't contain members with non-zero collateral requirement but null collateral address.");
            }

            int distinctCount = collateralMembers.Select(x => x.CollateralMainchainAddress).Distinct().Count();

            if (distinctCount != collateralMembers.Count())
            {
                throw new Exception("Federation can't contain members with duplicated collateral addresses.");
            }
        }

        protected override void AddFederationMemberLocked(IFederationMember federationMember)
        {
            var collateralMember = federationMember as CollateralFederationMember;

            if (this.federationMembers.Cast<CollateralFederationMember>().Any(x => x.CollateralMainchainAddress == collateralMember.CollateralMainchainAddress))
            {
                this.logger.LogTrace("(-)[DUPLICATED_COLLATERAL_ADDR]");
                return;
            }

            base.AddFederationMemberLocked(federationMember);
        }

        protected override void LoadFederation()
        {
            VotingManager votingManager = this.fullNode.NodeService<VotingManager>();

            this.federationMembers = votingManager.GetFederationFromExecutedPolls();

            this.UpdateMultisigMiners(this.multisigMinersApplicabilityHeight != null);
        }

        public async Task<PubKey> JoinFederationAsync(JoinFederationRequestModel request, CancellationToken cancellationToken)
        {
            // Get the address pub key hash.
            var address = BitcoinAddress.Create(request.CollateralAddress, this.counterChainSettings.CounterChainNetwork);
            KeyId addressKey = PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(address.ScriptPubKey);

            // Get mining key.
            var keyTool = new KeyTool(this.settings.DataFolder);
            Key minerKey = keyTool.LoadPrivateKey();
            if (minerKey == null)
                throw new Exception($"The private key file ({KeyTool.KeyFileDefaultName}) has not been configured or is not present.");

            var expectedCollateralAmount = CollateralFederationMember.GetCollateralAmountForPubKey(this.network, minerKey.PubKey);

            var collateralAmount = new Money(expectedCollateralAmount, MoneyUnit.BTC);

            var joinRequest = new JoinFederationRequest(minerKey.PubKey, collateralAmount, addressKey);

            // Populate the RemovalEventId.
            var collateralFederationMember = new CollateralFederationMember(minerKey.PubKey, false, joinRequest.CollateralAmount, request.CollateralAddress);

            byte[] federationMemberBytes = (this.network.Consensus.ConsensusFactory as CollateralPoAConsensusFactory).SerializeFederationMember(collateralFederationMember);
            VotingManager votingManager = this.fullNode.NodeService<VotingManager>();
            Poll poll = votingManager.GetFinishedPolls().FirstOrDefault(x => x.IsExecuted &&
                  x.VotingData.Key == VoteKey.KickFederationMember && x.VotingData.Data.SequenceEqual(federationMemberBytes));

            joinRequest.RemovalEventId = (poll == null) ? Guid.Empty : new Guid(poll.PollExecutedBlockData.Hash.ToBytes().TakeLast(16).ToArray());

            // Get the signature by calling the counter-chain "signmessage" API.
            var signMessageRequest = new SignMessageRequest()
            {
                Message = joinRequest.SignatureMessage,
                WalletName = request.CollateralWalletName,
                Password = request.CollateralWalletPassword,
                ExternalAddress = request.CollateralAddress
            };

            var walletClient = new WalletClient(this.loggerFactory, this.httpClientFactory, $"http://{this.counterChainSettings.CounterChainApiHost}", this.counterChainSettings.CounterChainApiPort);
            string signature = await walletClient.SignMessageAsync(signMessageRequest, cancellationToken);
            if (signature == null)
                throw new Exception("The call to sign the join federation request failed. It could have timed-out or the counter chain node is offline.");

            joinRequest.AddSignature(signature);

            IWalletTransactionHandler walletTransactionHandler = this.fullNode.NodeService<IWalletTransactionHandler>();
            var encoder = new JoinFederationRequestEncoder(this.loggerFactory);
            JoinFederationRequestResult result = JoinFederationRequestBuilder.BuildTransaction(walletTransactionHandler, this.network, joinRequest, encoder, request.WalletName, request.WalletAccount, request.WalletPassword);
            if (result.Transaction == null)
                throw new Exception(result.Errors);

            IWalletService walletService = this.fullNode.NodeService<IWalletService>();
            await walletService.SendTransaction(new SendTransactionRequest(result.Transaction.ToHex()), cancellationToken);

            return minerKey.PubKey;
        }

        private CollateralFederationMember GetMember(VotingData votingData)
        {
            if (!(this.network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory collateralPoAConsensusFactory))
                return null;

            if (!(collateralPoAConsensusFactory.DeserializeFederationMember(votingData.Data) is CollateralFederationMember collateralFederationMember))
                return null;

            return collateralFederationMember;
        }

        public CollateralFederationMember CollateralAddressOwner(VotingManager votingManager, VoteKey voteKey, string address)
        {
            CollateralFederationMember member = (this.federationMembers.Cast<CollateralFederationMember>().FirstOrDefault(x => x.CollateralMainchainAddress == address));
            if (member != null)
                return member;

            List<Poll> finishedPolls = votingManager.GetFinishedPolls();

            member = finishedPolls
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

        /// <inheritdoc />
        public override int? GetMultisigMinersApplicabilityHeight()
        {
            IConsensusManager consensusManager = this.fullNode.NodeService<IConsensusManager>();
            ChainedHeader fork = (this.lastBlockChecked == null) ? null : consensusManager.Tip.FindFork(this.lastBlockChecked);

            if (this.multisigMinersApplicabilityHeight != null && fork?.HashBlock == this.lastBlockChecked?.HashBlock)
                return this.multisigMinersApplicabilityHeight;

            this.lastBlockChecked = fork;
            this.multisigMinersApplicabilityHeight = null;
            var commitmentHeightEncoder = new CollateralHeightCommitmentEncoder(this.logger);

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
    }
}
