using System;
using System.Collections.Generic;
using System.Text;
using Stratis.Bitcoin.EventBus;

namespace Stratis.Bitcoin.Features.PoA.Events
{
    public class MiningStatisticsEvent : EventBase
    {
        public MiningStatisticsModel MiningStatistics { get; }
        public MiningStatisticsEvent(MiningStatisticsModel miningStatistics)
        {
            this.MiningStatistics = miningStatistics;
        }
    }
}
