using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            MethodInfo subscribeMethod = this.signals.GetType().GetMethod("Subscribe");
            MethodInfo onEventCallbackMethod = typeof(EventSubscriptionService).GetMethod("OnEvent");
            foreach (IClientEvent eventToHandle in this.options.EventsToHandle)
            {
                this.logger.Debug("Create subscription for {0}", eventToHandle.NodeEventType);
                MethodInfo subscribeMethodInfo = subscribeMethod.MakeGenericMethod(eventToHandle.NodeEventType);
                Type callbackType = typeof(Action<>).MakeGenericType(eventToHandle.NodeEventType);
                Delegate onEventDelegate = Delegate.CreateDelegate(callbackType, this, onEventCallbackMethod);

                var token = (SubscriptionToken)subscribeMethodInfo.Invoke(this.signals, new object[] { onEventDelegate });
                this.subscriptions.Add(token);
            }
        }

        /// <summary> This is invoked through reflection.</summary>
        public void OnEvent(EventBase @event)
        {
            Type childType = @event.GetType();
            IClientEvent clientEvent = this.options.EventsToHandle.FirstOrDefault(ev => ev.NodeEventType == childType);
            if (clientEvent == null) return;
            clientEvent.BuildFrom(@event);
            this.eventsHub.SendToClientsAsync(clientEvent).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            this.eventsHub?.Dispose();
            this.subscriptions.ForEach(s => s?.Dispose());
        }
    }
}