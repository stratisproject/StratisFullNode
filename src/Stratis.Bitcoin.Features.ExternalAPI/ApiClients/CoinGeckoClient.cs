using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.ExternalApi.Models;

namespace Stratis.Bitcoin.Features.ExternalApi.ApiClients
{
    /// <summary>
    /// Retrieves price data from Coin Gecko.
    /// </summary>
    public class CoinGeckoClient : IDisposable
    {
        /// <summary>
        /// The user-agent used by this class when retrieving price data.
        /// </summary>
        public const string DummyUserAgent = "Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.129 Safari/537.36";

        private readonly ExternalApiSettings externalApiSettings;
        private readonly HttpClient client;

        private decimal stratisPrice = -1;
        private decimal ethereumPrice = -1;

        /// <summary>
        /// Class constructor.
        /// </summary>
        /// <param name="externalApiSettings">The <see cref="ExternalApiSettings"/>.</param>
        public CoinGeckoClient(ExternalApiSettings externalApiSettings)
        {
            this.externalApiSettings = externalApiSettings;

            this.client = new HttpClient();
        }

        /// <summary>
        /// Gets the most recently retrieved Stratis price.
        /// </summary>
        /// <returns>The Stratis price.</returns>
        public decimal GetStratisPrice()
        {
            return this.stratisPrice;
        }

        /// <summary>
        /// Gets the most recently retrieved Ethereum price.
        /// </summary>
        /// <returns>The Ethereum price.</returns>
        public decimal GetEthereumPrice()
        {
            return this.ethereumPrice;
        }

        /// <summary>
        /// Retrieves price data for Stratis and Ethereum from Coin Gecko.
        /// </summary>
        /// <returns>The <see cref="CoinGeckoResponse"/>.</returns>
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

        /// <summary>
        /// Disposes instances of this class.
        /// </summary>
        public void Dispose()
        {
            this.client?.Dispose();
        }
    }
}
