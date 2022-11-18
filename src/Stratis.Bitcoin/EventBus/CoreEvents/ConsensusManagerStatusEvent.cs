namespace Stratis.Bitcoin.EventBus.CoreEvents
{
    public class ConsensusManagerStatusEvent : EventBase
    {
        public bool IsIbd { get; }

        public int? HeaderHeight { get; }

        public ConsensusManagerStatusEvent(bool isIbd, int? headerHeight)
        {
            this.IsIbd = isIbd;
            this.HeaderHeight = headerHeight;
        }
    }
}
