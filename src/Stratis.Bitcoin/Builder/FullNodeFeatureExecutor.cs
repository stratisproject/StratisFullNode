using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Builder
{
    /// <summary>
    /// Starts and stops all features registered with a full node.
    /// </summary>
    public interface IFullNodeFeatureExecutor : IDisposable
    {
        /// <summary>
        /// Starts all registered features of the associated full node.
        /// </summary>
        void Initialize();
    }

    /// <summary>
    /// Starts and stops all features registered with a full node.
    /// </summary>
    /// <remarks>Borrowed from ASP.NET.</remarks>
    public class FullNodeFeatureExecutor : IFullNodeFeatureExecutor
    {
        /// <summary>Full node which features are to be managed by this executor.</summary>
        private readonly IFullNode fullNode;

        /// <summary>Object logger.</summary>
        private readonly ILogger logger;

        /// <summary>Provides event publishing services to the node.</summary>
        private readonly ISignals signals;

        /// <summary>
        /// Initializes an instance of the object with specific full node and logger factory.
        /// </summary>
        /// <param name="fullNode">Full node which features are to be managed by this executor.</param>
        public FullNodeFeatureExecutor(IFullNode fullNode, ISignals signals)
        {
            Guard.NotNull(fullNode, nameof(fullNode));

            this.fullNode = fullNode;
            this.signals = signals;
            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <inheritdoc />
        public void Initialize()
        {
            try
            {
                this.Execute(feature => feature.ValidateDependencies(this.fullNode.Services));

                this.Execute(feature =>
                {
                    this.signals.Publish(new FullNodeEvent() { Message = $"Initializing feature '{feature.GetType().Name}' ... " });
                    feature.State = FeatureInitializationState.Initializing;
                    feature.InitializeAsync().GetAwaiter().GetResult();
                    feature.State = FeatureInitializationState.Initialized;
                    this.signals.Publish(new FullNodeEvent() { Message = $"Feature '{feature.GetType().Name}' initialized ... " });
                });
            }
            catch
            {
                this.logger.Error("An error occurred starting the application.");
                this.logger.Trace("(-)[INITIALIZE_EXCEPTION]");
                throw;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                this.Execute(feature =>
                {
                    feature.State = FeatureInitializationState.Disposing;
                    feature.Dispose();
                    feature.State = FeatureInitializationState.Disposed;
                }, true);
            }
            catch
            {
                this.logger.Error("An error occurred stopping the application.");
                this.logger.Trace("(-)[DISPOSE_EXCEPTION]");
                throw;
            }
        }

        /// <summary>
        /// Executes start or stop method of all the features registered with the associated full node.
        /// </summary>
        /// <param name="callback">Delegate to run start or stop method of the feature.</param>
        /// <param name="disposing">Reverse the order of which the features are executed.</param>
        /// <exception cref="AggregateException">Thrown in case one or more callbacks threw an exception.</exception>
        private void Execute(Action<IFullNodeFeature> callback, bool disposing = false)
        {
            if (this.fullNode.Services == null)
            {
                this.logger.Trace("(-)[NO_SERVICES]");
                return;
            }

            List<Exception> exceptions = null;

            if (disposing)
            {
                // When the node is shutting down, we need to dispose all features, so we don't break on exception.
                foreach (IFullNodeFeature feature in this.fullNode.Services.Features.Reverse())
                {
                    try
                    {
                        callback(feature);
                    }
                    catch (Exception exception)
                    {
                        if (exceptions == null)
                            exceptions = new List<Exception>();

                        this.LogAndAddException(feature, disposing, exceptions, exception);
                    }
                }
            }
            else
            {
                // Initialize features that are flagged to start before the base feature.
                foreach (IFullNodeFeature feature in this.fullNode.Services.Features.OrderByDescending(f => f.InitializeBeforeBase))
                {
                    try
                    {
                        callback(feature);
                    }
                    catch (Exception exception)
                    {
                        if (exceptions == null)
                            exceptions = new List<Exception>();

                        this.LogAndAddException(feature, disposing, exceptions, exception);

                        // When the node is starting we don't continue initialization when an exception occurs.
                        break;
                    }
                }
            }

            // Throw an aggregate exception if there were any exceptions.
            if (exceptions != null)
            {
                this.logger.Trace("(-)[EXECUTION_FAILED]");
                throw new AggregateException(exceptions);
            }
        }

        private void LogAndAddException(IFullNodeFeature feature, bool disposing, List<Exception> exceptions, Exception exception)
        {
            exceptions.Add(exception);

            var messageText = disposing ? "disposing" : "starting";
            var exceptionText = $"An error occurred {messageText} full node feature '{feature.GetType().Name}': '{exception}'";

            this.logger.Error(exceptionText);
            this.signals.Publish(new FullNodeEvent() { Message = exceptionText });
        }
    }
}
