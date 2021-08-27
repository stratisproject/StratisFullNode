using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TracerAttributes;

namespace Stratis.Bitcoin.EventBus.PerformanceCounters.InMemoryEventBus
{
    /// <summary>Snapshot of <see cref="ConsensusManager"/> performance.</summary>
    public class InMemoryEventBusPerformanceSnapshot
    {
        public ExecutionsCountAndDelay GetExecutionsCountAndDelay<TEvent>(ISubscription subscription)
        {
            var actionField = subscription.GetType().GetField("action", BindingFlags.NonPublic | BindingFlags.Instance);
            var action = actionField.GetValue(subscription);

            MethodInfo methodInfo = null;
            if (action is Action<TEvent> actionEvent)
                methodInfo = actionEvent.Method;

            if (action is Func<EventBase, Task> actionFunc)
                methodInfo = actionFunc.Method;

            if (!this.EventExecutionTime.TryGetValue(typeof(TEvent), out ConcurrentDictionary<MethodInfo, ExecutionsCountAndDelay> eventExecutionCountsAndDelay))
            {
                eventExecutionCountsAndDelay = new ConcurrentDictionary<MethodInfo, ExecutionsCountAndDelay>();
                this.EventExecutionTime[typeof(TEvent)] = eventExecutionCountsAndDelay;
            }

            if (!eventExecutionCountsAndDelay.TryGetValue(methodInfo, out ExecutionsCountAndDelay executionsCountAndDelay))
            {
                executionsCountAndDelay = new ExecutionsCountAndDelay();
                eventExecutionCountsAndDelay[methodInfo] = executionsCountAndDelay;
            }

            return executionsCountAndDelay;
        }

        public ConcurrentDictionary<Type, ConcurrentDictionary<MethodInfo, ExecutionsCountAndDelay>> EventExecutionTime { get; }

        public InMemoryEventBusPerformanceSnapshot()
        {
            this.EventExecutionTime = new ConcurrentDictionary<Type, ConcurrentDictionary<MethodInfo, ExecutionsCountAndDelay>>();
        }

        public string GetEventStats(Type eventType)
        {
            var builder = new StringBuilder();
            long totalDelayTicks = 0;
            double avgTotalExecutionTime = 0;

            ConcurrentDictionary<MethodInfo, ExecutionsCountAndDelay> eventExecutionTime = null;

            if (this.EventExecutionTime.ContainsKey(eventType))
            {
                eventExecutionTime = this.EventExecutionTime[eventType];

                // Get average execution time for group.
                foreach ((MethodInfo methodInfo, ExecutionsCountAndDelay executionsCountAndDelay) in eventExecutionTime)
                {
                    totalDelayTicks += executionsCountAndDelay.GetTotalDelayTicks();
                    avgTotalExecutionTime += executionsCountAndDelay.GetAvgExecutionTimeMs();
                }
            }

            if (totalDelayTicks == 0)
            {
                builder.AppendLine($"\"{eventType.Name}\" has no samples...");
                return builder.ToString();
            }

            builder.AppendLine($"\"{eventType.Name}\" average total execution time: {Math.Round(avgTotalExecutionTime, 4)} ms.");

            foreach ((MethodInfo methodInfo, ExecutionsCountAndDelay executionsCountAndDelay) in eventExecutionTime)
            {
                string methodName = $"{methodInfo.DeclaringType.Name}.{methodInfo.Name}";

                double avgExecutionTimeMs = Math.Round(executionsCountAndDelay.GetAvgExecutionTimeMs(), 4);

                // % from average execution time for the group.
                double percentage = Math.Round(((double)executionsCountAndDelay.GetTotalDelayTicks() / totalDelayTicks) * 100.0);

                builder.AppendLine($"    {methodName.PadRight(60, '-')}{(avgExecutionTimeMs + " ms").PadRight(12, '-')}{percentage} %");
            }

            return builder.ToString();
        }

        [NoTrace]
        public override string ToString()
        {
            var builder = new StringBuilder();

            builder.AppendLine(">> Signals Bench");

            if (this.EventExecutionTime.Keys.Count == 0)
            {
                builder.AppendLine("No samples...");
            }
            else
            {
                foreach (Type type in this.EventExecutionTime.Keys)
                {
                    builder.AppendLine(this.GetEventStats(type));
                }
            }

            return builder.ToString();
        }
    }

    public class ExecutionsCountAndDelay
    {
        private int totalExecutionsCount;
        private long totalDelayTicks;

        public ExecutionsCountAndDelay()
        {
            this.totalExecutionsCount = 0;
            this.totalDelayTicks = 0;
        }

        public int GetTotalExecutionsCount()
        {
            return this.totalExecutionsCount;
        }

        public long GetTotalDelayTicks()
        {
            return this.totalDelayTicks;
        }

        public double GetAvgExecutionTimeCountMin()
        {
            if (this.totalDelayTicks == 0)
                return 0;

            return Math.Round(this.totalExecutionsCount / TimeSpan.FromTicks(this.totalDelayTicks).TotalMinutes, 4);
        }

        public double GetAvgExecutionTimeMs()
        {
            if (this.totalExecutionsCount == 0)
                return 0;

            return Math.Round(TimeSpan.FromTicks(this.totalDelayTicks).TotalMilliseconds / this.totalExecutionsCount, 4);
        }

        public void Increment(long elapsedTicks)
        {
            Interlocked.Increment(ref this.totalExecutionsCount);
            Interlocked.Add(ref this.totalDelayTicks, elapsedTicks);
        }
    }
}
