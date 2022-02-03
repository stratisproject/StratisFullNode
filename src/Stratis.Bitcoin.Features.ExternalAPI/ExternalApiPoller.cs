using System;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.ExternalApi.ApiClients;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.ExternalApi
{
    /// <summary>
    /// External API poller.
    /// </summary>
    public interface IExternalApiPoller : IDisposable
    {
        /// <summary>
        /// Initialize the instance.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Gets the Stratis price.
        /// </summary>
        /// <returns>The Stratis price.</returns>
        decimal GetStratisPrice();

        /// <summary>
        /// Gets the Ethereum price.
        /// </summary>
        /// <returns>The Ethereum price.</returns>
        decimal GetEthereumPrice();

        /// <summary>
        /// Gets the gas price.
        /// </summary>
        /// <returns>The gas price.</returns>
        int GetGasPrice();

        /// <summary>Estimates the total conversion gas costs.</summary>
        /// <returns>The estimated total amount of gas a conversion transaction will require.</returns>
        /// <remarks>The decimal type is acceptable here because it supports sufficiently large numbers for most conceivable gas calculations.</remarks>
        decimal EstimateConversionTransactionGas();

        /// <summary>
        /// Estimates the conversion transaction fee.
        /// </summary>
        /// <returns>The estimated conversion transaction fee, converted from the USD total to the equivalent STRAX amount.</returns>
        decimal EstimateConversionTransactionFee();
    }

    /// <inheritdoc/>
    public class ExternalApiPoller : IExternalApiPoller
    {
        /// <summary>
        /// The quorum size.
        /// </summary>
        // TODO: This should be linked to the setting in the interop feature
        public const int QuorumSize = 6;

        /// <summary>
        /// One Ether.
        /// </summary>
        public const decimal OneEther = 1_000_000_000_000_000_000;

        /// <summary>
        /// One Gwei.
        /// </summary>
        public const decimal OneGwei = 1_000_000_000;

        private readonly IAsyncProvider asyncProvider;
        private readonly INodeLifetime nodeLifetime;
        private readonly ILogger logger;
        private readonly ExternalApiSettings externalApiSettings;
        private readonly EtherscanClient etherscanClient;
        private readonly CoinGeckoClient coinGeckoClient;

        private IAsyncLoop gasPriceLoop;
        private IAsyncLoop priceLoop;

        /// <summary>
        /// The instance constructor.
        /// </summary>
        /// <param name="nodeSettings">The <see cref="NodeSettings"/>.</param>
        /// <param name="asyncProvider">The <see cref="IAsyncProvider"/>.</param>
        /// <param name="nodeLifetime">The <see cref="INodeLifetime"/>.</param>
        /// <param name="externalApiSettings">The <see cref="ExternalApiSettings"/>.</param>
        public ExternalApiPoller(NodeSettings nodeSettings,
            IAsyncProvider asyncProvider,
            INodeLifetime nodeLifetime,
            ExternalApiSettings externalApiSettings)
        {
            this.asyncProvider = asyncProvider;
            this.nodeLifetime = nodeLifetime;
            this.logger = nodeSettings.LoggerFactory.CreateLogger(this.GetType().FullName);
            this.externalApiSettings = externalApiSettings;
            this.etherscanClient = new EtherscanClient(this.externalApiSettings);
            this.coinGeckoClient = new CoinGeckoClient(this.externalApiSettings);
        }

        /// <summary>
        /// Inializes the instance.
        /// </summary>
        public void Initialize()
        {
            this.logger.LogInformation($"External API feature enabled, initializing periodic loops.");

            if (this.externalApiSettings.EthereumGasPriceTracking)
            {
                this.logger.LogInformation($"Ethereum gas price tracking enabled.");

                this.gasPriceLoop = this.asyncProvider.CreateAndRunAsyncLoop("PeriodicCheckGasPrice", async (cancellation) =>
                {
                    this.logger.LogTrace("Beginning gas price check loop.");

                    try
                    {
                        await this.etherscanClient.GasOracleAsync().ConfigureAwait(false);
                    }
                    catch (HttpRequestException e2) when (e2.InnerException is SocketException socketException && socketException.ErrorCode == 11001)
                    {
                        this.logger.LogWarning("Unable to contact gas oracle. Are you offline?");
                    }
                    catch (Exception e)
                    {
                        this.logger.LogWarning("Exception raised when checking current gas price. {0}", e);
                    }

                    this.logger.LogTrace("Finishing gas price check loop.");
                },
                this.nodeLifetime.ApplicationStopping,
                repeatEvery: TimeSpans.Minute,
                startAfter: TimeSpans.Second);
            }

            if (this.externalApiSettings.PriceTracking)
            {
                this.logger.LogInformation($"Price tracking for STRAX and ETH enabled.");

                this.priceLoop = this.asyncProvider.CreateAndRunAsyncLoop("PeriodicCheckPrice", async (cancellation) =>
                {
                    this.logger.LogTrace("Beginning price check loop.");

                    try
                    {
                        await this.coinGeckoClient.PriceDataRetrievalAsync().ConfigureAwait(false);
                    }
                    catch (HttpRequestException e2) when (e2.InnerException is SocketException socketException && socketException.ErrorCode == 11001)
                    {
                        this.logger.LogWarning("Unable to retrieve price data. Are you offline?");
                    }
                    catch (Exception e)
                    {
                        this.logger.LogWarning("Exception raised when checking current prices. {0}", e);
                    }

                    this.logger.LogTrace("Finishing price check loop.");
                },
                this.nodeLifetime.ApplicationStopping,
                repeatEvery: TimeSpans.Minute,
                startAfter: TimeSpans.Second);
            }
        }

        /// <inheritdoc/>
        public decimal GetStratisPrice()
        {
            return this.coinGeckoClient.GetStratisPrice();
        }

        /// <inheritdoc/>
        public decimal GetEthereumPrice()
        {
            return this.coinGeckoClient.GetEthereumPrice();
        }

        /// <inheritdoc/>
        public int GetGasPrice()
        {
            return this.etherscanClient.GetGasPrice();
        }

        /// <inheritdoc/>
        public decimal EstimateConversionTransactionGas()
        {
            // The cost of submitting a multisig ERC20 transfer to the multisig contract.
            const decimal SubmissionGasCost = 230_000;

            // The cost of submitting a confirmation transaction to the multisig contract.
            const decimal ConfirmGasCost = 100_000;

            // The final confirmation that meets the contract threshold; this incurs slightly higher gas due to the transaction execution occurring as well.
            const decimal ExecuteGasCost = 160_000;

            // Of the required number of confirmations, one confirmation comes from the initial submission, and the final confirmation is more expensive.
            decimal totalGas = SubmissionGasCost + ((QuorumSize - 2) * ConfirmGasCost) + ExecuteGasCost;

            int gasPrice = this.GetGasPrice();

            // Need to convert the integral gas price into its gwei equivalent so that the right amount of gas is computed here.
            return totalGas * gasPrice * OneGwei;
        }

        /// <inheritdoc/>
        public decimal EstimateConversionTransactionFee()
        {
            // The approximate USD fee that will be applied to conversion transactions, over and above the computed gas cost.
            const decimal ConversionTransactionFee = 220;

            decimal ethereumUsdPrice = this.GetEthereumPrice();

            if (ethereumUsdPrice == -1)
                return -1;

            decimal overallGasUsd = (this.EstimateConversionTransactionGas() / OneEther) * ethereumUsdPrice;

            decimal stratisPriceUsd = this.GetStratisPrice();

            if (stratisPriceUsd == -1)
                return -1;

            return (overallGasUsd / stratisPriceUsd) + (ConversionTransactionFee / stratisPriceUsd);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.gasPriceLoop?.Dispose();
            this.priceLoop?.Dispose();
        }
    }
}
