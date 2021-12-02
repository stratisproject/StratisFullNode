using Stratis.Bitcoin.Configuration;

namespace Stratis.Bitcoin.Features.ExternalApi
{
    /// <summary>
    /// External Api Settings.
    /// </summary>
    public class ExternalApiSettings
    {
        /// <summary>
        /// The key for the Etherscan api key.
        /// </summary>
        public const string EtherscanApiKeyKey = "etherscanapikey";

        /// <summary>
        /// The Etherscan gas oracle key.
        /// </summary>
        public const string EtherscanGasOracleUrlKey = "etherscangasoracle";

        /// <summary>
        /// The Ethereum gas price tracking key.
        /// </summary>
        public const string EthereumGasPriceTrackingKey = "ethereumgaspricetracking";

        /// <summary>
        /// The Ethereum price url key.
        /// </summary>
        public const string PriceUrlKey = "ethereumpriceurl";

        /// <summary>
        /// The key for a flag indicating whether price tracking is enabled.
        /// </summary>
        public const string PriceTrackingKey = "pricetracking";

        /// <summary>
        /// The Etherscan api key.
        /// </summary>
        public string EtherscanApiKey { get; set; }

        /// <summary>
        /// The Etherscan gas oracle url and key.
        /// </summary>
        public string EtherscanGasOracleUrl { get; set; }

        /// <summary>
        /// Indicates whether Ethereum gas price tracking is enabled.
        /// </summary>
        public bool EthereumGasPriceTracking { get; set; }

        /// <summary>
        /// Url for retrieving Stratis and Ethereum prices.
        /// </summary>
        public string PriceUrl { get; set; }

        /// <summary>
        /// A flag indicating whether price tracking is enabled.
        /// </summary>
        public bool PriceTracking { get; set; }

        /// <summary>
        /// The instance constructor.
        /// </summary>
        /// <param name="nodeSettings">The <see cref="NodeSettings"/>.</param>
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
