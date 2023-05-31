namespace Stratis.Bitcoin.EventBus.CoreEvents
{
    public sealed class MultiSigMemberStateRequestEvent : EventBase
    {
        public MultiSigMemberStateRequestEvent()
        {
        }

        public string PubKey { get; set; }

        public int CrossChainStoreHeight { get; set; }

        public int CrossChainStoreNextDepositHeight { get; set; }

        public int PartialTransactions { get; set; }

        public int SuspendedPartialTransactions { get; set; }
    }
}