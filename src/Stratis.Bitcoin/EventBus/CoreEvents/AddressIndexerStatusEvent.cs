
namespace Stratis.Bitcoin.EventBus.CoreEvents
{
    public sealed class AddressIndexerStatusEvent : EventBase
    {
        public int Tip { get; set; }
    }
}
