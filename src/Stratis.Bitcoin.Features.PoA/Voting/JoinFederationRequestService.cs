using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Features.Wallet.Controllers;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.PoA.Features.Voting;
using Stratis.Features.PoA.Collateral.CounterChain;

namespace Stratis.Features.PoA.Voting
{
    public interface IJoinFederationRequestService
    {
        JoinFederationRequest BuildJoinFederationRequest(string collateralAddress);
        Task<PubKey> BroadcastSignedJoinRequestAsync(JoinFederationRequest request, string walletName, string walletPassword, string walletAccount, CancellationToken cancellationToken);
        Task<PubKey> JoinFederationAsync(JoinFederationRequestModel request, CancellationToken cancellationToken);
    }

    public sealed class JoinFederationRequestService : IJoinFederationRequestService
    {
        private readonly ICounterChainSettings counterChainSettings;
        private readonly IFullNode fullNode;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILoggerFactory loggerFactory;
        private readonly PoANetwork network;
        private readonly NodeSettings nodeSettings;
        private readonly VotingManager votingManager;

        public JoinFederationRequestService(ICounterChainSettings counterChainSettings, IFullNode fullNode, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, Network network, NodeSettings nodeSettings, VotingManager votingManager)
        {
            this.counterChainSettings = counterChainSettings;
            this.fullNode = fullNode;
            this.httpClientFactory = httpClientFactory;
            this.loggerFactory = loggerFactory;
            this.network = network as PoANetwork;
            this.nodeSettings = nodeSettings;
            this.votingManager = votingManager;
        }

        public async Task<PubKey> JoinFederationAsync(JoinFederationRequestModel request, CancellationToken cancellationToken)
        {
            // Get the address pub key hash.
            JoinFederationRequest joinRequest = BuildJoinFederationRequest(request.CollateralAddress);

            // Get the signature by calling the counter-chain "signmessage" API.
            var signMessageRequest = new SignMessageRequest()
            {
                Message = joinRequest.SignatureMessage,
                WalletName = request.CollateralWalletName,
                Password = request.CollateralWalletPassword,
                ExternalAddress = request.CollateralAddress
            };

            var walletClient = new WalletClient(this.loggerFactory, this.httpClientFactory, $"http://{this.counterChainSettings.CounterChainApiHost}", this.counterChainSettings.CounterChainApiPort);

            try
            {
                string signature = await walletClient.SignMessageAsync(signMessageRequest, cancellationToken);
                joinRequest.AddSignature(signature);
            }
            catch (Exception err)
            {
                throw new Exception($"The call to sign the join federation request failed: '{err.Message}'.");
            }

            if (!VerifyCollateralSignature(joinRequest))
            {
                throw new Exception($"The signature from the collateral address {joinRequest.CollateralMainchainAddress} is invalid");
            }

            return await BroadcastSignedJoinRequestAsync(joinRequest, request.WalletName, request.WalletPassword, request.WalletAccount, cancellationToken);
        }

        public async Task<PubKey> BroadcastSignedJoinRequestAsync(JoinFederationRequest request, string walletName, string walletPassword, string walletAccount, CancellationToken cancellationToken)
        {
            if(!VerifyCollateralSignature(request))
            {
                throw new Exception($"The signature from the collateral address {request.CollateralMainchainAddress} is invalid");
            }

            IWalletTransactionHandler walletTransactionHandler = this.fullNode.NodeService<IWalletTransactionHandler>();
            var encoder = new JoinFederationRequestEncoder();
            JoinFederationRequestResult result = JoinFederationRequestBuilder.BuildTransaction(walletTransactionHandler, this.network, request, encoder, walletName, walletAccount, walletPassword);
            if (result.Transaction == null)
                throw new Exception(result.Errors);

            IWalletService walletService = this.fullNode.NodeService<IWalletService>();
            await walletService.SendTransaction(new SendTransactionRequest(result.Transaction.ToHex()), cancellationToken);

            return GetMinerKey().PubKey;
        }

        public JoinFederationRequest BuildJoinFederationRequest(string collateralAddress)
        {
            Key minerKey = GetMinerKey();

            var expectedCollateralAmount = CollateralFederationMember.GetCollateralAmountForPubKey(this.network, minerKey.PubKey);

            var collateralAmount = new Money(expectedCollateralAmount, MoneyUnit.BTC);

            BitcoinAddress address = BitcoinAddress.Create(collateralAddress, this.counterChainSettings.CounterChainNetwork);
            KeyId addressKey = PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(address.ScriptPubKey);

            var joinRequest = new JoinFederationRequest(minerKey.PubKey, collateralAmount, addressKey);

            // Populate the RemovalEventId.
            var collateralFederationMember = new CollateralFederationMember(minerKey.PubKey, false, joinRequest.CollateralAmount, collateralAddress);

            byte[] federationMemberBytes = (this.network.Consensus.ConsensusFactory as CollateralPoAConsensusFactory).SerializeFederationMember(collateralFederationMember);
            Poll poll = this.votingManager.GetApprovedPolls().FirstOrDefault(x => x.IsExecuted &&
                  x.VotingData.Key == VoteKey.KickFederationMember && x.VotingData.Data.SequenceEqual(federationMemberBytes));

            joinRequest.RemovalEventId = (poll == null) ? Guid.Empty : new Guid(poll.PollExecutedBlockData.Hash.ToBytes().TakeLast(16).ToArray());
            return joinRequest;
        }

        private Key GetMinerKey()
        {
            // Get mining key.
            var keyTool = new KeyTool(this.nodeSettings.DataFolder);
            Key minerKey = keyTool.LoadPrivateKey();
            if (minerKey == null)
                throw new Exception($"The private key file ({KeyTool.KeyFileDefaultName}) has not been configured or is not present.");
            return minerKey;
        }

        /// <summary>
        /// Returns true if the join request message is signed by the given collateral address.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public bool VerifyCollateralSignature(JoinFederationRequest request)
        {
            BitcoinPubKeyAddress bitcoinPubKeyAddress = new BitcoinPubKeyAddress(request.CollateralMainchainAddress, this.counterChainSettings.CounterChainNetwork);
            return bitcoinPubKeyAddress.VerifyMessage(request.SignatureMessage, request.Signature);
        }
    }
}
