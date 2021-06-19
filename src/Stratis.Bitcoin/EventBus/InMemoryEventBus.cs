using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.EventBus
{
    public class InMemoryEventBus : IEventBus
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>
        /// The subscriber error handler
        /// </summary>
        private readonly ISubscriptionErrorHandler subscriptionErrorHandler;

        /// <summary>
        /// The subscriptions stored by EventType
        /// </summary>
        private readonly Dictionary<Type, List<ISubscription>> subscriptions;

        /// <summary>
        /// The subscriptions lock to prevent race condition during publishing
        /// </summary>
        private readonly object subscriptionsLock = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemoryEventBus"/> class.
        /// </summary>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="subscriptionErrorHandler">The subscription error handler. If null the default one will be used</param>
        public InMemoryEventBus(ILoggerFactory loggerFactory, ISubscriptionErrorHandler subscriptionErrorHandler)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.subscriptionErrorHandler = subscriptionErrorHandler ?? new DefaultSubscriptionErrorHandler(loggerFactory);
            this.subscriptions = new Dictionary<Type, List<ISubscription>>();
        }

        private static ConcurrentDictionary<Guid, (long executionTimesTicks, MethodInfo methodCalled)> blockConnectedStatistics = new ConcurrentDictionary<Guid, (long, MethodInfo)>();
        private static int blockConnectedStatisticsCount = 0;

        /// <inheritdoc />
        public SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : EventBase
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            lock (this.subscriptionsLock)
            {
                if (!this.subscriptions.ContainsKey(typeof(TEvent)))
                {
                    this.subscriptions.Add(typeof(TEvent), new List<ISubscription>());
                }

                var subscriptionToken = new SubscriptionToken(this, typeof(TEvent));
                this.subscriptions[typeof(TEvent)].Add(new Subscription<TEvent>(handler, subscriptionToken));

                return subscriptionToken;
            }
        }

        /// <inheritdoc />
        public void Unsubscribe(SubscriptionToken subscriptionToken)
        {
            // Ignore null token
            if (subscriptionToken == null)
            {
                this.logger.LogDebug("Unsubscribe called with a null token, ignored.");
                return;
            }

            lock (this.subscriptionsLock)
            {
                if (this.subscriptions.ContainsKey(subscriptionToken.EventType))
                {
                    var allSubscriptions = this.subscriptions[subscriptionToken.EventType];

                    var subscriptionToRemove = allSubscriptions.FirstOrDefault(sub => sub.SubscriptionToken.Token == subscriptionToken.Token);
                    if (subscriptionToRemove != null)
                        this.subscriptions[subscriptionToken.EventType].Remove(subscriptionToRemove);
                }
            }
        }

        /// <inheritdoc />
        public void Publish<TEvent>(TEvent @event) where TEvent : EventBase
        {
            if (@event == null)
                throw new ArgumentNullException(nameof(@event));

            List<ISubscription> allSubscriptions = new List<ISubscription>();
            lock (this.subscriptionsLock)
            {
                if (this.subscriptions.ContainsKey(typeof(TEvent)))
                    allSubscriptions = this.subscriptions[typeof(TEvent)].ToList();
            }

            if (allSubscriptions.Count == 0)
                return;

            for (var index = 0; index < allSubscriptions.Count; index++)
            {
                var subscription = allSubscriptions[index];
                long flagFall = DateTime.Now.Ticks;
                try
                {
                    subscription.Publish(@event);
                }
                catch (Exception ex)
                {
                    this.subscriptionErrorHandler?.Handle(@event, ex, subscription);
                }
                finally
                {
                    if (typeof(TEvent) == typeof(BlockConnected))
                    {
                        long elapsed = DateTime.Now.Ticks - flagFall;
                        var actionField = subscription.GetType().GetField("action", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var action = actionField.GetValue(subscription);
                        if (!blockConnectedStatistics.TryGetValue(subscription.SubscriptionToken.Token, out (long totalElapsedTicks, MethodInfo) stats))
                            stats = (0, ((Action<TEvent>)action).Method);
                        stats.totalElapsedTicks += elapsed;
                        blockConnectedStatistics[subscription.SubscriptionToken.Token] = stats;
                    }
                }
            }

            if (typeof(TEvent) == typeof(BlockConnected))
                blockConnectedStatisticsCount++;
        }

        public static string GetBenchStats()
        {
            var builder = new StringBuilder();

            builder.AppendLine();
            builder.AppendLine(">> Signals Bench");

            int nIterations = blockConnectedStatisticsCount;
            if (nIterations == 0)
            {
                builder.AppendLine("No samples...");
                return builder.ToString();
            }

            long totalTimesTicks = 0;
            foreach (var statistics in blockConnectedStatistics)
            {
                (long executionTimesTicks, _) = statistics.Value;
                totalTimesTicks += executionTimesTicks;
            }

            builder.AppendLine($"\"OnBlockConnected\" block processing . Average total execution time: {Math.Round((new TimeSpan(totalTimesTicks / nIterations)).TotalMilliseconds, 4)} ms.");

            foreach (var statistics in blockConnectedStatistics)
            {
                (long executionTimesTicks, MethodInfo methodCalled) = statistics.Value;

                string methodName = $"{methodCalled.DeclaringType.Name}.{methodCalled.Name}";

                double avgExecutionTimeMs = Math.Round((TimeSpan.FromTicks(executionTimesTicks / nIterations).TotalMilliseconds), 4);

                // % from average execution time for the group.
                double percentage = Math.Round(((double)executionTimesTicks / totalTimesTicks) * 100.0);

                builder.AppendLine($"    {methodName.PadRight(60, '-')}{(avgExecutionTimeMs + " ms").PadRight(12, '-')}{percentage} %");
            }

            return builder.ToString();
        }
    }
}