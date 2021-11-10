using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Stratis.Bitcoin.Features.RPC.Models
{
    public class TransactionInfoModel
    {
        public TransactionInfoModel()
        {

        }

        public TransactionInfoModel(JObject tx)
        {
            if (tx["blockheight"] != null)
            {
                this.BlockHeight = (int)tx["blockheight"];
            }

            this.BlockHash = (string)tx["blockhash"];
            decimal amount = (decimal)tx["amount"];
            this.Amount = new Money((long)(amount * Money.COIN));
            this.Confirmations = (int)tx["confirmations"];
            this.TxId = (string)tx["txid"];
            this.BlockIndex = (int)tx["blockindex"];

            if (tx["generated"] != null)
            {
                this.Generated = (bool)tx["generated"];
            }
            else
            {
                // Default to True for earlier versions, i.e. if not present
                this.Generated = false;
            }
        }

        /// <summary>
        /// The transaction id.</summary>
        [JsonProperty(Order = 1, PropertyName = "txid")]
        public string TxId { get; set; }

        /// <summary>
        /// The index of the transaction in the block that includes it.
        /// </summary>
        [JsonProperty(PropertyName = "blockindex")]
        public int BlockIndex { get; set; }

        /// <summary>
        /// Only present if transaction only input is a coinbase one.
        /// </summary>
        [JsonProperty(PropertyName = "generated")]
        public bool Generated { get; set; }

        /// <summary>
        /// The transaction amount.
        /// </summary>
        [JsonProperty(PropertyName = "amount")]
        public long Amount { get; set; }

        /// <summary>
        /// The number of confirmations.
        /// </summary>
        [JsonProperty(PropertyName = "confirmations")]
        public int Confirmations { get; set; }

        /// <summary>
        /// The hash of the block containing this transaction.</summary>
        [JsonProperty(PropertyName = "blockhash")]
        public string BlockHash { get; set; }

        /// <summary>
        /// The height of the block containing this transaction.
        /// </summary>
        [JsonProperty(PropertyName = "blockheight")]
        public int BlockHeight { get; set; }
    }
}