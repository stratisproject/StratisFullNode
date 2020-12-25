﻿using System;
using NBitcoin;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.PoA.Models
{
    public class FederationMemberModel
    {
        [JsonProperty("pubkey")]
        public PubKey PubKey { get; set; }

        [JsonProperty("lastActiveTime")]
        public DateTime? LastActiveTime { get; set; }

        [JsonProperty("periodOfInactivity")]
        public TimeSpan? PeriodOfInActivity { get; set; }
    }

    public sealed class FederationMemberDetailedModel : FederationMemberModel
    {
        [JsonProperty("pollStartBlockHeight")]
        public int? PollStartBlockHeight { get; set; }

        [JsonProperty("pollNumberOfVotesAcquired")]
        public int? PollNumberOfVotesAcquired { get; set; }

        [JsonProperty("pollFinishedBlockHeight")]
        public int? PollFinishedBlockHeight { get; set; }

        [JsonProperty("pollWillFinishInBlocks")]
        public long? PollWillFinishInBlocks { get; set; }

        [JsonProperty("pollExecutedBlockHeight")]
        public int? PollExecutedBlockHeight { get; set; }

        [JsonProperty("memberWillStartMiningAtBlockHeight")]
        public long? MemberWillStartMiningAtBlockHeight { get; set; }

        [JsonProperty("memberWillStartEarningRewardsEstimateHeight")]
        public long? MemberWillStartEarningRewardsEstimateHeight { get; set; }

        [JsonProperty("pollType")]
        public string PollType { get; set; }

        [JsonProperty("rewardEstimatePerBlock")]
        public double RewardEstimatePerBlock { get; set; }
    }
}