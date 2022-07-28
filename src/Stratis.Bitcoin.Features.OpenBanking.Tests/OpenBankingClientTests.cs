using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Bitcoin.Features.OpenBanking.Tests
{
    /// <summary>
    /// Financial-grade API (FAPI) is a technical specification that Financial-grade API Working Group of OpenID Foundation has developed. 
    /// It uses OAuth 2.0 and OpenID Connect (OIDC) as its base and defines additional technical requirements for the financial industry 
    /// and other industries that require higher security.
    /// </summary>
    public class OpenBankingClientTests
    {
        private readonly OpenBankingSettings settings;
        private readonly OpenBankConfiguration configuration;
        private readonly OpenBankAccount account;
        private readonly string[] openBankingScopes;

        public OpenBankingClientTests()
        {
            var network = new CirrusTest();
            var nodeSettings = new NodeSettings(network, args: new[] { "clientid=836fde77-d2c2-4f3b-8b3d-80f4f0cf1b02", "clientsecret=b7e4b4d8-7a9e-4b3a-b3c0-abed80a2c5d4" });
            this.settings = new OpenBankingSettings(nodeSettings);
            this.configuration = new OpenBankConfiguration()
            {
                AISPURL = "https://ob-mtls-resource-server.azurewebsites.net/open-banking/v3.1/aisp",
                RedirectURL = "https://localhost:44315/",
                TokenURL = "https://ob-mtls-resource-server.azurewebsites.net/token",
                AuthCodeURL = "https://ob-mtls-resource-server.azurewebsites.net/auth/code"
            };
            this.openBankingScopes = new[] { "openid", "profile", "email", "accounts", "transactions" };
            this.account = new OpenBankAccount(this.configuration, "22289", SmartContracts.MetadataTracker.MetadataTableNumber.GBPT, "GBP", "tBHv3YgiSGZiohpEdTcsNbXivrCzxVReeP", 0);
        }

        /// <summary>Get a short-lived authorization code from the server.</summary>
        /// <remarks>The authorization code is a temporary code that the client will exchange for an access token. 
        /// The code itself is obtained from the authorization server where the user gets a chance to see what 
        /// the information the client is requesting, and approve or deny the request.</remarks>
        private string GetCode(OpenBankConfiguration configuration, OpenBankingSettings settings, string[] openBankingScopes)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                var oauthRequest = new HttpRequestMessage(HttpMethod.Get, configuration.AuthCodeURL);
                oauthRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                  { "client_id", settings.OpenBankingClientId },
                  { "client_secret", settings.OpenBankingClientSecret },
                  { "redirect_uri", configuration.RedirectURL },
                  { "scope", string.Join(" ", openBankingScopes) }
                });
                HttpResponseMessage oauthResult = httpClient.SendAsync(oauthRequest).Result;
                string oauthResultContent = oauthResult.Content.ReadAsStringAsync().Result;
                dynamic oauth = JsonConvert.DeserializeObject<dynamic>(oauthResultContent);

                return oauth.code;
            }
        }

        /// <summary>
        /// This method proves that we can retrieve a list of transactions.
        /// </summary>
        [Fact]
        public void CanGetTransactions()
        {
            // Arrange
            var client = new OpenBankingClient(this.settings);

            // Act
            var openBankingTransactions = client.GetTransactionsAsync(this.account, null).Result;

            // Assert
            Assert.Equal(3, openBankingTransactions.Data.Transaction.Length);
        }

        /// <summary>
        /// This method proves that we can retrieve an access code.
        /// </summary>
        [Fact]
        public void CanGetAccessCode()
        {
            // Arrange
            var client = new OpenBankingClient(this.settings);

            // Act
            string code = this.GetCode(this.configuration, this.settings, new[] { "accounts" });

            Assert.NotNull(code);
        }

        /// <summary>
        /// This method proves that we can retrieve an authorization token.
        /// </summary>
        [Fact]
        public void CanGetAccessToken()
        {
            // Arrange
            var client = new OpenBankingClient(this.settings);

            // Act
            var openBankingToken = client.GetAccessTokenAsync(this.configuration, this.settings, new[] { "accounts" }).Result;

            Assert.NotNull(openBankingToken);
        }
    }
}