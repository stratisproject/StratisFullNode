using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
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
        private readonly Network straxNetwork;

        private List<SwapTransaction> swapTransactions;

        /// <summary>
        /// This is the block height from which to start scanning from.
        /// </summary>
        private const int StartHeight = 1_487_140;
        private const int EndHeight = 1_487_160;
        //private const int StartHeight = 1_900_000;

        public SwapExtractionService(Network stratisNetwork, Network straxNetwork)
        {
            this.network = stratisNetwork;
            this.straxNetwork = straxNetwork;
            this.swapTransactions = new List<SwapTransaction>();
        }

        public async Task RunAsync()
        {
            for (int height = StartHeight; height < EndHeight; height++)
            {
                Block block = await RetrieveBlockAtHeightAsync(height);

                // Process block for swap OP_RETURN transactions.
                ProcessBlock(block, height);
            }

            Console.WriteLine($"Writing {this.swapTransactions.Count} swap transactions.");

            using (var writer = new StreamWriter(Path.Combine("c:", "[StratisWork]", "swaps.csv")))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                foreach (SwapTransaction swapTransaction in this.swapTransactions)
                {
                    csv.WriteRecord(swapTransaction);
                }
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
                    IList<Op> ops = output.ScriptPubKey.ToOps();
                    var pushData = Encoding.ASCII.GetString(ops.Last().PushData);
                    var straxAddress = pushData.Substring("SWAP".Length);
                    try
                    {
                        // Verify the sender address is a valid Strax address
                        var validStraxAddress = BitcoinAddress.Create(straxAddress, this.straxNetwork);
                        Console.WriteLine($"Swap found: {validStraxAddress}:{output.Value}");
                        var swapTransaction = new SwapTransaction()
                        {
                            BlockHeight = blockHeight,
                            SenderAddress = validStraxAddress.ToString(),
                            SenderAmount = output.Value,
                            TransactionHash = transaction.GetHash().ToString()
                        };

                        this.swapTransactions.Add(swapTransaction);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
    }

    public sealed class SwapTransaction
    {
        public int BlockHeight { get; set; }
        public string SenderAddress { get; set; }
        public Money SenderAmount { get; set; }
        public string TransactionHash { get; set; }
    }
}
