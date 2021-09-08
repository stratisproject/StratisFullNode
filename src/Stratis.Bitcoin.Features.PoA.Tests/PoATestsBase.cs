using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Persistence.KeyValueStores;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral.CounterChain;

namespace Stratis.Bitcoin.Features.PoA.Tests
{
    public class PoATestsBase
    {
        protected readonly ChainedHeader currentHeader;
        protected readonly TestPoANetwork network;
        protected readonly PoAConsensusOptions consensusOptions;

        protected PoAConsensusRuleEngine rulesEngine;
        protected readonly LoggerFactory loggerFactory;
        protected readonly PoABlockHeaderValidator poaHeaderValidator;
        protected readonly ISlotsManager slotsManager;
        protected readonly ConsensusSettings consensusSettings;
        protected readonly ChainIndexer ChainIndexer;
        protected readonly IFederationManager federationManager;
        protected readonly IFederationHistory federationHistory;
        protected readonly VotingManager votingManager;
        protected readonly Mock<IPollResultExecutor> resultExecutorMock;
        protected readonly Mock<ChainIndexer> chainIndexerMock;
        protected readonly ISignals signals;
        protected readonly DBreezeSerializer dBreezeSerializer;
        protected readonly ChainState chainState;
        protected readonly IAsyncProvider asyncProvider;
        protected readonly Mock<IFullNode> fullNode;

        public PoATestsBase(TestPoANetwork network = null)
        {
            this.loggerFactory = new LoggerFactory();
            this.signals = new Signals.Signals(this.loggerFactory, null);
            this.network = network ?? new TestPoANetwork();
            this.consensusOptions = this.network.ConsensusOptions;
            this.dBreezeSerializer = new DBreezeSerializer(this.network.Consensus.ConsensusFactory);

            this.ChainIndexer = new ChainIndexer(this.network);
            IDateTimeProvider dateTimeProvider = new DateTimeProvider();
            this.consensusSettings = new ConsensusSettings(NodeSettings.Default(this.network));

            (this.federationManager, this.federationHistory) = CreateFederationManager(this, this.network, this.loggerFactory, this.signals);

            this.chainIndexerMock = new Mock<ChainIndexer>();
            var header = new BlockHeader();
            this.chainIndexerMock.Setup(x => x.Tip).Returns(new ChainedHeader(header, header.GetHash(), 0));
            this.slotsManager = new SlotsManager(this.network, this.federationManager, this.chainIndexerMock.Object, this.loggerFactory);

            this.poaHeaderValidator = new PoABlockHeaderValidator(this.loggerFactory);
            this.asyncProvider = new AsyncProvider(this.loggerFactory, this.signals);

            var dataFolder = new DataFolder(TestBase.CreateTestDir(this));

            this.resultExecutorMock = new Mock<IPollResultExecutor>();

            this.votingManager = new VotingManager(this.federationManager, this.loggerFactory, this.resultExecutorMock.Object, new NodeStats(dateTimeProvider, NodeSettings.Default(this.network), new Mock<IVersionProvider>().Object),
                dataFolder, this.dBreezeSerializer, this.signals, this.network, null, this.chainIndexerMock.Object);

            this.votingManager.Initialize(this.federationHistory);

            this.chainState = new ChainState();

            this.rulesEngine = new PoAConsensusRuleEngine(this.network, this.loggerFactory, new DateTimeProvider(), this.ChainIndexer, new NodeDeployments(this.network, this.ChainIndexer),
                this.consensusSettings, new Checkpoints(this.network, this.consensusSettings), new Mock<ICoinView>().Object, this.chainState, new InvalidBlockHashStore(dateTimeProvider),
               new NodeStats(dateTimeProvider, NodeSettings.Default(this.network), new Mock<IVersionProvider>().Object), this.slotsManager, this.poaHeaderValidator, this.votingManager, this.federationManager, this.asyncProvider,
                new ConsensusRulesContainer(), this.federationHistory);

            List<ChainedHeader> headers = ChainedHeadersHelper.CreateConsecutiveHeaders(50, null, false, null, this.network);

            this.currentHeader = headers.Last();
        }

