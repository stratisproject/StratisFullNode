using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.ExternalApi.Models;

namespace Stratis.Bitcoin.Features.ExternalApi.ApiClients
{
    public class CoinGeckoClient : IDisposable
    {
        private readonly ExternalApiSettings externalApiSettings;
        private readonly HttpClient client;

        private decimal stratisPrice = -1;
        private decimal ethereumPrice = -1;

        public CoinGeckoClient(ExternalApiSettings externalApiSettings)
        {
            this.externalApiSettings = externalApiSettings;

            this.client = new HttpClient();
        }

        public decimal GetStratisPrice()
        {
            return this.stratisPrice;
        }

        public decimal GetEthereumPrice()
        {
            return this.ethereumPrice;
        }

        public async Task<CoinGeckoResponse> PriceDataRetrievalAsync()
        {
            string content = await this.client.GetStringAsync(this.externalApiSettings.EtherscanGasOracleUrl);

            CoinGeckoResponse response = JsonConvert.DeserializeObject<CoinGeckoResponse>(content);

            if (response?.stratis == null || response?.ethereum == null)
            {
                return null;
            }

            this.stratisPrice = response.stratis.usd;
            this.ethereumPrice = response.ethereum.usd;

            return response;
        }

        public void Dispose()
        {
            this.client?.Dispose();
        }
    }
}
