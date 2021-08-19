namespace Stratis.Bitcoin.Features.ExternalApi.Models
{
    public class EtherscanGasOracleResponse
    {
        public string status { get; set; }

        public string message { get; set; }

        public EtherscanGasOracleResponseResult result { get; set; }
    }

    public class EtherscanGasOracleResponseResult
    {
        public int LastBlock { get; set; }

        public int SafeGasPrice { get; set; }

        public int ProposeGasPrice { get; set; }

        public int FastGasPrice { get; set; }
    }
}
