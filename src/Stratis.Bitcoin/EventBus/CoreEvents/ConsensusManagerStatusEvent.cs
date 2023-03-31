namespace Stratis.Bitcoin.EventBus.CoreEvents
{
    public class ConsensusManagerStatusEvent : EventBase
    {
        public bool IsIbd { get; private set; }

        public int? HeaderHeight { get; private set; }

        public ConsensusManagerStatusEvent(bool isIbd, int? headerHeight)
        {
            this.IsIbd = isIbd;
            this.HeaderHeight = headerHeight;
        }
    }
}
