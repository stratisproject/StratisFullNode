using System;
using System.Collections.Generic;
using System.Text;
using Stratis.Bitcoin.EventBus;

namespace Stratis.Bitcoin.Features.PoA.Events
{
    public class MiningStatisticsEvent : EventBase
    {
        public MiningStatisticsModel MiningStatistics { get; }
        public readonly int FederationMemberSize;
        public MiningStatisticsEvent(MiningStatisticsModel miningStatistics, int federationMemberSize)
        {
            this.MiningStatistics = miningStatistics;
            this.FederationMemberSize = federationMemberSize;
        }
    }
}
