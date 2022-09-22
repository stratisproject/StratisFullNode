using System;
using System.Collections.Generic;
using System.Text;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public class PeerConnectionInfoClientEvent : IClientEvent
    {
        public IEnumerable<PeerConnectionModel> PeerConnectionModels { get; set; }
        public Type NodeEventType { get; } = typeof(PeerConnectionInfoEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is PeerConnectionInfoEvent peerConnectionInfo)
            {
                this.PeerConnectionModels = peerConnectionInfo.PeerConnectionModels;

                return;
            }
        }
    }
}
