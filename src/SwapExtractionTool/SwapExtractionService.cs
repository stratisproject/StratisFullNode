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
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Utilities.JsonErrors;

namespace SwapExtractionTool
{
    public sealed class SwapExtractionService
    {
        private const string walletName = "";
        private const string walletPassword = "";

        private readonly Network stratisNetwork;
        private readonly Network straxNetwork;

        private readonly List<SwapTransaction> swapTransactions;

        /// <summary>
        /// This is the block height from which to start scanning from.
        /// </summary>
        private const int StartHeight = 1_487_140;
        private const int EndHeight = 1_487_160;

        public SwapExtractionService(Network stratisNetwork, Network straxNetwork)
        {
            this.stratisNetwork = stratisNetwork;
            this.straxNetwork = straxNetwork;
            this.swapTransactions = new List<SwapTransaction>();
        }

        public async Task RunAsync(bool distribute = false)
        {
            for (int height = StartHeight; height < EndHeight; height++)
            {
                Block block = await RetrieveBlockAtHeightAsync(height);
                ProcessBlockForSwapTransactions(block, height);
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

            if (distribute)
                await BuildAndSendDistributionTransactionsAsync();
        }

        private async Task<Block> RetrieveBlockAtHeightAsync(int blockHeight)
        {
            var blockHash = await $"http://localhost:{this.stratisNetwork.DefaultAPIPort}/api"
                .AppendPathSegment("consensus/getblockhash")
                .SetQueryParams(new { height = blockHeight })
                .GetJsonAsync<string>();

            var blockHex = await $"http://localhost:{this.stratisNetwork.DefaultAPIPort}/api"
                .AppendPathSegment("blockstore/block")
                .SetQueryParams(new SearchByHashRequest() { Hash = blockHash })
                .GetJsonAsync<string>();

            var block = Block.Load(Encoders.Hex.DecodeData(blockHex), this.stratisNetwork.Consensus.ConsensusFactory);

            return block;
        }

        private void ProcessBlockForSwapTransactions(Block block, int blockHeight)
        {
            // Inspect each transaction
            foreach (Transaction transaction in block.Transactions)
            {
                // Find all the OP_RETURN outputs.
                foreach (TxOut output in transaction.Outputs.Where(o => o.ScriptPubKey.IsUnspendable))
                {
                    IList<Op> ops = output.ScriptPubKey.ToOps();
                    var potentialStraxAddress = Encoding.ASCII.GetString(ops.Last().PushData);
                    try
                    {
                        // Verify the sender address is a valid Strax address
                        var validStraxAddress = BitcoinAddress.Create(potentialStraxAddress, this.straxNetwork);
                        Console.WriteLine($"Swap found: {validStraxAddress}:{output.Value}");
                        var swapTransaction = new SwapTransaction()
                        {
                            BlockHeight = blockHeight,
                            StraxAddress = validStraxAddress.ToString(),
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

        private async Task BuildAndSendDistributionTransactionsAsync()
        {
            var distributedSwaps = new List<SwapTransaction>();

            foreach (SwapTransaction swapTransaction in this.swapTransactions)
            {
                var distributedSwap = new SwapTransaction(swapTransaction);

                try
                {
                    IActionResult result = await $"http://localhost:{this.straxNetwork.DefaultAPIPort}/api"
                        .AppendPathSegment("wallet/build-transaction")
                        .PostJsonAsync(new BuildTransactionRequest
                        {
                            WalletName = walletName,
                            AccountName = "account 0",
                            FeeType = "medium",
                            Password = walletPassword,
                            Recipients = new List<RecipientModel> { new RecipientModel { DestinationAddress = swapTransaction.StraxAddress, Amount = Money.Satoshis(swapTransaction.SenderAmount).ToUnit(MoneyUnit.BTC).ToString() } }
                        })
                        .ReceiveJson<IActionResult>();

                    if (result is ErrorResult errorResult)
                    {
                        var response = errorResult.Value as ErrorResponse;
                        throw new Exception($"Failed to build swap transaction {swapTransaction.TransactionHash} : {response.Errors.First().Description}");
                    }

                    var buildResult = (result as JsonResult).Value as WalletBuildTransactionModel;

                    distributedSwap.TransactionBuilt = true;

                    IActionResult sendActionResult = await $"http://localhost:{this.straxNetwork.DefaultAPIPort}/api"
                        .AppendPathSegment("wallet/send-transaction")
                        .PostJsonAsync(new SendTransactionRequest
                        {
                            Hex = buildResult.Hex
                        })
                        .ReceiveJson<IActionResult>();

                    if (sendActionResult is ErrorResult sendErrorResult)
                    {
                        var response = sendErrorResult.Value as ErrorResponse;
                        throw new Exception($"Failed to send swap transaction {swapTransaction.TransactionHash} : {response.Errors.First().Description}");
                    }

                    var sendResult = (sendActionResult as JsonResult).Value as WalletSendTransactionModel;

                    distributedSwap.TransactionSent = true;
                    distributedSwap.TransactionSentHash = sendResult.TransactionId.ToString();

                    Console.WriteLine($"Swap trasnction built and sent to {swapTransaction.StraxAddress}:{swapTransaction.SenderAmount}");

                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    break;
                }
                finally
                {
                    distributedSwaps.Add(distributedSwap);
                }
            }

            using (var writer = new StreamWriter(Path.Combine("c:", "[StratisWork]", "distributedSwaps.csv")))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                foreach (SwapTransaction swapTransaction in distributedSwaps)
                {
                    csv.WriteRecord(swapTransaction);
                }
            }
        }
    }

    public sealed class SwapTransaction
    {
        public SwapTransaction() { }

        public SwapTransaction(SwapTransaction swapTransaction)
        {
            this.BlockHeight = swapTransaction.BlockHeight;
            this.StraxAddress = swapTransaction.StraxAddress;
            this.SenderAmount = swapTransaction.SenderAmount;
            this.TransactionHash = swapTransaction.TransactionHash;
        }

        public int BlockHeight { get; set; }
        public string StraxAddress { get; set; }
        public Money SenderAmount { get; set; }
        public string TransactionHash { get; set; }
        public bool TransactionBuilt { get; set; }
        public bool TransactionSent { get; set; }
        public string TransactionSentHash { get; set; }
    }
}
