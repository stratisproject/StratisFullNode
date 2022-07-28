using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using System.IO;
using System;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public interface IOpenBankingClient
    {
        Task<OBGetTransactionsResponse> GetTransactionsAsync(IOpenBankAccount account, DateTime? fromDateTime);
    }

    public class OpenBankingClient : IOpenBankingClient
    {
        private readonly OpenBankingSettings settings;
        private Token accessToken;

        public OpenBankingClient(OpenBankingSettings settings)
        {
            this.settings = settings;
        }

        private async Task<StreamReader> GetResponseAsStreamAsync(string endpoint)
        {
            /*
             GET /accounts/22289/transactions HTTP/1.1
                Authorization: Bearer Az90SAOJklae
                x-fapi-financial-id: OB/2017/001
                x-fapi-customer-last-logged-time:  Sun, 10 Sep 2017 19:43:31 GMT
                x-fapi-customer-ip-address: 104.25.212.99
                x-fapi-interaction-id: 93bac548-d2de-4546-b106-880a5018460d
                Accept: application/json

             HTTP/1.1 200 OK
                x-fapi-interaction-id: 93bac548-d2de-4546-b106-880a5018460d          
            */
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                var tokenRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
                tokenRequest.Headers.Add("Authorization", $"Bearer {this.accessToken}");
                var tokenResponse = await httpClient.SendAsync(tokenRequest);
                var tokenResponseContent = await tokenResponse.Content.ReadAsStreamAsync();
                return new StreamReader(tokenResponseContent);
            }
        }

        public async Task<OBGetTransactionsResponse> GetTransactionsAsync(IOpenBankAccount account, DateTime? fromDateTime)
        {
            if (this.accessToken == null)
                this.accessToken = await GetAccessTokenAsync(account.OpenBankConfiguration, this.settings, new[] { "transactions" });

            var sr = await GetResponseAsStreamAsync(account.TransactionsEndpoint(fromDateTime));

            return JsonSerializer.Deserialize<OBGetTransactionsResponse>(sr.ReadToEnd());
        }

        public async Task<Token> GetAccessTokenAsync(OpenBankConfiguration configuration, OpenBankingSettings settings, string[] openBankingScopes)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                var tokenRequest = new HttpRequestMessage(HttpMethod.Post, configuration.TokenURL);
                tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                  { "client_id", settings.OpenBankingClientId },
                  { "client_secret", settings.OpenBankingClientSecret },
                  { "redirect_uri", configuration.RedirectURL },
                  //{ "code", this.GetCode(configuration, settings, openBankingScopes) },
                  { "grant_type", "authorization_code" },
                  { "scope", string.Join(" ", openBankingScopes) }
                });
                var tokenResponse = await httpClient.SendAsync(tokenRequest);
                var responseContent = await tokenResponse.Content.ReadAsStringAsync();

                return JsonSerializer.Deserialize<Token>(responseContent);
            }
        }
    }

    public class Token
    {
        public string access_token { get; set; }
        public string refresh_token { get; set; }
        public int expires_in { get; set; }
        public string token_type { get; set; }
    }
}

