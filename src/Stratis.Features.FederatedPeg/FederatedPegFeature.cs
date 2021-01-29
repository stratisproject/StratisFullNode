using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NLog;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.Notifications;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.Interop;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol.Payloads;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.FederatedPeg.Controllers;
using Stratis.Features.FederatedPeg.Distribution;
using Stratis.Features.FederatedPeg.InputConsolidation;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Notifications;
using Stratis.Features.FederatedPeg.Payloads;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Features.FederatedPeg.TargetChain;
using Stratis.Features.FederatedPeg.Wallet;
using TracerAttributes;

//todo: this is pre-refactoring code
//todo: ensure no duplicate or fake withdrawal or deposit transactions are possible (current work underway)

namespace Stratis.Features.FederatedPeg
{
    internal class FederatedPegFeature : FullNodeFeature
    {
        public const string FederationGatewayFeatureNamespace = "federationgateway";

        private readonly IConnectionManager connectionManager;

        private readonly IFederatedPegSettings federatedPegSettings;

        private readonly IFullNode fullNode;

        private readonly IFederationWalletManager federationWalletManager;

        private readonly IFederationWalletSyncManager walletSyncManager;

        private readonly Network network;

        private readonly ICrossChainTransferStore crossChainTransferStore;

        private readonly IPartialTransactionRequester partialTransactionRequester;

        private readonly MempoolCleaner mempoolCleaner;

        private readonly ISignedMultisigTransactionBroadcaster signedBroadcaster;

        private readonly IMaturedBlocksSyncManager maturedBlocksSyncManager;

        private readonly IInputConsolidator inputConsolidator;

        private readonly ILogger logger;

        public FederatedPegFeature(
            IConnectionManager connectionManager,
            IFederatedPegSettings federatedPegSettings,
            IFullNode fullNode,
            IFederationWalletManager federationWalletManager,
            IFederationWalletSyncManager walletSyncManager,
            Network network,
            INodeStats nodeStats,
            ICrossChainTransferStore crossChainTransferStore,
            IPartialTransactionRequester partialTransactionRequester,
            MempoolCleaner mempoolCleaner,
            ISignedMultisigTransactionBroadcaster signedBroadcaster,
            IMaturedBlocksSyncManager maturedBlocksSyncManager,
            IInputConsolidator inputConsolidator,
            ICollateralChecker collateralChecker = null)
        {
            this.connectionManager = connectionManager;
            this.federatedPegSettings = federatedPegSettings;
            this.fullNode = fullNode;
            this.federationWalletManager = federationWalletManager;
            this.walletSyncManager = walletSyncManager;
            this.network = network;
            this.crossChainTransferStore = crossChainTransferStore;
            this.partialTransactionRequester = partialTransactionRequester;
            this.mempoolCleaner = mempoolCleaner;
            this.maturedBlocksSyncManager = maturedBlocksSyncManager;
            this.signedBroadcaster = signedBroadcaster;
            this.inputConsolidator = inputConsolidator;

            this.logger = LogManager.GetCurrentClassLogger();

            // add our payload
            var payloadProvider = (PayloadProvider)this.fullNode.Services.ServiceProvider.GetService(typeof(PayloadProvider));
            payloadProvider.AddPayload(typeof(RequestPartialTransactionPayload));

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
        }

        public override async Task InitializeAsync()
        {
            // Set up our database of deposit and withdrawal transactions. Needs to happen before everything else.
            this.crossChainTransferStore.Initialize();

            // Load the federation wallet that will be used to generate transactions.
            this.federationWalletManager.Start();

            // Query the other chain every N seconds for deposits. Triggers signing process if deposits are found.
            await this.maturedBlocksSyncManager.StartAsync();

            // Syncs the wallet correctly when restarting the node. i.e. deals with reorgs.
            this.walletSyncManager.Initialize();

            // Synchronises the wallet and the transfer store.
            this.crossChainTransferStore.Start();

            // Query our database for partially-signed transactions and send them around to be signed every N seconds.
            this.partialTransactionRequester.Start();

            // Cleans the mempool periodically.
            this.mempoolCleaner.Start();

            // Query our database for fully-signed transactions and broadcast them every N seconds.
            this.signedBroadcaster.Start();

            // Check that the feature is configured correctly before going any further.
            this.CheckConfiguration();

            // Connect the node to the other federation members.
            foreach (IPEndPoint federationMemberIp in this.federatedPegSettings.FederationNodeIpEndPoints)
                this.connectionManager.AddNodeAddress(federationMemberIp, true);

            // Respond to requests to sign transactions from other nodes.
            NetworkPeerConnectionParameters networkPeerConnectionParameters = this.connectionManager.Parameters;
            networkPeerConnectionParameters.TemplateBehaviors.Add(new PartialTransactionsBehavior(this.federationWalletManager, this.network,
                this.federatedPegSettings, this.crossChainTransferStore, this.inputConsolidator));
        }

        /// <summary>
        /// Checks that the redeem script and the multisig address in the multisig wallet match the values provided in the federated peg settings.
        /// </summary>
        private void CheckConfiguration()
        {
            FederationWallet wallet = this.federationWalletManager.GetWallet();

            if (wallet.MultiSigAddress.RedeemScript != this.federatedPegSettings.MultiSigRedeemScript)
            {
                throw new ConfigurationException("Wallet redeem script does not match redeem script provided in settings. Please check that your wallet JSON file is correct and that the settings are configured correctly.");
            }

            if (wallet.MultiSigAddress.Address != this.federatedPegSettings.MultiSigAddress.ToString())
            {
                throw new ConfigurationException(
                    "Wallet multisig address does not match multisig address of the redeem script provided in settings.  Please check that your wallet JSON file is correct and that the settings are configured correctly.");
            }
        }

