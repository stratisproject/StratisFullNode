using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.Features.PoA.Tests
{
    public class SlotsManagerTests
    {
        private ISlotsManager slotsManager;
        private TestPoANetwork network;
        private readonly PoAConsensusOptions consensusOptions;
        private readonly IFederationManager federationManager;
        private Mock<ChainIndexer> chainIndexer;

        public SlotsManagerTests()
        {
            this.network = new TestPoANetwork();
            this.consensusOptions = this.network.ConsensusOptions;

            this.federationManager = PoATestsBase.CreateFederationManager(this).federationManager;
            var federationHistory = new FederationHistory(this.federationManager);
            this.chainIndexer = new Mock<ChainIndexer>();
            this.slotsManager = new SlotsManager(this.network, this.federationManager, federationHistory);
        }
        
        [Fact]
        public void GetMiningTimestamp()
        {
            var tool = new KeyTool(new DataFolder(string.Empty));
            Key key = tool.GeneratePrivateKey();
            this.network = new TestPoANetwork(new List<PubKey>() { key.PubKey, tool.GeneratePrivateKey().PubKey, tool.GeneratePrivateKey().PubKey });

            IFederationManager fedManager = PoATestsBase.CreateFederationManager(this, this.network, new ExtendedLoggerFactory(), new Signals.Signals(new LoggerFactory(), null)).federationManager;
            var header = new BlockHeader();
            this.chainIndexer.Setup(x => x.Tip).Returns(new ChainedHeader(header, header.GetHash(), 0));
            this.slotsManager = new SlotsManager(this.network, fedManager, new FederationHistory(fedManager));

            List<IFederationMember> federationMembers = fedManager.GetFederationMembers();
            DateTimeOffset roundStart = DateTimeOffset.FromUnixTimeSeconds(this.consensusOptions.TargetSpacingSeconds * (uint)federationMembers.Count * 5);

            fedManager.SetPrivatePropertyValue(typeof(FederationManager), nameof(IFederationManager.CurrentFederationKey), key);
            fedManager.SetPrivatePropertyValue(typeof(FederationManager), nameof(this.federationManager.IsFederationMember), true);

            TimeSpan targetSpacing = TimeSpan.FromSeconds(this.consensusOptions.TargetSpacingSeconds);

            Assert.Equal(roundStart + targetSpacing, this.slotsManager.GetMiningTimestamp(this.chainIndexer.Object.Tip, roundStart));
            Assert.Equal(roundStart + targetSpacing, this.slotsManager.GetMiningTimestamp(this.chainIndexer.Object.Tip, roundStart + TimeSpan.FromSeconds(4)));

            roundStart = roundStart + targetSpacing * federationMembers.Count;
            Assert.Equal(roundStart + targetSpacing, this.slotsManager.GetMiningTimestamp(this.chainIndexer.Object.Tip, roundStart - TimeSpan.FromSeconds(5)));
            Assert.Equal(roundStart + targetSpacing, this.slotsManager.GetMiningTimestamp(this.chainIndexer.Object.Tip, roundStart - TimeSpan.FromSeconds(this.consensusOptions.TargetSpacingSeconds + 1)));

            DateTimeOffset thisTurnTimestamp = roundStart + targetSpacing;
            DateTimeOffset nextTurnTimestamp = thisTurnTimestamp + targetSpacing * federationMembers.Count;

            // If we are past our last timestamp's turn, always give us the NEXT timestamp.
            DateTimeOffset justPastOurTurnTime = thisTurnTimestamp + targetSpacing / 2 + TimeSpan.FromSeconds(1);
            Assert.Equal(nextTurnTimestamp, this.slotsManager.GetMiningTimestamp(this.chainIndexer.Object.Tip, justPastOurTurnTime));

            // TODO: Refactor this.
            /*
            // If we are only just past our last timestamp, but still in the "range" and we haven't mined a block yet, get THIS turn's timestamp.
            Assert.Equal(thisTurnTimestamp, this.slotsManager.GetMiningTimestamp(this.chainIndexer.Object.Tip, thisTurnTimestamp + TimeSpan.FromSeconds(1)));

            // If we are only just past our last timestamp, but we've already mined a block there, then get the NEXT turn's timestamp.
            header = new BlockHeader
            {
                BlockTime = thisTurnTimestamp
            };

            this.chainIndexer.Setup(x => x.Tip).Returns(new ChainedHeader(header, header.GetHash(), 0));
            this.slotsManager = new SlotsManager(this.network, fedManager, new FederationHistory(fedManager), this.chainIndexer.Object, new LoggerFactory());
            Assert.Equal(nextTurnTimestamp, this.slotsManager.GetMiningTimestamp(this.chainIndexer.Object.Tip, thisTurnTimestamp + TimeSpan.FromSeconds(1)));
            */
        }        
    }
}
