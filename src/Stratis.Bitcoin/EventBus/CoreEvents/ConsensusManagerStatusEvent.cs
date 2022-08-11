using System;
using System.Collections.Generic;
using System.Text;

namespace Stratis.Bitcoin.EventBus.CoreEvents
{
    public class ConsensusManagerStatusEvent : EventBase
    {
        public readonly bool IsIbd;

        public ConsensusManagerStatusEvent(bool isIbd)
        {
            this.IsIbd = isIbd;
        }
    }
}
