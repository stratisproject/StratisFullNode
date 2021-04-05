using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.PoA.Collateral.CounterChain;

namespace Stratis.Features.FederatedPeg.Controllers
{
    /// <summary>Rest client for <see cref="FederationGatewayController"/>.</summary>
    public interface IFederationGatewayClient : IRestApiClientBase
    {
        /// <summary><see cref="FederationGatewayController.GetMaturedBlockDeposits"/></summary>
        /// <param name="blockHeight">Last known block height at which to retrieve from.</param>
        /// <param name="cancellation">Cancellation Token.</param>
        Task<SerializableResult<List<MaturedBlockDepositsModel>>> GetMaturedBlockDepositsAsync(int blockHeight, CancellationToken cancellation = default);
    }

    /// <inheritdoc cref="IFederationGatewayClient"/>
    public class FederationGatewayClient : RestApiClientBase, IFederationGatewayClient
    {
        /// <summary>
        /// Currently the <paramref name="url"/> is required as it needs to be configurable for testing.
        /// <para>
        /// In a production/live scenario the sidechain and mainnet federation nodes should run on the same machine.
        /// </para>
        /// </summary>
        public FederationGatewayClient(ICounterChainSettings counterChainSettings, IHttpClientFactory httpClientFactory)
            : base(httpClientFactory, counterChainSettings.CounterChainApiPort, "FederationGateway", $"http://{counterChainSettings.CounterChainApiHost}")
        {
        }

        /// <inheritdoc />
        public Task<SerializableResult<List<MaturedBlockDepositsModel>>> GetMaturedBlockDepositsAsync(int height, CancellationToken cancellation = default)
        {
            return this.SendGetRequestAsync<SerializableResult<List<MaturedBlockDepositsModel>>>(FederationGatewayRouteEndPoint.GetMaturedBlockDeposits, $"blockHeight={height}", cancellation);
        }
    }
}