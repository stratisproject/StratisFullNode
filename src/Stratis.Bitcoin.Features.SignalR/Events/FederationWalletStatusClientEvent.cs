using System;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;

namespace Stratis.Bitcoin.Features.SignalR.Events
{
    public sealed class FederationWalletStatusClientEvent : IClientEvent
    {
        public string ConfirmedBalance { get; set; }
        public string UnconfirmedBalance { get; set; }
        public Type NodeEventType { get; } = typeof(FederationWalletStatusEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is FederationWalletStatusEvent federationWalletStatusEvent)
            {
                this.ConfirmedBalance = federationWalletStatusEvent.ConfirmedBalance.ToString();
                this.UnconfirmedBalance = federationWalletStatusEvent.UnconfirmedBalance.ToString();
                return;
            }

            throw new NotImplementedException();
        }
    }
}
