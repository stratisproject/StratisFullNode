namespace Stratis.Bitcoin.Features.ExternalApi.Models
{
    /// <summary>
    /// Etherscan gas oracle response.
    /// </summary>
    public class EtherscanGasOracleResponse
    {
        /// <summary>
        /// Status.
        /// </summary>
        public string status { get; set; }

        /// <summary>
        /// Message.
        /// </summary>
        public string message { get; set; }

        /// <summary>
        /// See <see cref="EtherscanGasOracleResponseResult"/>.
        /// </summary>
        public EtherscanGasOracleResponseResult result { get; set; }
    }

    /// <summary>
    /// Etherscan gas oracle response result as included in <see cref="EtherscanGasOracleResponse"/>.
    /// </summary>
    public class EtherscanGasOracleResponseResult
    {
        /// <summary>
        /// The last block.
        /// </summary>
        public int LastBlock { get; set; }

        /// <summary>
        /// The safe gas price.
        /// </summary>
        public int SafeGasPrice { get; set; }

        /// <summary>
        /// The proposed gas price.
        /// </summary>
        public int ProposeGasPrice { get; set; }

        /// <summary>
        /// The fast gas price.
        /// </summary>
        public int FastGasPrice { get; set; }
    }
}
