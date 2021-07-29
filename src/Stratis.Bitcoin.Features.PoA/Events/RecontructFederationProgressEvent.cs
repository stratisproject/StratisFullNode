using Stratis.Bitcoin.EventBus;

namespace Stratis.Bitcoin.Features.PoA.Events
{
    public sealed class RecontructFederationProgressEvent : EventBase
    {
        public string Progress { get; set; }
    }
}
