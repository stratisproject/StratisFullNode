using System.Collections.Generic;
using System.Linq;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.PoA.Features.Voting;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.Features.PoA.Tests
{
    public class VotingManagerTests : PoATestsBase
    {
        private readonly VotingDataEncoder encoder;

        private readonly List<VotingData> changesApplied;
        private readonly List<VotingData> changesReverted;

        public VotingManagerTests()
        {
            this.encoder = new VotingDataEncoder();
            this.changesApplied = new List<VotingData>();
            this.changesReverted = new List<VotingData>();

            this.resultExecutorMock.Setup(x => x.ApplyChange(It.IsAny<VotingData>())).Callback((VotingData data) => this.changesApplied.Add(data));
            this.resultExecutorMock.Setup(x => x.RevertChange(It.IsAny<VotingData>())).Callback((VotingData data) => this.changesReverted.Add(data));
        }

        [Fact]
        public void CanScheduleAndRemoveVotes()
        {
            this.federationManager.SetPrivatePropertyValue(typeof(FederationManager), nameof(this.federationManager.IsFederationMember), true);

            this.votingManager.ScheduleVote(new VotingData());

            Assert.Single(this.votingManager.GetScheduledVotes());

            this.votingManager.ScheduleVote(new VotingData());

            Assert.Single(this.votingManager.GetAndCleanScheduledVotes());

            Assert.Empty(this.votingManager.GetScheduledVotes());
        }

        [Fact]
        public void CanVote()
        {
            // Create add federation member vote.
            PubKey memberPubKey = (new Key()).PubKey;
            var votingData = new VotingData()
            {
                Key = VoteKey.AddFederationMember,
                Data = memberPubKey.ToBytes()
            };

            // Create a voting request.
            var addressKey = new Key();
            var request = new JoinFederationRequest(memberPubKey, new Money(CollateralFederationMember.MinerCollateralAmount, MoneyUnit.BTC), addressKey.PubKey.Hash);
            request.AddSignature(addressKey.SignMessage(request.SignatureMessage));
            ChainedHeaderBlock votingRequest = GetBlocksWithVotingRequest(1, request, new ChainedHeader(this.network.GetGenesis().Header, this.network.GetGenesis().GetHash(), 0)).First();

            int votesRequired = (this.federationManager.GetFederationMembers().Count / 2) + 1;
            ChainedHeaderBlock[] blocks = GetBlocksWithVotingData(votesRequired, votingData, votingRequest.ChainedHeader);

            // Mock the blocks via the block repository.
            this.blockRepository.Setup(r => r.GetBlock(It.IsAny<uint256>())).Returns((uint256 hash) =>
            {
                return (hash == votingRequest.ChainedHeader.HashBlock) ? votingRequest.Block : blocks.FirstOrDefault(b => b.ChainedHeader.HashBlock == hash)?.Block;
            });

            this.TriggerOnBlockConnected(votingRequest);

            for (int i = 0; i < votesRequired; i++)
            {
                this.TriggerOnBlockConnected(blocks[i]);
            }

            Assert.Single(this.votingManager.GetApprovedPolls());
        }

        [Fact]
        public void AddVoteAfterPollComplete()
        {
            // Create add federation member vote.
            PubKey memberPubKey = (new Key()).PubKey;
            var votingData = new VotingData()
            {
                Key = VoteKey.AddFederationMember,
                Data = memberPubKey.ToBytes()
            };

            int votesRequired = (this.federationManager.GetFederationMembers().Count / 2) + 1;

            // Create a voting request.
            var addressKey = new Key();
            var request = new JoinFederationRequest(memberPubKey, new Money(CollateralFederationMember.MinerCollateralAmount, MoneyUnit.BTC), addressKey.PubKey.Hash);
            request.AddSignature(addressKey.SignMessage(request.SignatureMessage));
            ChainedHeaderBlock votingRequest = GetBlocksWithVotingRequest(1, request, new ChainedHeader(this.network.GetGenesis().Header, this.network.GetGenesis().GetHash(), 0)).First();

            ChainedHeaderBlock[] blocks = GetBlocksWithVotingData(votesRequired + 1, votingData, votingRequest.ChainedHeader);

            // Mock the blocks via the block repository.
            this.blockRepository.Setup(r => r.GetBlock(It.IsAny<uint256>())).Returns((uint256 hash) =>
            {
                return (hash == votingRequest.ChainedHeader.HashBlock) ? votingRequest.Block : blocks.FirstOrDefault(b => b.ChainedHeader.HashBlock == hash)?.Block;
            });

            this.TriggerOnBlockConnected(votingRequest);

            for (int i = 0; i < votesRequired; i++)
            {
                this.TriggerOnBlockConnected(blocks[i]);
            }

            Assert.Single(this.votingManager.GetApprovedPolls());
            Assert.Empty(this.votingManager.GetPendingPolls());

            // Now that poll is complete, add another vote for it.
            ChainedHeaderBlock blockToDisconnect = blocks[votesRequired];
            this.TriggerOnBlockConnected(blockToDisconnect);

            // Now we have 1 finished and 0 pending for the same data.
            Assert.Single(this.votingManager.GetApprovedPolls());
            Assert.Single(this.votingManager.GetPendingPolls());

            // This previously caused an error because of Single() being used.
            this.TriggerOnBlockDisconnected(blockToDisconnect);

            // No change, as the superfluous vote had no effect.
            Assert.Single(this.votingManager.GetApprovedPolls());
            Assert.Empty(this.votingManager.GetPendingPolls());
        }

        [Fact]
        public void CanCreateVotingRequest()
        {
            var addressKey = new Key();
            var miningKey = new Key();

            var votingRequest = new JoinFederationRequest(miningKey.PubKey, new Money(10_000m, MoneyUnit.BTC), addressKey.PubKey.Hash);

            votingRequest.AddSignature(addressKey.SignMessage(votingRequest.SignatureMessage));

            int votesRequired = (this.federationManager.GetFederationMembers().Count / 2) + 1;

            ChainedHeaderBlock[] blocks = GetBlocksWithVotingRequest(votesRequired, votingRequest, new ChainedHeader(this.network.GetGenesis().Header, this.network.GetGenesis().GetHash(), 0));

            for (int i = 0; i < votesRequired; i++)
            {
                this.TriggerOnBlockConnected(blocks[i]);
            }
        }

        [Fact]
        public void CanExpireAndUnExpirePollViaBlockDisconnected()
        {
            // Create add federation member vote.
            PubKey memberPubKey = (new Key()).PubKey;
            var votingData = new VotingData()
            {
                Key = VoteKey.AddFederationMember,
                Data = memberPubKey.ToBytes()
            };

            // Create a voting request.
            var addressKey = new Key();
            var request = new JoinFederationRequest(memberPubKey, new Money(CollateralFederationMember.MinerCollateralAmount, MoneyUnit.BTC), addressKey.PubKey.Hash);
            request.AddSignature(addressKey.SignMessage(request.SignatureMessage));
            ChainedHeaderBlock votingRequest = GetBlocksWithVotingRequest(1, request, new ChainedHeader(this.network.GetGenesis().Header, this.network.GetGenesis().GetHash(), 0)).First();

            // Create a single pending poll.
            ChainedHeaderBlock[] blocks = GetBlocksWithVotingData(1, votingData, votingRequest.ChainedHeader);

            // Mock the blocks via the block repository.
            this.blockRepository.Setup(r => r.GetBlock(It.IsAny<uint256>())).Returns((uint256 hash) =>
            {
                return (hash == votingRequest.ChainedHeader.HashBlock) ? votingRequest.Block : blocks.FirstOrDefault(b => b.ChainedHeader.HashBlock == hash)?.Block;
            });

            this.TriggerOnBlockConnected(votingRequest);
            this.TriggerOnBlockConnected(blocks[0]);
            Assert.Single(this.votingManager.GetPendingPolls());

            // Advance the chain so that the poll expires.
            blocks = PoaTestHelper.GetEmptyBlocks(this.ChainIndexer, this.network, 10);

            for (int i = 0; i < blocks.Length; i++)
            {
                this.TriggerOnBlockConnected(blocks[i]);
            }

            // Assert that the poll expired.
            Assert.Single(this.votingManager.GetExpiredPolls());

            // Fake a rewind via block disconnected (this will generally happen via a re-org)
            this.TriggerOnBlockDisconnected(blocks[9]);

            // Assert that the poll was "un-expired".
            Assert.Single(this.votingManager.GetPendingPolls());
        }

        [Fact]
        public void CanExpireAndUnExpirePollViaNodeRewind()
        {
            // Create add federation member vote.
            PubKey memberPubKey = (new Key()).PubKey;
            var votingData = new VotingData()
            {
                Key = VoteKey.AddFederationMember,
                Data = memberPubKey.ToBytes()
            };

            // Create a voting request.
            var addressKey = new Key();
            var request = new JoinFederationRequest(memberPubKey, new Money(CollateralFederationMember.MinerCollateralAmount, MoneyUnit.BTC), addressKey.PubKey.Hash);
            request.AddSignature(addressKey.SignMessage(request.SignatureMessage));
            ChainedHeaderBlock votingRequest = GetBlocksWithVotingRequest(1, request, new ChainedHeader(this.network.GetGenesis().Header, this.network.GetGenesis().GetHash(), 0)).First();

            // Create a single pending poll.
            ChainedHeaderBlock[] blocks = GetBlocksWithVotingData(1, votingData, votingRequest.ChainedHeader);

            // Mock the blocks via the block repository.
            this.blockRepository.Setup(r => r.GetBlock(It.IsAny<uint256>())).Returns((uint256 hash) =>
            {
                return (hash == votingRequest.ChainedHeader.HashBlock) ? votingRequest.Block : blocks.FirstOrDefault(b => b.ChainedHeader.HashBlock == hash)?.Block;
            });

            this.TriggerOnBlockConnected(votingRequest);
            this.TriggerOnBlockConnected(blocks[0]);
            Assert.Single(this.votingManager.GetPendingPolls());

            // Advance the chain so that the poll expires.
            blocks = PoaTestHelper.GetEmptyBlocks(this.ChainIndexer, this.network, 10);
            for (int i = 0; i < blocks.Length; i++)
            {
                this.TriggerOnBlockConnected(blocks[i]);
            }

            // Assert that the poll expired.
            Assert.Single(this.votingManager.GetExpiredPolls());

            // Fake a rewind via setting the node's tip back (this will generally happen via the api/node/rewind call)
            this.ChainIndexer.Remove(this.ChainIndexer.Tip);

            // Re-initialize the voting manager
            this.votingManager.Initialize(this.federationHistory);

            // Assert that the poll was "un-expired".
            Assert.Single(this.votingManager.GetPendingPolls());
        }

        private void TriggerOnBlockConnected(ChainedHeaderBlock block)
        {
            this.signals.Publish(new BlockConnected(block));
        }

        private void TriggerOnBlockDisconnected(ChainedHeaderBlock block)
        {
            this.signals.Publish(new BlockDisconnected(block));
        }

        private ChainedHeaderBlock[] GetBlocksWithVotingData(int count, VotingData votingData, ChainedHeader previous)
        {
            return PoaTestHelper.GetBlocks(count, this.ChainIndexer, i => this.CreateBlockWithVotingData(new List<VotingData>() { votingData }, i + 2), previous);
        }

        private ChainedHeaderBlock[] GetBlocksWithVotingRequest(int count, JoinFederationRequest votingRequest, ChainedHeader previous)
        {
            return PoaTestHelper.GetBlocks(count, this.ChainIndexer, i => this.CreateBlockWithVotingRequest(votingRequest, i + 1), previous);
        }

        private ChainedHeaderBlock CreateBlockWithVotingRequest(JoinFederationRequest votingRequest, int height)
        {
            var encoder = new JoinFederationRequestEncoder();

            var votingRequestData = new List<byte>();
            votingRequestData.AddRange(encoder.Encode(votingRequest));

            var votingRequestOutputScript = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(votingRequestData.ToArray()));

            Transaction tx = this.network.CreateTransaction();
            tx.AddOutput(new TxOut());
            tx.AddOutput(new Money(JoinFederationRequestBuilder.VotingRequestTransferAmount, MoneyUnit.BTC), votingRequestOutputScript);

            Block block = PoaTestHelper.CreateBlock(this.network, tx, height);

            return new ChainedHeaderBlock(block, new ChainedHeader(block.Header, block.GetHash(), height));
        }

        private ChainedHeaderBlock CreateBlockWithVotingData(List<VotingData> data, int height)
        {
            var votingData = new List<byte>(VotingDataEncoder.VotingOutputPrefixBytes);
            votingData.AddRange(this.encoder.Encode(data));

            var votingOutputScript = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(votingData.ToArray()));

            Transaction tx = this.network.CreateTransaction();
            tx.AddOutput(Money.COIN, votingOutputScript);

            Block block = PoaTestHelper.CreateBlock(this.network, tx, height);

            return new ChainedHeaderBlock(block, new ChainedHeader(block.Header, block.GetHash(), height));
        }
    }
}