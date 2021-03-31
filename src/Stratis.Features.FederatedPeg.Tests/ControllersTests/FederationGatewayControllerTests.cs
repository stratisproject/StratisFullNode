using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using NBitcoin.Protocol;
using NSubstitute;
using NSubstitute.Core;
using Stratis.Bitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Persistence.KeyValueStores;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.FederatedPeg.Controllers;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Features.FederatedPeg.TargetChain;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests.ControllersTests
{
    public class FederationGatewayControllerTests
    {
        private readonly Network network;

        private readonly ICrossChainTransferStore crossChainTransferStore;

        private readonly ILoggerFactory loggerFactory;

        private readonly ILogger logger;

        private readonly IDepositExtractor depositExtractor;

        private readonly IConsensusManager consensusManager;

        private readonly IFederatedPegSettings federatedPegSettings;

        private IFederationManager federationManager;

        private readonly IFederationWalletManager federationWalletManager;

        private readonly ISignals signals;

        private readonly ISignedMultisigTransactionBroadcaster signedMultisigTransactionBroadcaster;

        public FederationGatewayControllerTests()
        {
            this.network = CirrusNetwork.NetworksSelector.Regtest();

            this.crossChainTransferStore = Substitute.For<ICrossChainTransferStore>();
            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.logger = Substitute.For<ILogger>();
            this.loggerFactory.CreateLogger(null).ReturnsForAnyArgs(this.logger);
            this.depositExtractor = Substitute.For<IDepositExtractor>();
            this.consensusManager = Substitute.For<IConsensusManager>();
            this.federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            this.federationWalletManager = Substitute.For<IFederationWalletManager>();
            this.signals = new Signals(this.loggerFactory, null);

            this.signedMultisigTransactionBroadcaster = Substitute.For<ISignedMultisigTransactionBroadcaster>();
        }

        private FederationGatewayController CreateController(IFederatedPegSettings federatedPegSettings)
        {
            var controller = new FederationGatewayController(
                Substitute.For<IAsyncProvider>(),
                new ChainIndexer(),
                Substitute.For<IConnectionManager>(),
                this.crossChainTransferStore,
                this.GetMaturedBlocksProvider(federatedPegSettings),
                this.network,
                this.federatedPegSettings,
                this.federationWalletManager,
                Substitute.For<IFullNode>(),
                Substitute.For<IPeerBanning>(),
                this.federationManager);

            return controller;
        }

        private MaturedBlocksProvider GetMaturedBlocksProvider(IFederatedPegSettings federatedPegSettings)
        {
            IBlockRepository blockRepository = Substitute.For<IBlockRepository>();

            blockRepository.GetBlocks(Arg.Any<List<uint256>>()).ReturnsForAnyArgs((x) =>
            {
                List<uint256> hashes = x.ArgAt<List<uint256>>(0);
                var blocks = new List<Block>();

                foreach (uint256 hash in hashes)
                {
                    blocks.Add(this.network.CreateBlock());
                }

                return blocks;
            });

            IExternalApiPoller externalApiPoller = Substitute.For<IExternalApiPoller>();

            return new MaturedBlocksProvider(this.consensusManager, this.depositExtractor, federatedPegSettings, externalApiPoller);
        }

        [Fact]
        public void GetMaturedBlockDeposits_Fails_When_Block_Height_Greater_Than_Minimum_Deposit_Confirmations_Async()
        {
            ChainedHeader tip = ChainedHeadersHelper.CreateConsecutiveHeaders(5, null, true).Last();
            this.consensusManager.Tip.Returns(tip);

            // Minimum deposit confirmations : 2
            IFederatedPegSettings federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            federatedPegSettings.MinimumConfirmationsNormalDeposits.Returns(2);
            FederationGatewayController controller = this.CreateController(federatedPegSettings);

            int maturedHeight = (int)(tip.Height - 2);

            // Back online at block height : 3
            // 0 - 1 - 2 - 3
            ChainedHeader earlierBlock = tip.GetAncestor(maturedHeight + 1);

            // Mature height = 2 (Chain header height (4) - Minimum deposit confirmations (2))
            IActionResult result = controller.GetMaturedBlockDeposits(earlierBlock.Height);

            // Block height (3) > Mature height (2) - returns error message
            var maturedBlockDepositsResult = (result as JsonResult).Value as SerializableResult<List<MaturedBlockDepositsModel>>;
            maturedBlockDepositsResult.Should().NotBeNull();
            maturedBlockDepositsResult.Value.Count().Should().Be(0);
            Assert.NotNull(maturedBlockDepositsResult.Message);
        }

        [Fact]
        public void GetMaturedBlockDeposits_Gets_All_Matured_Block_Deposits()
        {
            ChainedHeader tip = ChainedHeadersHelper.CreateConsecutiveHeaders(10, null, true).Last();
            this.consensusManager.Tip.Returns(tip);

            int minConfirmations = 2;

            // Minimum deposit confirmations : 2
            IFederatedPegSettings federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            federatedPegSettings.MinimumConfirmationsNormalDeposits.Returns(minConfirmations);
            FederationGatewayController controller = this.CreateController(federatedPegSettings);

            ChainedHeader earlierBlock = tip.GetAncestor(minConfirmations);

            int depositExtractorCallCount = 0;
            this.depositExtractor.ExtractDepositsFromBlock(Arg.Any<Block>(), Arg.Any<int>(), Arg.Any<DepositRetrievalType[]>()).Returns(new List<IDeposit>());
            this.depositExtractor.When(x => x.ExtractDepositsFromBlock(Arg.Any<Block>(), Arg.Any<int>(), Arg.Any<DepositRetrievalType[]>())).Do(info =>
            {
                depositExtractorCallCount++;
            });

            this.consensusManager.GetBlocksAfterBlock(Arg.Any<ChainedHeader>(), MaturedBlocksProvider.MaturedBlocksBatchSize, Arg.Any<CancellationTokenSource>()).Returns(delegate (CallInfo info)
            {
                var chainedHeader = (ChainedHeader)info[0];
                var blocks = new List<ChainedHeaderBlock>();

                int startHeight = (chainedHeader == null) ? 0 : (chainedHeader.Height + 1);

                for (int i = startHeight; i <= this.consensusManager.Tip.Height; i++)
                    blocks.Add(new ChainedHeaderBlock(new Block(), tip.GetAncestor(i)));

                return blocks;
            });

            IActionResult result = controller.GetMaturedBlockDeposits(earlierBlock.Height);

            result.Should().BeOfType<JsonResult>();
            var maturedBlockDepositsResult = (result as JsonResult).Value as SerializableResult<List<MaturedBlockDepositsModel>>;
            maturedBlockDepositsResult.Should().NotBeNull();
            maturedBlockDepositsResult.Message.Should().Be(string.Empty);

            // Heights 0 to 10.
            depositExtractorCallCount.Should().Be(11);
        }

        [Fact]
        public void Call_Sidechain_Gateway_Get_Info()
        {
            var federation = new Federation(new[] {
                new PubKey("02fad5f3c4fdf4c22e8be4cfda47882fff89aaa0a48c1ccad7fa80dc5fee9ccec3"),
                new PubKey("02503f03243d41c141172465caca2f5cef7524f149e965483be7ce4e44107d7d35"),
                new PubKey("03be943c3a31359cd8e67bedb7122a0898d2c204cf2d0119e923ded58c429ef97c")
            });

            Network sidechainNetwork = new TestNetwork(federation);

            string redeemScript = PayToFederationTemplate.Instance.GenerateScriptPubKey(federation.Id).ToString();
            string federationIps = "127.0.0.1:36201,127.0.0.1:36202,127.0.0.1:36203";
            string multisigPubKey = federation.GetFederationDetails().transactionSigningKeys.TakeLast(1).First().ToHex();
            string[] args = new[] { "-sidechain", "-regtest", $"-federationips={federationIps}", $"-redeemscript={redeemScript}", $"-publickey={multisigPubKey}", "-mincoinmaturity=1", "-mindepositconfirmations=1" };
            var nodeSettings = new NodeSettings(sidechainNetwork, ProtocolVersion.ALT_PROTOCOL_VERSION, args: args);

            this.federationWalletManager.IsFederationWalletActive().Returns(true);

            CreateFederationManager(nodeSettings);

            var federatedPegSettings = new FederatedPegSettings(nodeSettings, new CounterChainNetworkWrapper(KnownNetworks.StraxRegTest));

            var controller = new FederationGatewayController(
                Substitute.For<IAsyncProvider>(),
                new ChainIndexer(),
                Substitute.For<IConnectionManager>(),
                this.crossChainTransferStore,
                this.GetMaturedBlocksProvider(federatedPegSettings),
                this.network,
                federatedPegSettings,
                this.federationWalletManager,
                Substitute.For<IFullNode>(),
                Substitute.For<IPeerBanning>(),
                this.federationManager);

            IActionResult result = controller.GetInfo();

            result.Should().BeOfType<JsonResult>();
            ((JsonResult)result).Value.Should().BeOfType<FederationGatewayInfoModel>();

            var model = ((JsonResult)result).Value as FederationGatewayInfoModel;
            model.IsMainChain.Should().BeFalse();
            model.FederationMiningPubKeys.Should().Equal(((PoAConsensusOptions)CirrusNetwork.NetworksSelector.Regtest().Consensus.Options).GenesisFederationMembers.Select(keys => keys.ToString()));
            model.MultiSigRedeemScript.Should().Be(redeemScript);
            string.Join(",", model.FederationNodeIpEndPoints).Should().Be(federationIps);
            model.IsActive.Should().BeTrue();
            model.MinimumDepositConfirmationsSmallDeposits.Should().Be(25);
            model.MinimumDepositConfirmationsNormalDeposits.Should().Be(80);
            model.MultisigPublicKey.Should().Be(multisigPubKey);
        }

        private void CreateFederationManager(NodeSettings nodeSettings)
        {
            var fullNode = new Mock<IFullNode>();

            var counterChainSettings = new CounterChainSettings(nodeSettings, new CounterChainNetworkWrapper(new StraxRegTest()));

            this.federationManager = new FederationManager(fullNode.Object, this.network, NodeSettings.Default(this.network), this.signals, counterChainSettings);

            VotingManager votingManager = InitializeVotingManager(nodeSettings);

            fullNode.Setup(x => x.NodeService<VotingManager>(It.IsAny<bool>())).Returns(votingManager);

            this.federationManager.Initialize();
        }

        private VotingManager InitializeVotingManager(NodeSettings nodeSettings)
        {
            var dbreezeSerializer = new DBreezeSerializer(this.network.Consensus.ConsensusFactory);
            var asyncProvider = new AsyncProvider(this.loggerFactory, this.signals);
            var finalizedBlockRepo = new FinalizedBlockInfoRepository(new LevelDbKeyValueRepository(nodeSettings.DataFolder, dbreezeSerializer), asyncProvider);
            finalizedBlockRepo.LoadFinalizedBlockInfoAsync(this.network).GetAwaiter().GetResult();

            var chainIndexerMock = new Mock<ChainIndexer>();
            var header = new BlockHeader();
            chainIndexerMock.Setup(x => x.Tip).Returns(new ChainedHeader(header, header.GetHash(), 0));

            var votingManager = new VotingManager(this.federationManager, this.loggerFactory, new Mock<IPollResultExecutor>().Object, new Mock<INodeStats>().Object, nodeSettings.DataFolder, dbreezeSerializer, this.signals, finalizedBlockRepo, this.network);
            var federationHistory = new FederationHistory(this.federationManager, votingManager);
            votingManager.Initialize(federationHistory);

            return votingManager;
        }

        public class TestNetwork : CirrusRegTest
        {
            public TestNetwork(Federation federation) : base()
            {
                this.Name = "TestCirrusRegTest";
                this.Federations = new Federations();
                this.Federations.RegisterFederation(federation);
            }
        }

        [Fact]
        public void Call_Mainchain_Gateway_Get_Info()
        {
            var federation = new Federation(new[]
            {
                new PubKey("02fad5f3c4fdf4c22e8be4cfda47882fff89aaa0a48c1ccad7fa80dc5fee9ccec3"),
                new PubKey("02503f03243d41c141172465caca2f5cef7524f149e965483be7ce4e44107d7d35"),
                new PubKey("03be943c3a31359cd8e67bedb7122a0898d2c204cf2d0119e923ded58c429ef97c")
            });

            Network sideChainNetwork = new TestNetwork(federation);

            string redeemScript = PayToFederationTemplate.Instance.GenerateScriptPubKey(federation.Id).ToString();

            string federationIps = "127.0.0.1:36201,127.0.0.1:36202,127.0.0.1:36203";
            string multisigPubKey = federation.GetFederationDetails().transactionSigningKeys.TakeLast(1).First().ToHex();
            string[] args = new[] { "-mainchain", "-testnet", $"-federationips={federationIps}", $"-redeemscript={redeemScript}", $"-publickey={multisigPubKey}", "-mincoinmaturity=1", "-mindepositconfirmations=1" };
            var nodeSettings = new NodeSettings(sideChainNetwork, ProtocolVersion.ALT_PROTOCOL_VERSION, args: args);

            this.federationWalletManager.IsFederationWalletActive().Returns(true);

            var settings = new FederatedPegSettings(nodeSettings, new CounterChainNetworkWrapper(KnownNetworks.StraxRegTest));

            var controller = new FederationGatewayController(
                Substitute.For<IAsyncProvider>(),
                new ChainIndexer(),
                Substitute.For<IConnectionManager>(),
                this.crossChainTransferStore,
                this.GetMaturedBlocksProvider(settings),
                this.network,
                settings,
                this.federationWalletManager,
                Substitute.For<IFullNode>(),
                Substitute.For<IPeerBanning>(),
                this.federationManager);

            IActionResult result = controller.GetInfo();

            result.Should().BeOfType<JsonResult>();
            ((JsonResult)result).Value.Should().BeOfType<FederationGatewayInfoModel>();

            var model = ((JsonResult)result).Value as FederationGatewayInfoModel;
            model.IsMainChain.Should().BeTrue();
            model.FederationMiningPubKeys.Should().BeNull();
            model.MiningPublicKey.Should().BeNull();
            model.MultiSigRedeemScript.Should().Be(redeemScript);
            string.Join(",", model.FederationNodeIpEndPoints).Should().Be(federationIps);
            model.IsActive.Should().BeTrue();
            model.MinimumDepositConfirmationsSmallDeposits.Should().Be(25);
            model.MinimumDepositConfirmationsNormalDeposits.Should().Be(80);
            model.MultisigPublicKey.Should().Be(multisigPubKey);
        }
    }
}
