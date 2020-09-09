using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
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
        private readonly IWalletService walletService;
        private readonly IWalletTransactionHandler walletTransactionHandler;

        public CollateralFederationManager(NodeSettings nodeSettings, Network network, ILoggerFactory loggerFactory, IKeyValueRepository keyValueRepo, ISignals signals, 
            ICounterChainSettings counterChainSettings, IWalletService walletService, IWalletTransactionHandler walletTransactionHandler)
            : base(nodeSettings, network, loggerFactory, keyValueRepo, signals)
        {
            this.counterChainSettings = counterChainSettings;
            this.loggerFactory = loggerFactory;
            this.walletService = walletService;
            this.walletTransactionHandler = walletTransactionHandler;
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

        public void JoinFederation(string collateralAddress, string collateralWalletName, string collateralWalletPassword, string walletName, string walletAccount, string walletPassword, CancellationToken cancellationToken)
        {
            // Get the address pub key hash.
            var address = BitcoinAddress.Create(collateralAddress, this.counterChainSettings.CounterChainNetwork);
            KeyId addressKey = PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(address.ScriptPubKey);

            // Get mining key.
            var keyTool = new KeyTool(this.settings.DataFolder);
            Key minerKey = keyTool.LoadPrivateKey();
            if (minerKey == null)
            {
                minerKey = keyTool.GeneratePrivateKey();
                keyTool.SavePrivateKey(minerKey);
            }

            var request = new JoinFederationRequest(minerKey.PubKey, new Money(10_000m, MoneyUnit.BTC), addressKey);

            // In practice this signature will come from calling the counter-chain "signmessage" API.
            request.AddSignature(collateralKey.SignMessage(request.SignatureMessage));

            var encoder = new JoinFederationRequestEncoder(this.loggerFactory);
            // TODO: Rely on wallet being unlocked?
            Transaction trx = JoinFederationRequestBuilder.BuildTransaction(this.walletTransactionHandler, this.network, request, encoder, walletName, walletAccount, walletPassword);

            this.walletService.SendTransaction(new SendTransactionRequest(trx.ToHex()), cancellationToken);
        }
    }
}
