using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.PoA.Collateral.CounterChain;

namespace Stratis.Features.FederatedPeg.Controllers
{
    public interface IFederationNodeClient : IRestApiClientBase
    {
        /// <summary><see cref="FederationGatewayController.GetMaturedBlockDeposits"/></summary>
        /// <param name="depositId">The deposit to retrieve verbose transaction information for.</param>
        /// <param name="cancellation">Cancellation Token.</param>
        Task<TransactionVerboseModel> GetDepositTransactionVerboseAsync(uint256 depositId, CancellationToken cancellation = default);
    }

    /// <inheritdoc cref="IFederationNodeClient"/>
    public class FederationNodeClient : RestApiClientBase, IFederationNodeClient
    {
        /// <summary>
        /// Currently the <paramref name="url"/> is required as it needs to be configurable for testing.
        /// <remarks>
        /// In a production/live scenario the sidechain and mainnet federation nodes should run on the same machine.
        /// </remarks>
        /// </summary>
        public FederationNodeClient(ICounterChainSettings counterChainSettings, IHttpClientFactory httpClientFactory)
            : base(httpClientFactory, counterChainSettings.CounterChainApiPort, "Node", $"http://{counterChainSettings.CounterChainApiHost}")
        {
        }

        public Task<TransactionVerboseModel> GetDepositTransactionVerboseAsync(uint256 depositId, CancellationToken cancellation = default)
        {
            return this.SendGetRequestAsync<TransactionVerboseModel>("getrawtransaction", $"trxid={depositId}&verbose=true", cancellation);
        }
    }
}