        public static (IFederationManager federationManager, IFederationHistory federationHistory) CreateFederationManager(object caller, Network network, LoggerFactory loggerFactory, ISignals signals)
        {
            string dir = TestBase.CreateTestDir(caller);

            var dbreezeSerializer = new DBreezeSerializer(network.Consensus.ConsensusFactory);

            var nodeSettings = new NodeSettings(network, args: new string[] { $"-datadir={dir}" });

            Key federationKey = new Mnemonic("lava frown leave wedding virtual ghost sibling able mammal liar wide wisdom").DeriveExtKey().PrivateKey;
            new KeyTool(nodeSettings.DataFolder).SavePrivateKey(federationKey);

            var consensusManager = new Mock<IConsensusManager>();
            consensusManager.Setup(c => c.Tip).Returns(new ChainedHeader(network.GetGenesis().Header, network.GenesisHash, 0));

            var fullNode = new Mock<IFullNode>();
            fullNode.Setup(x => x.NodeService<IConsensusManager>(false)).Returns(consensusManager.Object);

            var counterChainSettings = new CounterChainSettings(nodeSettings, new CounterChainNetworkWrapper(new StraxRegTest()));

            var federationManager = new FederationManager(fullNode.Object, network, nodeSettings, signals, counterChainSettings);
            var asyncProvider = new AsyncProvider(loggerFactory, signals);

            var chainIndexerMock = new Mock<ChainIndexer>();
            var header = new BlockHeader();
            chainIndexerMock.Setup(x => x.Tip).Returns(new ChainedHeader(header, header.GetHash(), 0));
            var votingManager = new VotingManager(federationManager, loggerFactory,
                new Mock<IPollResultExecutor>().Object, new Mock<INodeStats>().Object, nodeSettings.DataFolder, dbreezeSerializer, signals, network, null, chainIndexerMock.Object, null);

            var federationHistory = new Mock<IFederationHistory>();
            federationHistory.Setup(x => x.GetFederationMemberForBlock(It.IsAny<ChainedHeader>())).Returns<ChainedHeader>((chainedHeader) =>
            {
                List<IFederationMember> members = ((PoAConsensusOptions)network.Consensus.Options).GenesisFederationMembers;
                return members[chainedHeader.Height % members.Count];
            });

            federationHistory.Setup(x => x.GetFederationForBlock(It.IsAny<ChainedHeader>())).Returns<ChainedHeader>((chainedHeader) =>
            {
                return ((PoAConsensusOptions)network.Consensus.Options).GenesisFederationMembers;
            });

            federationHistory.Setup(x => x.CanGetFederationForBlock(It.IsAny<ChainedHeader>())).Returns<ChainedHeader>((chainedHeader) => true);

            votingManager.Initialize(federationHistory.Object);
            fullNode.Setup(x => x.NodeService<VotingManager>(It.IsAny<bool>())).Returns(votingManager);
            federationManager.Initialize();

            return (federationManager, federationHistory.Object);
        }

        public static (IFederationManager federationManager, IFederationHistory federationHistory) CreateFederationManager(object caller)
        {
            return CreateFederationManager(caller, new TestPoANetwork(), new ExtendedLoggerFactory(), new Signals.Signals(new LoggerFactory(), null));
        }

        public void InitRule(ConsensusRuleBase rule)
        {
            rule.Parent = this.rulesEngine;
            rule.Logger = this.loggerFactory.CreateLogger(rule.GetType().FullName);
            rule.Initialize();
        }
    }

    public class TestPoANetwork : PoANetwork
    {
        public TestPoANetwork(List<PubKey> pubKeysOverride = null)
        {
            List<IFederationMember> genesisFederationMembers;

            if (pubKeysOverride != null)
            {
                genesisFederationMembers = new List<IFederationMember>();

                foreach (PubKey key in pubKeysOverride)
                    genesisFederationMembers.Add(new FederationMember(key));
            }
            else
            {
                genesisFederationMembers = new List<IFederationMember>()
                {
                    new FederationMember(new PubKey("02d485fc5ae101c2780ff5e1f0cb92dd907053266f7cf3388eb22c5a4bd266ca2e")),
                    new FederationMember(new PubKey("026ed3f57de73956219b85ef1e91b3b93719e2645f6e804da4b3d1556b44a477ef")),
                    new FederationMember(new PubKey("03895a5ba998896e688b7d46dd424809b0362d61914e1432e265d9539fe0c3cac0")),
                    new FederationMember(new PubKey("020fc3b6ac4128482268d96f3bd911d0d0bf8677b808eaacd39ecdcec3af66db34")),
                    new FederationMember(new PubKey("038d196fc2e60d6dfc533c6a905ba1f9092309762d8ebde4407d209e37a820e462")),
                    new FederationMember(new PubKey("0358711f76435a508d98a9dee2a7e160fed5b214d97e65ea442f8f1265d09e6b55"))
                };
            }

            var baseOptions = this.Consensus.Options as PoAConsensusOptions;

            this.Consensus.Options = new PoAConsensusOptions(
                maxBlockBaseSize: baseOptions.MaxBlockBaseSize,
                maxStandardVersion: baseOptions.MaxStandardVersion,
                maxStandardTxWeight: baseOptions.MaxStandardTxWeight,
                maxBlockSigopsCost: baseOptions.MaxBlockSigopsCost,
                maxStandardTxSigopsCost: baseOptions.MaxStandardTxSigopsCost,
                genesisFederationMembers: genesisFederationMembers,
                targetSpacingSeconds: 60,
                votingEnabled: baseOptions.VotingEnabled,
                autoKickIdleMembers: baseOptions.AutoKickIdleMembers,
                federationMemberMaxIdleTimeSeconds: baseOptions.FederationMemberMaxIdleTimeSeconds
            );

            this.Consensus.SetPrivatePropertyValue(nameof(this.Consensus.MaxReorgLength), (uint)5);
        }
    }
}
