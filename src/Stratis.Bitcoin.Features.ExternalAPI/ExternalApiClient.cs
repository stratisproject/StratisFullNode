using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.ExternalApi.Controllers;

namespace Stratis.Bitcoin.Features.ExternalApi
{
    /// <summary>
    /// Used for retrieving data from the External API controller when only HTTP access to the node running it is available.
    /// This is necessary because having multiple nodes running the poller on the same machine can cause rate limiting issues.
    /// </summary>
    public class ExternalApiClient : RestApiClientBase
    {
        public ExternalApiClient(string apiHost, int apiPort, IHttpClientFactory httpClientFactory)
            : base(httpClientFactory, apiPort, "ExternalApi", $"http://{apiHost}")
        {
        }

        public Task<SerializableResult<string>> EstimateConversionTransactionFeeAsync(CancellationToken cancellation = default)
        {
            return this.SendGetRequestAsync<SerializableResult<string>>(ExternalApiController.EstimateConversionFeeEndpoint, cancellation: cancellation);
        }
    }
}
