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
        Task<string> EstimateConversionTransactionFeeAsync(CancellationToken cancellation = default);
    }

    /// <inheritdoc/>
    public sealed class ExternalApiClient : RestApiClientBase, IExternalApiClient
    {
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
