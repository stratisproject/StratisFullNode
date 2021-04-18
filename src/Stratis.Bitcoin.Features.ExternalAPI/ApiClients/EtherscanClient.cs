using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.ExternalApi.Models;

namespace Stratis.Bitcoin.Features.ExternalApi.ApiClients
{
    public class EtherscanClient : IDisposable
    {
        private readonly ExternalApiSettings externalApiSettings;
        private readonly HttpClient client;

        private int[] fastSamples = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private int[] proposeSamples = new [] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private int[] safeSamples = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        private bool sampled = false;
        private int samplePointer = 0;

        public EtherscanClient(ExternalApiSettings externalApiSettings)
        {
            this.externalApiSettings = externalApiSettings;

            this.client = new HttpClient();
        }

        /// <summary>
        /// Retrieves a recommended gas price based on historical measured samples.
        /// </summary>
        /// <remarks>We use the average of the historic proposed price.</remarks>
        /// <returns>The recommended gas price in gwei. Returns -1 if price data is not yet available.</returns>
        public int GetGasPrice()
        {
            if (!this.sampled)
            {
                return -1;
            }

            // In future this could be made more responsive to sudden changes in the recent price, and possibly use the safe price if it can be reasonably anticipated that the transaction will still confirm timeously.

            // The weightings should add up to 1.
            decimal fastWeighting = 0.0m;
            decimal proposedWeighting = 1.0m;
            decimal safeWeighting = 0.0m;

            decimal totalFast = 0m;
            decimal totalProposed = 0m;
            decimal totalSafe = 0m;

            for (int i = 0; i < this.fastSamples.Length; i++)
            {
                totalFast += this.fastSamples[i];
                totalProposed += this.proposeSamples[i];
                totalSafe += this.safeSamples[i];
            }

            return (int)Math.Ceiling((((totalFast * fastWeighting) + (totalProposed * proposedWeighting) + (totalSafe * safeWeighting)) / this.fastSamples.Length));
        }

        public async Task<EtherscanGasOracleResponse> GasOracle(bool recordSamples)
        {
            string content = await this.client.GetStringAsync(this.externalApiSettings.EtherscanGasOracleUrl);

            EtherscanGasOracleResponse response = JsonConvert.DeserializeObject<EtherscanGasOracleResponse>(content);

            if (response?.result == null)
            {
                return null;
            }

            // We do not know how long the node was shut down for, so the very first sample must populate every array element (regardless of whether the caller requested sample recording).
            // There would be little point storing the historic data in the key value store, for instance.
            if (!this.sampled)
            {
                for (int i = 0; i < this.fastSamples.Length; i++)
                {
                    this.fastSamples[i] = response.result.FastGasPrice;
                    this.proposeSamples[i] = response.result.ProposeGasPrice;
                    this.safeSamples[i] = response.result.SafeGasPrice;
                }

                this.sampled = true;

                return response;
            }

            if (recordSamples)
            {
                this.fastSamples[this.samplePointer] = response.result.FastGasPrice;
                this.proposeSamples[this.samplePointer] = response.result.ProposeGasPrice;
                this.safeSamples[this.samplePointer] = response.result.SafeGasPrice;

                this.samplePointer++;

                if (this.samplePointer >= this.fastSamples.Length)
                {
                    this.samplePointer = 0;
                }
            }

            return response;
        }

        public void Dispose()
        {
            this.client?.Dispose();
        }
    }
}
