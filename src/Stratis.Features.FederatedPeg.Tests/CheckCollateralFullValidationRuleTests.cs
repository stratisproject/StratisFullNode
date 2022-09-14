using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.PoA.Collateral;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    public class CheckCollateralFullValidationRuleTests
    {
        private readonly CheckCollateralFullValidationRule rule;
        private readonly Mock<IInitialBlockDownloadState> ibdMock;
        private readonly Mock<ICollateralChecker> collateralCheckerMock;
        private readonly Mock<ISlotsManager> slotsManagerMock;
        private readonly RuleContext ruleContext;

        public CheckCollateralFullValidationRuleTests()
        {
            this.ibdMock = new Mock<IInitialBlockDownloadState>();
            this.collateralCheckerMock = new Mock<ICollateralChecker>();
            this.slotsManagerMock = new Mock<ISlotsManager>();

            this.ibdMock.Setup(x => x.IsInitialBlockDownload()).Returns(false);
            this.ruleContext = new RuleContext(new ValidationContext(), DateTimeOffset.Now);
            var header = new BlockHeader() { Time = 5234 };
            this.ruleContext.ValidationContext.BlockToValidate = new Block(header);
            this.ruleContext.ValidationContext.ChainedHeaderToValidate = new ChainedHeader(header, header.GetHash(), 0);

            Block block = this.ruleContext.ValidationContext.BlockToValidate;
            block.AddTransaction(new Transaction());

            var loggerFactory = new ExtendedLoggerFactory();
            ILogger logger = loggerFactory.CreateLogger(this.GetType().FullName);

            var votingDataEncoder = new VotingDataEncoder();
            var votes = new List<VotingData>
            {
                new VotingData()
                {
                    Key = VoteKey.WhitelistHash,
                    Data = new uint256(0).ToBytes()
                }
            };
            byte[] encodedVotingData = votingDataEncoder.Encode(votes);

            var votingData = new List<byte>(VotingDataEncoder.VotingOutputPrefixBytes);
            votingData.AddRange(encodedVotingData);

            var votingOutputScript = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(votingData.ToArray()));
            block.Transactions[0].AddOutput(Money.Zero, votingOutputScript);

            var commitmentHeightEncoder = new CollateralHeightCommitmentEncoder();
            byte[] encodedHeight = commitmentHeightEncoder.EncodeCommitmentHeight(1000);
            var commitmentHeightData = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(encodedHeight));
            block.Transactions[0].AddOutput(Money.Zero, commitmentHeightData);

            var fullnode = new Mock<IFullNode>();
            fullnode.Setup(x => x.NodeService<CounterChainNetworkWrapper>(false)).Returns(new CounterChainNetworkWrapper(new StraxMain()));
            var consensusManager = new Mock<IConsensusManager>();
            fullnode.Setup(x => x.NodeService<IConsensusManager>(false)).Returns(consensusManager.Object);

            var federationHistory = new Mock<IFederationHistory>();
            federationHistory
                .Setup(x => x.GetFederationMemberForBlock(It.IsAny<ChainedHeader>()))
                .Returns(new CollateralFederationMember(new Key().PubKey, false, new Money(1), "addr1"));

            this.rule = new CheckCollateralFullValidationRule(this.ibdMock.Object, this.collateralCheckerMock.Object, new Mock<IDateTimeProvider>().Object, new PoANetwork(), federationHistory.Object)
            {
                Logger = logger
            };

            this.rule.Initialize();
        }

        [Fact]
        public async Task SkippedIfIBDAsync()
        {
            this.ibdMock.Setup(x => x.IsInitialBlockDownload()).Returns(true);

            await this.rule.RunAsync(new RuleContext(new ValidationContext(), DateTimeOffset.Now));
        }

        [Fact]
        public async Task PassesIfCollateralIsOkAsync()
        {
            this.collateralCheckerMock.Setup(x => x.CheckCollateral(It.IsAny<IFederationMember>(), It.IsAny<int>(), It.IsAny<int>())).Returns(true);
            this.collateralCheckerMock.Setup(x => x.GetCounterChainConsensusHeight()).Returns(5000);

            await this.rule.RunAsync(this.ruleContext);
        }

        [Fact]
        public async Task ThrowsIfCollateralCheckFailsAsync()
        {
            this.collateralCheckerMock.Setup(x => x.CheckCollateral(It.IsAny<IFederationMember>(), It.IsAny<int>(), It.IsAny<int>())).Returns(false);

            await Assert.ThrowsAsync<ConsensusErrorException>(() => this.rule.RunAsync(this.ruleContext));
        }
    }
}