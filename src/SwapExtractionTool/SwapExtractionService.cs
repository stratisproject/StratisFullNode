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
    public sealed class SwapExtractionService
    {
        private const string walletName = "";
        private const string walletPassword = "";

        private readonly int stratisNetworkApiPort;
        private readonly Network straxNetwork;

        private readonly List<SwapTransaction> swapTransactions;

        /// <summary>
        /// This is the block height from which to start scanning from.
        /// </summary>
        private const int StartHeight = 1494786;
        private const int EndHeight = 1494787;

        public SwapExtractionService(int stratisNetworkApiPort, Network straxNetwork)
        {
            this.stratisNetworkApiPort = stratisNetworkApiPort;
            this.straxNetwork = straxNetwork;
            this.swapTransactions = new List<SwapTransaction>();
        }

        public async Task RunAsync(ExtractionType extractionType, bool distribute = false)
        {
            for (int height = StartHeight; height < EndHeight; height++)
            {
                BlockTransactionDetailsModel block = await RetrieveBlockAtHeightAsync(height);

                if (extractionType == ExtractionType.Swap)
                    ProcessBlockForSwapTransactions(block, height);

                if (extractionType == ExtractionType.Vote)
                    await ProcessBlockForVoteTransactionsAsync(block, height);
            }

            if (extractionType == ExtractionType.Swap)
            {
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
        }

        private async Task<BlockTransactionDetailsModel> RetrieveBlockAtHeightAsync(int blockHeight)
        {
            var blockHash = await $"http://localhost:{this.stratisNetworkApiPort}/api"
                .AppendPathSegment("consensus/getblockhash")
                .SetQueryParams(new { height = blockHeight })
                .GetJsonAsync<string>();

            BlockTransactionDetailsModel blockModel = await $"http://localhost:{this.stratisNetworkApiPort}/api"
                .AppendPathSegment("blockstore/block")
                .SetQueryParams(new SearchByHashRequest() { Hash = blockHash, ShowTransactionDetails = true, OutputJson = true })
                .GetJsonAsync<BlockTransactionDetailsModel>();

            return blockModel;
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
                        var validStraxAddress = BitcoinAddress.Create(potentialStraxAddress, this.straxNetwork);
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

        private async Task ProcessBlockForVoteTransactionsAsync(BlockTransactionDetailsModel block, int blockHeight)
        {
            var totalNoVotes = 0;
            var totalYesVotes = 0;

            // Inspect each transaction
            foreach (TransactionVerboseModel transaction in block.Transactions)
            {
                // Find the first the OP_RETURN output.
                Vout opReturnOutput = transaction.VOut.FirstOrDefault(v => v.ScriptPubKey.Type == "nulldata");
                if (opReturnOutput == null)
                    continue;

                IList<Op> ops = new NBitcoin.Script(opReturnOutput.ScriptPubKey.Asm).ToOps();
                var potentialVote = Encoding.ASCII.GetString(ops.Last().PushData);
                try
                {
                    var isVote = potentialVote.Substring(0, 1);
                    if (isVote != "V")
                        continue;

                    var isVoteValue = potentialVote.Substring(1, 1);
                    if (isVoteValue == "1" || isVoteValue == "0")
                    {
                        // Verify the sender address is a valid Strat address
                        var potentialStratAddress = potentialVote.Substring(2);
                        dynamic validateResult = await $"http://localhost:{this.stratisNetworkApiPort}/api"
                            .AppendPathSegment("node/validateaddress")
                            .SetQueryParams(new { address = potentialStratAddress })
                            .GetJsonAsync();

                        if (isVoteValue == "0")
                        {
                            totalNoVotes++;
                            Console.WriteLine($"Vote found at height {blockHeight}: '{potentialStratAddress}' voted : no");
                        }

                        if (isVoteValue == "1")
                        {
                            totalYesVotes++;
                            Console.WriteLine($"Vote found at height {blockHeight}: '{potentialStratAddress}' voted : yes");
                        }
                    }
                }
                catch (Exception)
                {
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

    public enum ExtractionType
    {
        Swap,
        Vote
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
