using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stratis.Bitcoin.Base.Deployments.Models;
using Stratis.Bitcoin.Controllers;
using Stratis.Features.PoA.Collateral.CounterChain;

namespace Stratis.Features.FederatedPeg.Controllers
{
    public class CounterChainConsensusClient : RestApiClientBase
    {
        /// <summary>
        /// Accesses the consensus controller on the counter-chain.
        /// <para>
        /// In a production/live scenario the sidechain and mainnet federation nodes should run on the same machine.
        /// </para>
        /// </summary>
        /// <param name="counterChainSettings">The counter-chain settings.</param>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> implementation.</param>
        public CounterChainConsensusClient(ICounterChainSettings counterChainSettings, IHttpClientFactory httpClientFactory)
            : base(httpClientFactory, counterChainSettings.CounterChainApiPort, "Consensus", $"http://{counterChainSettings.CounterChainApiHost}")
        {
        }

        public Task<List<ThresholdActivationModel>> GetLockedInDeployments(CancellationToken cancellation = default)
        {
            return this.SendGetRequestAsync<List<ThresholdActivationModel>>("lockedindeployments", null, cancellation);
        }
    }
}
