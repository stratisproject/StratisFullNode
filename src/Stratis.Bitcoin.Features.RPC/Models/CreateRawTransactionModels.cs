using NBitcoin;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.RPC.Models
{
    /// <summary>
    /// Used for storing the data that represents desired transaction inputs for the createrawtransaction RPC call.
    /// </summary>
    public class CreateRawTransactionInput
    {
        [JsonProperty(PropertyName = "txid")]
        public uint256 TxId { get; set; }

        [JsonProperty(PropertyName = "vout")]
        public uint VOut { get; set; }

        /// <summary>
        /// If not provided, the sequence in the created transaction will be set to uint.MaxValue by default.
        /// </summary>
        [JsonProperty(PropertyName = "sequence")]
        public uint? Sequence { get; set; }
    }

    /// <summary>
    /// Used for storing the key-value pairs that represent desired transaction outputs for the createrawtransaction RPC call.
    /// </summary>
    /// <remarks>The key and value properties are populated by a custom model binder and therefore never appear with those names in the raw JSON.</remarks>
    public class CreateRawTransactionOutput
    {
            [JsonProperty]
            public string Key { get; set; }

            [JsonProperty]
            public string Value { get; set; }
    }

    public class CreateRawTransactionResponse
    {
        [JsonProperty(PropertyName = "hex")]
        public Transaction Transaction { get; set; }
    }
}
