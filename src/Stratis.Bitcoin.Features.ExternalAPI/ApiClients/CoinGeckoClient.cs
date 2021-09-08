using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.ExternalApi.Models;

namespace Stratis.Bitcoin.Features.ExternalApi.ApiClients
{
    public class CoinGeckoClient : IDisposable
    {
        public const string DummyUserAgent = "Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.129 Safari/537.36";

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
            var targetUri = new Uri(this.externalApiSettings.PriceUrl);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, targetUri);
            requestMessage.Headers.TryAddWithoutValidation("User-Agent", DummyUserAgent);

            HttpResponseMessage resp = await this.client.SendAsync(requestMessage).ConfigureAwait(false);
            string content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

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
