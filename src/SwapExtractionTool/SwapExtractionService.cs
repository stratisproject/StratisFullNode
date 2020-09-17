using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Features.BlockStore.Models;

namespace SwapExtractionTool
{
    public sealed class SwapExtractionService
    {
        private readonly Network network;

        /// <summary>
        /// This is the block height from which to start scanning from.
        /// </summary>
        private const int StartHeight = 1482863;
        //private const int StartHeight = 1_900_000;

        public SwapExtractionService(Network network)
        {
            this.network = network;
        }

        public async Task RunAsync()
        {
            // Retrieve 500 blocks at a time from start height.
            for (int height = StartHeight; height < StartHeight + 1; height++)
            {
                Block block = await RetrieveBlockAtHeightAsync(height);

                // Process block for Swap OP_RETURN transactions.
                ProcessBlock(block, height);
            }
        }

        private async Task<Block> RetrieveBlockAtHeightAsync(int blockHeight)
        {
            var blockHash = await $"http://localhost:{this.network.DefaultAPIPort}/api"
                .AppendPathSegment("consensus/getblockhash")
                .SetQueryParams(new { height = blockHeight })
                .GetJsonAsync<string>();

            var blockHex = await $"http://localhost:{this.network.DefaultAPIPort}/api"
                .AppendPathSegment("blockstore/block")
                .SetQueryParams(new SearchByHashRequest() { Hash = blockHash })
                .GetJsonAsync<string>();

            var block = Block.Load(Encoders.Hex.DecodeData(blockHex), this.network.Consensus.ConsensusFactory);

            return block;
        }

        private void ProcessBlock(Block block, int blockHeight)
        {
            // Inspect each transaction
            foreach (Transaction transaction in block.Transactions)
            {
                // Find all the OP_RETURN outputs.
                foreach (TxOut output in transaction.Outputs.Where(o => o.ScriptPubKey.IsUnspendable))
                {
                    // Verify the sender address is a valid Strax address
                    IList<Op> ops = output.ScriptPubKey.ToOps();
                    var pushdata = ops.Last().PushData;
                    var senderAddress = Encoding.ASCII.GetString(pushdata);
                    var swapTransaction = new SwapTransaction()
                    {
                        BlockHeight = blockHeight,
                        SenderAddress = senderAddress.ToString(),
                        SenderAmount = output.Value,
                        TransactionHash = transaction.GetHash()
                    };
                }
            }
        }
    }

    public sealed class SwapTransaction
    {
        public int BlockHeight { get; set; }
        public string SenderAddress { get; set; }
        public Money SenderAmount { get; set; }
        public uint256 TransactionHash { get; set; }
    }
}