        public override void Dispose()
        {
            // Sync manager has to be disposed BEFORE cross chain transfer store.
            this.maturedBlocksSyncManager.Dispose();

            this.crossChainTransferStore.Dispose();
        }

        [NoTrace]
        private void AddComponentStats(StringBuilder benchLog)
        {
            try
            {
                string stats = this.CollectStats();
                benchLog.Append(stats);
            }
            catch (Exception e)
            {
                this.logger.Error(e.ToString());
            }
        }

        [NoTrace]
        private string CollectStats()
        {
            StringBuilder benchLog = new StringBuilder();

            List<ConsolidationTransaction> consolidationPartials = this.inputConsolidator.ConsolidationTransactions;

            if (consolidationPartials != null)
            {
                benchLog.AppendLine("--- Consolidation Transactions in Memory ---");

                foreach (ConsolidationTransaction partial in consolidationPartials.Take(20))
                {
                    benchLog.AppendLine(
                        string.Format("Tran#={0} TotalOut={1,12} Status={2} Signatures=({3}/{4})",
                            partial.PartialTransaction.ToString().Substring(0, 6),
                            partial.PartialTransaction.TotalOut,
                            partial.Status,
                            partial.PartialTransaction.GetSignatureCount(this.network),
                            this.federatedPegSettings.MultiSigM
                        )
                    );
                }

                if (consolidationPartials.Count > 20)
                    benchLog.AppendLine($"and {consolidationPartials.Count - 20} more...");

                benchLog.AppendLine();
            }

            IMaturedBlocksProvider maturedBlocksProvider = this.fullNode.NodeService<IMaturedBlocksProvider>();
            (int blocksBeforeMature, IDeposit deposit)[] maturingDeposits = maturedBlocksProvider.GetMaturingDeposits(21);
            if (maturingDeposits.Length > 0)
            {
                benchLog.AppendLine("--- Maturing Deposits ---");

                benchLog.AppendLine(string.Join(Environment.NewLine, maturingDeposits.Select(d =>
                {
                    var target = d.deposit.TargetAddress;
                    if (target == this.network.CirrusRewardDummyAddress)
                        target = "Reward Distribution";
                    return $"{d.deposit.Amount} ({d.blocksBeforeMature}) => {target} ({d.deposit.RetrievalType})";
                }).Take(10)));

                if (maturingDeposits.Length > 10)
                    benchLog.AppendLine("...");

                benchLog.AppendLine();
            }

            return benchLog.ToString();
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderSidechainRuntimeFeatureExtension
    {
        [NoTrace]
        public static IFullNodeBuilder AddFederatedPeg(this IFullNodeBuilder fullNodeBuilder, bool isMainChain = false)
        {
            LoggingConfiguration.RegisterFeatureNamespace<FederatedPegFeature>(FederatedPegFeature.FederationGatewayFeatureNamespace);

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features.AddFeature<FederatedPegFeature>()
                    .DependOn<BlockNotificationFeature>()
                    .DependOn<CounterChainFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<IMaturedBlocksProvider, MaturedBlocksProvider>();
                        services.AddSingleton<IFederatedPegSettings, FederatedPegSettings>();
                        services.AddSingleton<IOpReturnDataReader, OpReturnDataReader>();
                        services.AddSingleton<IDepositExtractor, DepositExtractor>();
                        services.AddSingleton<IWithdrawalExtractor, WithdrawalExtractor>();
                        services.AddSingleton<IFederationWalletSyncManager, FederationWalletSyncManager>();
                        services.AddSingleton<IFederationWalletTransactionHandler, FederationWalletTransactionHandler>();
                        services.AddSingleton<IFederationWalletManager, FederationWalletManager>();
                        services.AddSingleton<IMultisigCoinSelector, MultisigCoinSelector>();
                        services.AddSingleton<FedMultiSigManualWithdrawalTransactionBuilder>();
                        services.AddSingleton<IWithdrawalTransactionBuilder, WithdrawalTransactionBuilder>();
                        services.AddSingleton<ICrossChainTransferStore, CrossChainTransferStore>();
                        services.AddSingleton<ISignedMultisigTransactionBroadcaster, SignedMultisigTransactionBroadcaster>();
                        services.AddSingleton<IPartialTransactionRequester, PartialTransactionRequester>();
                        services.AddSingleton<MempoolCleaner>();
                        services.AddSingleton<IFederationGatewayClient, FederationGatewayClient>();

                        services.AddSingleton<IConversionRequestKeyValueStore, ConversionRequestKeyValueStore>();
                        services.AddSingleton<IConversionRequestRepository, ConversionRequestRepository>();

                        services.AddSingleton<IMaturedBlocksSyncManager, MaturedBlocksSyncManager>();
                        services.AddSingleton<IWithdrawalHistoryProvider, WithdrawalHistoryProvider>();
                        services.AddSingleton<FederatedPegSettings>();

                        services.AddSingleton<IFederatedPegBroadcaster, FederatedPegBroadcaster>();
                        services.AddSingleton<IInputConsolidator, InputConsolidator>();

                        // The reward distribution manager only runs on the side chain.
                        if (!isMainChain)
                        {
                            services.AddSingleton<IRewardDistributionManager, RewardDistributionManager>();
                            services.AddSingleton<ICoinbaseSplitter, PremineCoinbaseSplitter>();
                            services.AddSingleton<BlockDefinition, FederatedPegBlockDefinition>();
                            services.AddSingleton<IBlockBufferGenerator, BlockBufferGenerator>();
                        }

                        // The reward claimer only runs on the main chain.
                        if (isMainChain)
                            services.AddSingleton<RewardClaimer>();

                        // Set up events.
                        services.AddSingleton<TransactionObserver>();
                        services.AddSingleton<BlockObserver>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}