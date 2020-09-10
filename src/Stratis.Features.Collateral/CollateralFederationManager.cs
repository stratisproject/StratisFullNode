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
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Features.Wallet.Controllers;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.PoA.Features.Voting;
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

        protected override List<IFederationMember> LoadFederation()
        {
            List<CollateralFederationMemberModel> fedMemberModels = this.keyValueRepo.LoadValueJson<List<CollateralFederationMemberModel>>(federationMembersDbKey);

            if (fedMemberModels == null)
            {
                this.logger.LogTrace("(-)[NOT_FOUND]:null");
                return null;
            }

            var federation = new List<IFederationMember>(fedMemberModels.Count);

            foreach (CollateralFederationMemberModel fedMemberModel in fedMemberModels)
            {
                PubKey pubKey = new PubKey(fedMemberModel.PubKeyHex);
                bool isMultisigMember = FederationVotingController.IsMultisigMember(this.network, pubKey);
                federation.Add(new CollateralFederationMember(pubKey, isMultisigMember, new Money(fedMemberModel.CollateralAmountSatoshis),
                    fedMemberModel.CollateralMainchainAddress));
            }

            return federation;
        }

        protected override void SaveFederation(List<IFederationMember> federation)
        {
            IEnumerable<CollateralFederationMember> collateralFederation = federation.Cast<CollateralFederationMember>();

            var modelsCollection = new List<CollateralFederationMemberModel>(federation.Count);

            foreach (CollateralFederationMember federationMember in collateralFederation)
            {
                modelsCollection.Add(new CollateralFederationMemberModel()
                {
                    PubKeyHex = federationMember.PubKey.ToHex(),
                    CollateralMainchainAddress = federationMember.CollateralMainchainAddress,
                    CollateralAmountSatoshis = federationMember.CollateralAmount.Satoshi
                });
            }

            this.keyValueRepo.SaveValueJson(federationMembersDbKey, modelsCollection);
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
                throw new Exception($"The private key file ({KeyTool.KeyFileDefaultName}) has not been configured.");

            Money collateralAmount = new Money(CollateralPoAMiner.MinerCollateralAmount, MoneyUnit.BTC);

            var joinRequest = new JoinFederationRequest(minerKey.PubKey, collateralAmount, addressKey);

            // Populate the RemovalEventId.
            var collateralFederationMember = new CollateralFederationMember(minerKey.PubKey, false, joinRequest.CollateralAmount, request.CollateralAddress);

            byte[] federationMemberBytes = (this.network.Consensus.ConsensusFactory as CollateralPoAConsensusFactory).SerializeFederationMember(collateralFederationMember);
            var votingManager = this.fullNode.NodeService<VotingManager>();
            Poll poll = votingManager.GetFinishedPolls().FirstOrDefault(x => x.IsExecuted &&
                  x.VotingData.Key == VoteKey.KickFederationMember && x.VotingData.Data.SequenceEqual(federationMemberBytes));

            joinRequest.RemovalEventId = (poll == null) ? Guid.Empty : new Guid(poll.PollExecutedBlockData.ToBytes());

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
                throw new Exception("Operation was cancelled during call to counter-chain to sign the collateral address.");

            joinRequest.AddSignature(signature);

            var walletTransactionHandler = this.fullNode.NodeService<IWalletTransactionHandler>();
            var encoder = new JoinFederationRequestEncoder(this.loggerFactory);
            Transaction trx = JoinFederationRequestBuilder.BuildTransaction(walletTransactionHandler, this.network, joinRequest, encoder, request.WalletName, request.WalletAccount, request.WalletPassword);

            var walletService = this.fullNode.NodeService<IWalletService>();
            await walletService.SendTransaction(new SendTransactionRequest(trx.ToHex()), cancellationToken);

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
    }
}
