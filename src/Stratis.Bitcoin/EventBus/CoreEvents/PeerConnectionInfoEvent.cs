using System;
using System.Collections.Generic;
using System.Text;
using Stratis.Bitcoin.Connection;

namespace Stratis.Bitcoin.EventBus.CoreEvents
{
    public class PeerConnectionInfoEvent : EventBase
    {
        public IEnumerable<PeerConnectionModel> peerConnectionModels { get; set; }

        public PeerConnectionInfoEvent(IEnumerable<PeerConnectionModel> peerConnectionModels)
        {
            this.peerConnectionModels = peerConnectionModels;
        }
    }
}
