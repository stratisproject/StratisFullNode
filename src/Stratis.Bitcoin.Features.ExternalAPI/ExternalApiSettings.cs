using Stratis.Bitcoin.Configuration;

namespace Stratis.Bitcoin.Features.ExternalApi
{
    public class ExternalApiSettings
    {
        public const string EtherscanApiKeyKey = "etherscanapikey";

        public const string EtherscanGasOracleUrlKey = "etherscangasoracle";

        public const string EthereumGasPriceTrackingKey = "ethereumgaspricetracking";

        public const string PriceUrlKey = "ethereumpriceurl";

        public const string PriceTrackingKey = "pricetracking";

        public string EtherscanApiKey { get; set; }

        public string EtherscanGasOracleUrl { get; set; }

        public bool EthereumGasPriceTracking { get; set; }

        public string PriceUrl { get; set; }

        public bool PriceTracking { get; set; }

        public ExternalApiSettings(NodeSettings nodeSettings)
        {
            // To avoid any rate limiting by Etherscan it is better to have an API key defined, but the API is still supposed to work to a limited extent without one.
            this.EtherscanApiKey = nodeSettings.ConfigReader.GetOrDefault(EtherscanApiKeyKey, "YourApiKeyToken");
            this.EtherscanGasOracleUrl = nodeSettings.ConfigReader.GetOrDefault(EtherscanGasOracleUrlKey, "https://api.etherscan.io/api?module=gastracker&action=gasoracle&apikey=" + this.EtherscanApiKey);
            this.EthereumGasPriceTracking = nodeSettings.ConfigReader.GetOrDefault(EthereumGasPriceTrackingKey, true);
            this.PriceUrl = nodeSettings.ConfigReader.GetOrDefault(PriceUrlKey, "https://api.coingecko.com/api/v3/simple/price?ids=stratis,ethereum&vs_currencies=usd");
            this.PriceTracking = nodeSettings.ConfigReader.GetOrDefault(PriceTrackingKey, true);
        }
    }
}
