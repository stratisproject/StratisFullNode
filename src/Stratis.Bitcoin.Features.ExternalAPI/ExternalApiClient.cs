using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stratis.Bitcoin.Controllers;
using Stratis.Features.ExternalApi.Controllers;
using Stratis.Features.PoA.Collateral.CounterChain;

namespace Stratis.Bitcoin.Features.ExternalApi
{
    /// <summary>
    /// Used for retrieving data from the External API controller when only HTTP access to the node running it is available.
    /// This is necessary because having multiple nodes running the poller on the same machine can cause rate limiting issues.
    /// </summary>
    public interface IExternalApiClient
    {
        /// <summary>
        /// Estimates the conversion fee (in STRAX).
        /// </summary>
        /// <param name="cancellation"><see cref="CancellationToken"/>.</param>
        /// <returns>The conversion fee.</returns>
        Task<string> EstimateConversionTransactionFeeAsync(CancellationToken cancellation = default);
    }

    /// <inheritdoc/>
    public sealed class ExternalApiClient : RestApiClientBase, IExternalApiClient
    {
        /// <summary>
        /// The class constructor.
        /// </summary>
        /// <param name="counterChainSettings">The <see cref="ICounterChainSettings"/>.</param>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        public ExternalApiClient(ICounterChainSettings counterChainSettings, IHttpClientFactory httpClientFactory)
            : base(httpClientFactory, counterChainSettings.CounterChainApiPort, "ExternalApi", $"http://{counterChainSettings.CounterChainApiHost}")
        {
        }

        /// <inheritdoc/>
        public async Task<string> EstimateConversionTransactionFeeAsync(CancellationToken cancellation = default)
        {
            return await this.SendGetRequestAsync<string>(ExternalApiController.EstimateConversionFeeEndpoint, cancellation: cancellation).ConfigureAwait(false);
        }
    }
}
