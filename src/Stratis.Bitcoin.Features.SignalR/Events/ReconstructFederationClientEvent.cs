using System;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Features.PoA.Events;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public sealed class ReconstructFederationClientEvent : IClientEvent
    {
        public string Progress { get; set; }

        public Type NodeEventType { get; } = typeof(RecontructFederationProgressEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is RecontructFederationProgressEvent progressEvent)
            {
                this.Progress = progressEvent.Progress;
                return;
            }

            throw new ArgumentException();
        }
    }
}