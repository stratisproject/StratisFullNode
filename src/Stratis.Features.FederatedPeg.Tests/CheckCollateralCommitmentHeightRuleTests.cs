using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral;
using Stratis.Features.PoA.Collateral;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    public class CheckCollateralCommitmentHeightRuleTests
    {
        private readonly CheckCollateralCommitmentHeightRule rule;
        private readonly RuleContext ruleContext;
        private readonly CollateralHeightCommitmentEncoder commitmentHeightEncoder;

        private void EncodePreviousHeaderCommitmentHeight(int commitmentHeight)
        {
            // Setup previous block.
            var encodedHeight = this.commitmentHeightEncoder.EncodeCommitmentHeight(commitmentHeight);
            var commitmentHeightData = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(encodedHeight));

            Block prevBlock = this.ruleContext.ValidationContext.ChainedHeaderToValidate.Previous.Block;
            prevBlock.Transactions = new List<Transaction>();
            prevBlock.AddTransaction(new Transaction());
            prevBlock.Transactions[0].AddOutput(Money.Zero, commitmentHeightData);
        }

        public CheckCollateralCommitmentHeightRuleTests()
        {
            this.ruleContext = new RuleContext(new ValidationContext(), DateTimeOffset.Now);
            var prevHeader = new BlockHeader { Time = 5200 };
            var prevChainedHeader = new ChainedHeader(prevHeader, prevHeader.GetHash(), int.MaxValue - 1);
            var prevBlock = new Block(prevHeader);
            prevChainedHeader.Block = prevBlock;
            prevChainedHeader.BlockDataAvailability = BlockDataAvailabilityState.BlockAvailable;
            var header = new BlockHeader() { Time = 5234, HashPrevBlock = prevHeader.GetHash() };
            this.ruleContext.ValidationContext.BlockToValidate = new Block(header);
            this.ruleContext.ValidationContext.ChainedHeaderToValidate = new ChainedHeader(header, header.GetHash(), prevChainedHeader);

            Block block = this.ruleContext.ValidationContext.BlockToValidate;
            block.AddTransaction(new Transaction());

            var loggerFactory = new ExtendedLoggerFactory();
            ILogger logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.commitmentHeightEncoder = new CollateralHeightCommitmentEncoder();

            // Setup block.
            byte[] encodedHeight = this.commitmentHeightEncoder.EncodeCommitmentHeight(1000);
            var commitmentHeightData = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(encodedHeight));
            block.Transactions[0].AddOutput(Money.Zero, commitmentHeightData);

            var network = new CirrusMain();
            var chainIndexer = new ChainIndexer(network);

            var consensusRules = new Mock<ConsensusRuleEngine>(
                network,
                loggerFactory,
                new Mock<IDateTimeProvider>().Object,
                chainIndexer,
                new NodeDeployments(network, chainIndexer),
                new ConsensusSettings(new NodeSettings(network)),
                new Mock<ICheckpoints>().Object,
                new Mock<IChainState>().Object,
                new Mock<IInvalidBlockHashStore>().Object,
                new Mock<INodeStats>().Object,
                new ConsensusRulesContainer());

            this.rule = new CheckCollateralCommitmentHeightRule()
            {
                Logger = logger,
                Parent = consensusRules.Object
            };

            this.rule.Initialize();
        }

        [Fact]
        public async Task PassesIfCollateralHeightsAreOrderedAsync()
        {
            EncodePreviousHeaderCommitmentHeight(999);
            await this.rule.RunAsync(this.ruleContext);
        }

        [Fact]
        public async Task FailsIfCollateralHeightsAreDisorderedAsync()
        {
            EncodePreviousHeaderCommitmentHeight(1001);
            await Assert.ThrowsAsync<ConsensusErrorException>(() => this.rule.RunAsync(this.ruleContext));
        }
    }
}