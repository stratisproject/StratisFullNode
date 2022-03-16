using System;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Features.PoA.Tests
{
    public sealed class PollsRepositoryTests
    {
        private readonly ChainIndexer chainIndexer;
        private readonly PollsRepository repository;

        public PollsRepositoryTests()
        {
            var dataDir = new DataFolder(TestBase.CreateTestDir(this));
            TestPoANetwork network = new TestPoANetwork();
            this.chainIndexer = new ChainIndexer(network);

            this.repository = new PollsRepository(this.chainIndexer, dataDir, new DBreezeSerializer(network.Consensus.ConsensusFactory), network);
            this.repository.Initialize();
        }

        [Fact]
        public void CantAddOrRemovePollsOutOfOrder()
        {
            Assert.Equal(-1, this.repository.GetHighestPollId());

            this.repository.WithTransaction(transaction =>
            {
                transaction.AddPolls(new Poll() { Id = 0 });
                transaction.AddPolls(new Poll() { Id = 1 });
                transaction.AddPolls(new Poll() { Id = 2 });
                Assert.Throws<ArgumentException>(() => transaction.AddPolls(new Poll() { Id = 5 }));
                transaction.AddPolls(new Poll() { Id = 3 });

                transaction.Commit();
            });

            Assert.Equal(3, this.repository.GetHighestPollId());

            this.repository.WithTransaction(transaction =>
            {
                transaction.RemovePolls(3);

                Assert.Throws<ArgumentException>(() => transaction.RemovePolls(6));
                Assert.Throws<ArgumentException>(() => transaction.RemovePolls(3));

                transaction.RemovePolls(2);
                transaction.RemovePolls(1);
                transaction.RemovePolls(0);

                transaction.Commit();
            });

            this.repository.Dispose();
        }

        [Fact]
        public void SavesHighestPollId()
        {
            this.repository.WithTransaction(transaction =>
            {
                transaction.AddPolls(new Poll() { Id = 0, PollStartBlockData = new HashHeightPair(1, 1) });
                transaction.AddPolls(new Poll() { Id = 1, PollStartBlockData = new HashHeightPair(2, 2) });
                transaction.AddPolls(new Poll() { Id = 2, PollStartBlockData = new HashHeightPair(3, 3) });

                transaction.Commit();
            });

            this.repository.Initialize();

            Assert.Equal(2, this.repository.GetHighestPollId());
        }

        [Fact]
        public void CanLoadPolls()
        {
            this.repository.WithTransaction(transaction =>
            {
                transaction.AddPolls(new Poll() { Id = 0 });
                transaction.AddPolls(new Poll() { Id = 1 });
                transaction.AddPolls(new Poll() { Id = 2 });

                transaction.Commit();
            });

            this.repository.WithTransaction(transaction =>
            {
                Assert.True(transaction.GetPolls( 0, 1, 2).Count == 3);
                Assert.True(transaction.GetAllPolls().Count == 3);
                Assert.Throws<ArgumentException>(() => transaction.GetPolls(-1));
                Assert.Throws<ArgumentException>(() => transaction.GetPolls(9));
            });
        }

        [Fact]
        public void CanUpdatePolls()
        {
            var poll = new Poll() { Id = 0, VotingData = new VotingData() { Key = VoteKey.AddFederationMember } };

            this.repository.WithTransaction(transaction =>
             {
                 transaction.AddPolls(poll);

                 poll.VotingData.Key = VoteKey.KickFederationMember;
                 transaction.UpdatePoll(poll);

                 transaction.Commit();
             });

            this.repository.WithTransaction(transaction =>
            {
                Assert.Equal(VoteKey.KickFederationMember, transaction.GetPolls(poll.Id).First().VotingData.Key);
            });
        }
    }
}
