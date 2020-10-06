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
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Utilities.JsonErrors;

namespace SwapExtractionTool
{
    public sealed class SwapExtractionService : ExtractionBase
    {
        private const string walletName = "";
        private const string walletPassword = "";

        private readonly List<SwapTransaction> swapTransactions;

        public SwapExtractionService(int stratisNetworkApiPort, Network straxNetwork) : base(stratisNetworkApiPort, straxNetwork)
        {
            this.swapTransactions = new List<SwapTransaction>();
        }

        public async Task RunAsync(int startBlock, bool distribute = false)
        {
            Console.WriteLine($"Scanning for swap transactions...");

            for (int height = startBlock; height < EndHeight; height++)
            {
                BlockTransactionDetailsModel block = await RetrieveBlockAtHeightAsync(height);
                ProcessBlockForSwapTransactions(block, height);
            }

            Console.WriteLine($"Found {this.swapTransactions.Count} swap transactions.");

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

        private void ProcessBlockForSwapTransactions(BlockTransactionDetailsModel block, int blockHeight)
        {
            // Inspect each transaction
            foreach (TransactionVerboseModel transaction in block.Transactions)
            {
                //Find all the OP_RETURN outputs.
                foreach (Vout output in transaction.VOut.Where(o => o.ScriptPubKey.Type == "nulldata"))
                {
                    IList<Op> ops = new NBitcoin.Script(output.ScriptPubKey.Asm).ToOps();
                    var potentialStraxAddress = Encoding.ASCII.GetString(ops.Last().PushData);
                    try
                    {
                        // Verify the sender address is a valid Strax address
                        var validStraxAddress = BitcoinAddress.Create(potentialStraxAddress, StraxNetwork);
                        Console.WriteLine($"Swap found: {validStraxAddress}:{output.Value}");
                        var swapTransaction = new SwapTransaction()
                        {
                            BlockHeight = blockHeight,
                            StraxAddress = validStraxAddress.ToString(),
                            SenderAmount = Money.Coins(output.Value),
                            TransactionHash = transaction.Hash
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
                    IActionResult result = await $"http://localhost:{this.StraxNetwork.DefaultAPIPort}/api"
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

                    IActionResult sendActionResult = await $"http://localhost:{this.StraxNetwork.DefaultAPIPort}/api"
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
}
