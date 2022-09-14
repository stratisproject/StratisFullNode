using System;
using System.Collections.Generic;
using System.Text;

namespace Stratis.Bitcoin.EventBus.CoreEvents
{
    public class ConsensusManagerStatusEvent : EventBase
    {
        public readonly bool IsIbd;
        public readonly int? HeaderHeight;

        public ConsensusManagerStatusEvent(bool isIbd, int? headerHeight)
        {
            this.IsIbd = isIbd;
            this.HeaderHeight = headerHeight;
        }
    }
}
