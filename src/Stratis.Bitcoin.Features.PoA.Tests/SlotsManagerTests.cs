using System;
using System.Collections.Generic;
using System.Linq;
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
        private Key key;

        public SlotsManagerTests()
        {
            var tool = new KeyTool(new DataFolder(string.Empty));
            this.key = tool.GeneratePrivateKey();

            var pubKeys = new List<PubKey>() { this.key.PubKey, tool.GeneratePrivateKey().PubKey, tool.GeneratePrivateKey().PubKey };
            var members = pubKeys.Select(p => (IFederationMember)(new FederationMember(p))).ToList();
            this.network = new TestPoANetwork(pubKeys);

            this.consensusOptions = this.network.ConsensusOptions;

            // Set up a scenario where one block has been mined by the first member of the federation.

            var header = new PoABlockHeader();
            this.chainIndexer = new Mock<ChainIndexer>();
            this.chainIndexer.Setup(x => x.Tip).Returns(new ChainedHeader(header, header.GetHash(), 0));

            var settings = new NodeSettings(this.network);

            var res = PoATestsBase.CreateFederationManager(this);
            this.federationManager = res.federationManager;
            var federationHistory = new FederationHistory(this.federationManager, this.network, res.votingManager, this.chainIndexer.Object, settings);
            federationHistory.SetPrivateVariableValue("federationHistory", new SortedDictionary<int, (List<IFederationMember>, HashSet<IFederationMember>, IFederationMember)>()
            {
                {0, (members, new HashSet<IFederationMember>() { }, members[0]) },
                {1, (members, new HashSet<IFederationMember>() { }, null) }
            });

            federationHistory.SetPrivateVariableValue("lastActiveTip", this.chainIndexer.Object.Tip);
            federationHistory.SetPrivateVariableValue("lastFederationTip", 0);
            this.slotsManager = new SlotsManager(this.network, this.federationManager, federationHistory);

            IFederationManager fedManager = res.federationManager;
            this.slotsManager = new SlotsManager(this.network, fedManager, federationHistory);
        }

        [Fact]
        public void GetMiningTimestamp()
        {
            List<IFederationMember> federationMembers = this.federationManager.GetFederationMembers();
            DateTimeOffset roundStart = DateTimeOffset.FromUnixTimeSeconds(this.consensusOptions.TargetSpacingSeconds * (uint)federationMembers.Count * 5);

            this.federationManager.SetPrivatePropertyValue(typeof(FederationManager), nameof(IFederationManager.CurrentFederationKey), this.key);
            this.federationManager.SetPrivatePropertyValue(typeof(FederationManager), nameof(this.federationManager.IsFederationMember), true);

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
