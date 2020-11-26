using System;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.PoA.Voting;

namespace Stratis.Bitcoin.Features.PoA.Models
{
    public sealed class FederationMemberModel
    {
        [JsonProperty("pubkey")]
        public PubKey PubKey { get; set; }

        [JsonProperty("lastActiveTime")]
        public DateTime LastActiveTime { get; set; }

        [JsonProperty("periodOfInactivity")]
        public TimeSpan PeriodOfInActivity { get; set; }

        [JsonProperty("pollStart")]
        public int PollStartBlockHeight { get; set; }

        [JsonProperty("pollVoteCount")]
        public int PollNumberOfVotesAcquired { get; set; }

        [JsonProperty("pollFinished")]
        public int PollFinishedBlockHeight { get; set; }

        [JsonProperty("pollFinishInBlocks")]
        public long PollWillFinishInBlocks { get; set; }

        [JsonProperty("pollExecuted")]
        public int PollExecutedBlockHeight { get; set; }

        [JsonProperty("startMining")]
        public long MemberWillStartMiningAtBlockHeight { get; set; }

        [JsonProperty("rewardsAtHeight")]
        public long MemberWillStartEarningRewardsEstimateHeight { get; set; }

        [JsonProperty("pollType")]
        public VoteKey PollType { get; internal set; }

        [JsonProperty("rewardEstimate")]
        public Money RewardEstimatePerBlock { get; internal set; }
    }
}