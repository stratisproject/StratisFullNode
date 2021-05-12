using System.Threading;
using Microsoft.Extensions.Logging;

namespace Stratis.Bitcoin.Utilities
{
    /// <summary>
    /// Represents a resource that can only be held by one thread at a time.
    /// </summary>
    public class SingleThreadResource
    {
        private object lockObj;
        private int resourceOwner = -1;
        private ILogger logger;
        private string name;

        public SingleThreadResource(string name, ILogger logger)
        {
            this.name = name;
            this.logger = logger;
            this.lockObj = new object();
        }

        public bool Wait()
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;

            bool logged = false;

            while (this.resourceOwner != threadId)
            {
                lock (this.lockObj)
                {
                    if (this.resourceOwner == -1)
                    {
                        this.resourceOwner = threadId;
                        break;
                    }
                }

                if (!logged)
                {
                    this.logger.LogDebug("Thread {0} is waiting to acquire '{1}' held by thread {2}.", threadId, this.name, this.resourceOwner);
                    logged = true;
                }

                Thread.Yield();
            }

            this.logger.LogDebug("Thread {0} acquired lock '{1}'.", threadId, this.name);

            return true;
        }

        public bool IsHeld()
        {
            return this.resourceOwner == Thread.CurrentThread.ManagedThreadId;
        }

        public void Release()
        {
            lock (this.lockObj)
            {
                Guard.Assert(this.IsHeld());

                this.logger.LogDebug("Thread {0} released lock '{1}'.", this.resourceOwner, this.name);

                this.resourceOwner = -1;
            }
        }
    }
}
