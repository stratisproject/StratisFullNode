using System;
using System.Collections.Generic;
using System.Text;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Features.PoA.Events;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public class MiningStatisticsClientEvent : IClientEvent
    {
        public bool IsMining { get; set; }
        public int BlockProducerHit { get; set; }
        public Type NodeEventType { get; } = typeof(MiningStatisticsEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is MiningStatisticsEvent miningStatisticsEvent)
            {                
                this.IsMining = miningStatisticsEvent.MiningStatistics.ProducedBlockInLastRound;
                this.BlockProducerHit = miningStatisticsEvent.MiningStatistics.MinerHits;
                return;
            }

            throw new ArgumentException();
        }
    }
}
