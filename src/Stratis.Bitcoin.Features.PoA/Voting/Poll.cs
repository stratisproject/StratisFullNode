using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public class Vote : IBitcoinSerializable
    {
        public string PubKey;
        public int Height;

        /// <inheritdoc />
        public void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.PubKey);
            stream.ReadWrite(ref this.Height);
        }
    }

    /// <summary>Information about active poll.</summary>
    public class Poll : IBitcoinSerializable
    {
        public Poll()
        {
            this.PubKeysHexVotedInFavor = new List<Vote>();
        }

        /// <summary>
        /// <c>true</c> if poll still didn't get enough votes.
        /// <c>false</c> in case majority of fed members voted in favor and result of the poll was scheduled to be applied after max reorg blocks are mined.
        /// </summary>
        public bool IsPending => this.PollVotedInFavorBlockData == null;

        /// <summary><c>true</c> if poll wasn't executed yet; <c>false</c> otherwise.</summary>
        public bool IsExecuted => this.PollExecutedBlockData != null;

        public int Id;

        /// <summary>Data of a block where the poll got sufficient amount of votes. <c>null</c> if it number of votes is still insufficient.</summary>
        public HashHeightPair PollVotedInFavorBlockData;

        public VotingData VotingData;

        /// <summary>Data of a block where the poll was started.</summary>
        public HashHeightPair PollStartBlockData;

        /// <summary>Data of a block where the poll's changes were applied.</summary>
        public HashHeightPair PollExecutedBlockData;

        /// <summary>List of fed member's public keys that voted in favor.</summary>
        public List<Vote> PubKeysHexVotedInFavor;

        /// <inheritdoc />
        public void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.Id);
            stream.ReadWrite(ref this.PollVotedInFavorBlockData);
            stream.ReadWrite(ref this.VotingData);
            stream.ReadWrite(ref this.PollStartBlockData);
            stream.ReadWrite(ref this.PollExecutedBlockData);

            if (stream.Serializing)
            {
                stream.ReadWrite(ref this.PubKeysHexVotedInFavor);
            }
            else
            {
                stream.ReadWrite(ref this.PubKeysHexVotedInFavor);

                if (this.PollExecutedBlockData.Hash == uint256.Zero)
                    this.PollExecutedBlockData = null;

                if (this.PollStartBlockData.Hash == uint256.Zero)
                    this.PollStartBlockData = null;

                if (this.PollVotedInFavorBlockData.Hash == uint256.Zero)
                    this.PollVotedInFavorBlockData = null;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{nameof(this.Id)}:{this.Id}, {nameof(this.IsPending)}:{this.IsPending}, {nameof(this.IsExecuted)}:{this.IsExecuted}, " +
                   $"{nameof(this.PollStartBlockData)}:{this.PollStartBlockData?.ToString() ?? "null"}, " +
                   $"{nameof(this.PollVotedInFavorBlockData)}:{this.PollVotedInFavorBlockData?.ToString() ?? "null"}, " +
                   $"{nameof(this.PollExecutedBlockData)}:{this.PollExecutedBlockData?.ToString() ?? "null"}, " +
                   $"{nameof(this.PubKeysHexVotedInFavor)}:{string.Join(" ", this.PubKeysHexVotedInFavor)}";
        }
    }

    public class PollViewModel
    {
        public bool IsPending { get; private set; }

        public bool IsExecuted { get; private set; }

        public int Id { get; private set; }

        public uint256 PollVotedInFavorBlockDataHash { get; private set; }
        public int? PollVotedInFavorBlockDataHeight { get; private set; }

        public uint256 PollStartBlockDataHash { get; private set; }
        public int? PollStartBlockDataHeight { get; private set; }

        public uint256 PollExecutedBlockDataHash { get; private set; }
        public int? PollExecutedBlockDataHeight { get; private set; }

        public List<string> PubKeysHexVotedInFavor { get; private set; }

        public string VotingDataString { get; private set; }

        public PollViewModel(Poll poll, IPollResultExecutor executor)
        {
            this.IsPending = poll.IsPending;
            this.IsExecuted = poll.IsExecuted;
            this.Id = poll.Id;
            this.PollVotedInFavorBlockDataHash = poll.PollVotedInFavorBlockData?.Hash;
            this.PollVotedInFavorBlockDataHeight = poll.PollVotedInFavorBlockData?.Height;
            this.PollStartBlockDataHash = poll.PollStartBlockData?.Hash;
            this.PollStartBlockDataHeight = poll.PollStartBlockData?.Height;
            this.PollExecutedBlockDataHash = poll.PollExecutedBlockData?.Hash;
            this.PollExecutedBlockDataHeight = poll.PollExecutedBlockData?.Height;
            this.PubKeysHexVotedInFavor = poll.PubKeysHexVotedInFavor.Select(v => v.PubKey).ToList();
            this.VotingDataString = executor.ConvertToString(poll.VotingData);
        }
    }
}
