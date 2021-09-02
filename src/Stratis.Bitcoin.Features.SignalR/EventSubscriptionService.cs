using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Signals;

namespace Stratis.Bitcoin.Features.SignalR
{
    /// <summary>
    /// This class subscribes to Stratis.Bitcoin.EventBus messages and proxy's them
    /// to SignalR messages.
    /// </summary>
    public class EventSubscriptionService : IEventsSubscriptionService, IDisposable
    {
        private readonly SignalROptions options;
        private readonly ISignals signals;
        private readonly EventsHub eventsHub;
        private readonly ILogger logger;
        private readonly List<SubscriptionToken> subscriptions = new List<SubscriptionToken>();

        public EventSubscriptionService(
            SignalROptions options,
            ISignals signals,
            EventsHub eventsHub)
        {
            this.options = options;
            this.signals = signals;
            this.eventsHub = eventsHub;
            this.logger = LogManager.GetCurrentClassLogger();
        }

        public void Init()
        {
            foreach (IClientEvent eventToHandle in this.options.EventsToHandle)
            {
                this.logger.Debug("Create subscription for {0}", eventToHandle.NodeEventType);

                async Task callback(EventBase eventBase)
                {
                    Type childType = eventBase.GetType();

                    IClientEvent clientEvent = this.options.EventsToHandle.FirstOrDefault(ev => ev.NodeEventType == childType);
                    if (clientEvent == null)
                        return;

                    clientEvent.BuildFrom(eventBase);

                    await this.eventsHub.SendToClientsAsync(clientEvent).ConfigureAwait(false);
                }

                this.signals.Subscribe(eventToHandle.NodeEventType, callback);
            }
        }

        public void Dispose()
        {
            this.eventsHub?.Dispose();
            this.subscriptions.ForEach(s => s?.Dispose());
        }
    }
}