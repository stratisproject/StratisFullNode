using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Features.OpenBanking.TokenMinter;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.OpenBanking
{
    /// <summary>
    /// Responsible for managing the OpenBanking feature.
    /// </summary>
    public class OpenBankingFeature : FullNodeFeature
    {
        /// <summary>
        /// Defines a back-off interval in milliseconds if the Token Minting service fails whilst operating, before it restarts.
        /// </summary>
        public const int TokenMintingBackoffInterval = 2000;
        public const int TokenMintingSleepInterval = 30000;

        /// <summary>
        /// Defines the logger.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// Defines the node lifetime.
        /// </summary>
        private readonly INodeLifetime nodeLifetime;

        private readonly IMetadataTracker metadataTracker;

        /// <summary>
        /// Defines the long running task used to support the Token Minting Service.
        /// </summary>
        private Task mintingTask;

        private readonly ITokenMintingService tokenMintingService;

        /// <summary>
        /// Defines a flag used to indicate whether the object has been disposed or not.
        /// </summary>
        private bool disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenBankingFeature"/> class.
        /// </summary>
        /// <param name="loggerFactory">The factory to create the logger.</param>
        /// <param name="nodeLifetime">The node lifetime object used for graceful shutdown.</param>
        /// <param name="metadataTracker">The meta-data tracker.</param>
        /// <param name="network">The network.</param>
        /// <param name="tokenMintingService">The token minting service.</param>
        public OpenBankingFeature(ILoggerFactory loggerFactory, INodeLifetime nodeLifetime, IMetadataTracker metadataTracker, Network network, ITokenMintingService tokenMintingService, DataFolder dataFolder)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(nodeLifetime, nameof(nodeLifetime));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.nodeLifetime = nodeLifetime;
            this.metadataTracker = metadataTracker;
            this.tokenMintingService = tokenMintingService;
        }

        /// <summary>
        /// Initializes the Open Banking feature.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        public override Task InitializeAsync()
        {
            this.logger.LogInformation("Starting Open Banking server...");

            this.metadataTracker.Initialize();

            // Create long running task for minting service.
            this.mintingTask = Task.Factory.StartNew(this.RunMintingService, TaskCreationOptions.LongRunning);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Runs the Minting service until the node is stopped.
        /// </summary>
        private void RunMintingService()
        {
            // Initialize Token Minting Service.
            this.tokenMintingService.Initialize();

            while (true)
            {
                try
                {
                    this.logger.LogInformation("Starting Token Minting Service");

                    // Start.
                    this.tokenMintingService.RunAsync(this.nodeLifetime.ApplicationStopping).GetAwaiter().GetResult();

                    // Sleep.
                    Task.Delay(TokenMintingSleepInterval, this.nodeLifetime.ApplicationStopping).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    // Node shutting down, expected.
                    this.logger.LogInformation("Stopping Token Minting Service");
                    break;
                }
                catch (Exception e)
                {
                    this.logger.LogError(e, "Failed whilst running the Token Minting Service with: {0}", e.Message);

                    try
                    {
                        // Back-off before restart.
                        Task.Delay(TokenMintingBackoffInterval, this.nodeLifetime.ApplicationStopping).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        // Node shutting down, expected.
                        this.logger.LogInformation("Stopping Token Minting Service");
                        break;
                    }

                    this.logger.LogDebug("Restarting Token Minting Service following previous failure.");
                }
            }
        }

        /// <summary>
        /// Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            BaseSettings.PrintHelp(typeof(OpenBankingSettings), network);
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            BaseSettings.BuildDefaultConfigurationFile(typeof(OpenBankingSettings), builder, network);
        }

        /// <summary>
        /// Disposes of the object.
        /// </summary>
        public override void Dispose()
        {
            this.logger.LogInformation("Stopping Open Banking server...");

            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of the object.
        /// </summary>
        /// <param name="disposing"><c>true</c> if the object is being disposed of deterministically, otherwise <c>false</c>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    // Do stuff here.
                }

                this.disposed = true;
            }
        }
    }
}