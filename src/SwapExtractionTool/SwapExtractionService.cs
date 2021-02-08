using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.Wallet.Models;

namespace SwapExtractionTool
{
    public sealed class SwapExtractionService : ExtractionBase
    {
        private readonly string swapFilePath;
        private const string distributedSwapTransactionsFile = "DistributedSwaps.csv";
        private const string walletName = "swapfunds";
        private const string walletPassword = "password";
        private const decimal splitThreshold = 10_000m * 100_000_000m; // In stratoshi
        private const decimal splitCount = 10;

        private List<DistributedSwapTransaction> distributedSwapTransactions;
        private List<SwapTransaction> swapTransactions;

        public SwapExtractionService(int stratisNetworkApiPort, Network straxNetwork) : base(stratisNetworkApiPort, straxNetwork)
        {
            this.distributedSwapTransactions = new List<DistributedSwapTransaction>();
            this.swapTransactions = new List<SwapTransaction>();
            this.swapFilePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        public async Task RunAsync(int startBlock, bool scan, bool distribute)
        {
            await this.LoadAlreadyDistributedSwapTransactionsAsync();
            await this.LoadSwapTransactionFileAsync();

            if (scan)
                await ScanForSwapTransactionsAsync(startBlock);

            if (distribute)
                await BuildAndSendDistributionTransactionsAsync();
        }

        private async Task LoadAlreadyDistributedSwapTransactionsAsync()
        {
            Console.WriteLine($"Loading already distributed swap transactions...");

            if (File.Exists(Path.Combine(this.swapFilePath, distributedSwapTransactionsFile)))
            {
                using (var reader = new StreamReader(Path.Combine(this.swapFilePath, distributedSwapTransactionsFile)))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false }))
                {
                    this.distributedSwapTransactions = await csv.GetRecordsAsync<DistributedSwapTransaction>().ToListAsync();
                }
            }
            else
            {
                // Created it.
                using (FileStream file = File.Create(Path.Combine(this.swapFilePath, distributedSwapTransactionsFile)))
                {
                    file.Close();
                }
            }
        }

        private async Task LoadSwapTransactionFileAsync()
        {
            Console.WriteLine($"Loading swap transaction file...");

            // First check if the swap file has been created.
            if (File.Exists(Path.Combine(this.swapFilePath, "swaps.csv")))
            {
                // If so populate the list from disk.
                using (var reader = new StreamReader(Path.Combine(this.swapFilePath, "swaps.csv")))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)))
                {
                    this.swapTransactions = await csv.GetRecordsAsync<SwapTransaction>().ToListAsync();
                }
            }
            else
            {
                Console.WriteLine("A swap distribution file has not been created, is this correct? (y/n)");
                int result = Console.Read();
                if (result != 121 && result != 89)
                {
                    Console.WriteLine("Exiting...");
                    return;
                }
            }
        }

        private async Task ScanForSwapTransactionsAsync(int startBlock)
        {
            Console.WriteLine($"Scanning for swap transactions...");

            for (int height = startBlock; height < EndHeight; height++)
            {
                BlockTransactionDetailsModel block = await RetrieveBlockAtHeightAsync(height);
                if (block == null)
                    break;

                ProcessBlockForSwapTransactions(block, height);
            }

            Console.WriteLine($"{this.swapTransactions.Count} swap transactions to process.");
            Console.WriteLine($"{Money.Satoshis(this.swapTransactions.Sum(s => s.SenderAmount)).ToUnit(MoneyUnit.BTC)} STRAT swapped.");

            using (var writer = new StreamWriter(Path.Combine(this.swapFilePath, "swaps.csv")))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteRecords(this.swapTransactions);
            }
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
                        var validStraxAddress = BitcoinAddress.Create(potentialStraxAddress, this.StraxNetwork);
                        Console.WriteLine($"Swap address found: {validStraxAddress}:{output.Value}");

                        if (this.swapTransactions.Any(s => s.TransactionHash == transaction.Hash))
                            Console.WriteLine($"Swap transaction already exists: {validStraxAddress}:{output.Value}");
                        else
                        {
                            var swapTransaction = new SwapTransaction()
                            {
                                BlockHeight = blockHeight,
                                StraxAddress = validStraxAddress.ToString(),
                                SenderAmount = (long)Money.Coins(output.Value),
                                TransactionHash = transaction.Hash
                            };

                            this.swapTransactions.Add(swapTransaction);

                            Console.WriteLine($"Swap address added to file: {validStraxAddress}:{output.Value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Swap address invalid: {potentialStraxAddress}:{output.Value}");
                        Console.WriteLine($"Error: {ex.Message}");
                    }
                }
            }
        }

        private List<RecipientModel> GetRecipients(string destinationAddress, decimal amount)
        {
            if (amount < splitThreshold)
            {
                return new List<RecipientModel> { new RecipientModel { DestinationAddress = destinationAddress, Amount = Money.Satoshis(amount).ToUnit(MoneyUnit.BTC).ToString() } };
            }

            var recipientList = new List<RecipientModel>();

            for (int i = 0; i < splitCount; i++)
            {
                recipientList.Add(new RecipientModel()
                {
                    DestinationAddress = destinationAddress,
                    Amount = Money.Satoshis(amount / splitCount).ToUnit(MoneyUnit.BTC).ToString()
                });
            }

            return recipientList;
        }

        private async Task BuildAndSendDistributionTransactionsAsync()
        {
            foreach (SwapTransaction swapTransaction in this.swapTransactions)
            {
                if (this.distributedSwapTransactions.Any(d => d.TransactionHash == swapTransaction.TransactionHash))
                {
                    Console.WriteLine($"Swap already distributed: {swapTransaction.StraxAddress}:{Money.Satoshis(swapTransaction.SenderAmount).ToUnit(MoneyUnit.BTC)}");
                    continue;
                }

                try
                {
                    var distributedSwapTransaction = new DistributedSwapTransaction(swapTransaction);

                    var result = await $"http://localhost:{this.StraxNetwork.DefaultAPIPort}/api"
                        .AppendPathSegment("wallet/build-transaction")
                        .PostJsonAsync(new BuildTransactionRequest
                        {
                            WalletName = walletName,
                            AccountName = "account 0",
                            FeeType = "medium",
                            Password = walletPassword,
                            Recipients = this.GetRecipients(distributedSwapTransaction.StraxAddress, distributedSwapTransaction.SenderAmount)
                        })
                        .ReceiveBytes();

                    WalletBuildTransactionModel buildTransactionModel = null;

                    try
                    {
                        buildTransactionModel = JsonConvert.DeserializeObject<WalletBuildTransactionModel>(Encoding.ASCII.GetString(result));
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"An error occurred processing swap {distributedSwapTransaction.TransactionHash}");
                        break;
                    }

                    distributedSwapTransaction.TransactionBuilt = true;

                    WalletSendTransactionModel sendActionResult = await $"http://localhost:{this.StraxNetwork.DefaultAPIPort}/api"
                        .AppendPathSegment("wallet/send-transaction")
                        .PostJsonAsync(new SendTransactionRequest
                        {
                            Hex = buildTransactionModel.Hex
                        })
                        .ReceiveJson<WalletSendTransactionModel>();

                    distributedSwapTransaction.TransactionSent = true;
                    distributedSwapTransaction.TransactionSentHash = sendActionResult.TransactionId.ToString();

                    Console.WriteLine($"Swap transaction built and sent to {distributedSwapTransaction.StraxAddress}:{Money.Satoshis(distributedSwapTransaction.SenderAmount).ToUnit(MoneyUnit.BTC)}");

                    // Append to the file.
                    using (FileStream stream = File.Open(Path.Combine(this.swapFilePath, distributedSwapTransactionsFile), FileMode.Append))
                    using (var writer = new StreamWriter(stream))
                    using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csv.WriteRecord(distributedSwapTransaction);
                        csv.NextRecord();
                    }

                    this.distributedSwapTransactions.Add(distributedSwapTransaction);

                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    break;
                }
                finally
                {
                }
            }
        }
    }
}
