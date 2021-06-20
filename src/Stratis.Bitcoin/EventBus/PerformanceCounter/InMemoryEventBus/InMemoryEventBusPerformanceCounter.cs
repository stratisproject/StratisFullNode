using System;
using Stratis.Bitcoin.Utilities;
using TracerAttributes;

namespace Stratis.Bitcoin.EventBus.PerformanceCounters.InMemoryEventBus
{
    public class InMemoryEventBusPerformanceCounter
    {
        /// <summary>Snapshot that is currently being populated.</summary>
        private InMemoryEventBusPerformanceSnapshot currentSnapshot;

        public InMemoryEventBusPerformanceCounter()
        {
            this.currentSnapshot = new InMemoryEventBusPerformanceSnapshot();
        }

        /// <summary>
        /// Measures time to execute <c>OnPartialValidationSucceededAsync</c>.
        /// </summary>
        [NoTrace]
        public IDisposable MeasureEventExecutionTime<TEvent>(ISubscription subscription) where TEvent : EventBase
        {
            return new StopwatchDisposable((elapsed) => this.currentSnapshot.GetExecutionsCountAndDelay<TEvent>(subscription).Increment(elapsed));
        }

        /// <summary>Takes current snapshot.</summary>
        /// <remarks>Not thread-safe. Caller should ensure that it's not called from different threads at once.</remarks>
        [NoTrace]
        public InMemoryEventBusPerformanceSnapshot TakeSnapshot()
        {
            var newSnapshot = new InMemoryEventBusPerformanceSnapshot();
            InMemoryEventBusPerformanceSnapshot previousSnapshot = this.currentSnapshot;
            this.currentSnapshot = newSnapshot;

            return previousSnapshot;
        }
    }
}
