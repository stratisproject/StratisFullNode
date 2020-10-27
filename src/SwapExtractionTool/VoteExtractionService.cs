using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.Controllers;
using Stratis.Bitcoin.Features.BlockStore.Models;

namespace SwapExtractionTool
{
    public sealed class VoteExtractionService : ExtractionBase
    {
        private readonly List<CastVote> castVotes = new List<CastVote>();
        private readonly List<CollateralVote> collateralVotes = new List<CollateralVote>();

        public VoteExtractionService(int stratisNetworkApiPort, Network straxNetwork) : base(stratisNetworkApiPort, straxNetwork)
        {
        }

        public async Task RunAsync(VoteType voteType, int startBlock)
        {
            Console.WriteLine($"Scanning for {voteType} votes...");

            for (int height = startBlock; height < EndHeight; height++)
            {
                BlockTransactionDetailsModel block = await RetrieveBlockAtHeightAsync(height);
                if (block == null)
                    break;

                if (voteType == VoteType.CollateralVote)
                    await ProcessBlockForCollateralVoteTransactionsAsync(block, height);

                if (voteType == VoteType.SwapVote)
                    await ProcessBlockForSwapVoteTransactionsAsync(block, height);
            }

            if (voteType == VoteType.CollateralVote)
                CountCollateralVotes();

            if (voteType == VoteType.SwapVote)
                CountSwapVotes();
        }

        private void CountCollateralVotes()
        {
            IEnumerable<IGrouping<string, CollateralVote>> grouped = this.collateralVotes.GroupBy(a => a.Address);
            var votes = new List<CollateralVote>();
            foreach (IGrouping<string, CollateralVote> group in grouped)
            {
                IOrderedEnumerable<CollateralVote> finalVote = group.OrderByDescending(t => t.BlockHeight);
                votes.Add(finalVote.First());
            }

            var totalWeight = Money.Satoshis(votes.Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);
            var aWeight = Money.Satoshis(votes.Where(v => v.Selection == "A").Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);
            var bWeight = Money.Satoshis(votes.Where(v => v.Selection == "B").Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);
            var cWeight = Money.Satoshis(votes.Where(v => v.Selection == "C").Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);
            var dWeight = Money.Satoshis(votes.Where(v => v.Selection == "D").Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);
            var eWeight = Money.Satoshis(votes.Where(v => v.Selection == "E").Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);

            Console.WriteLine($"Total Weight: {totalWeight} STRAT");

            if (totalWeight == 0)
                return;

            Console.WriteLine($"Total A Weight: {(aWeight / totalWeight * 100).ToString("F")}% [{aWeight}]");
            Console.WriteLine($"Total B Weight: {(bWeight / totalWeight * 100).ToString("F")}% [{bWeight}]");
            Console.WriteLine($"Total C Weight: {(cWeight / totalWeight * 100).ToString("F")}% [{cWeight}]");
            Console.WriteLine($"Total D Weight: {(dWeight / totalWeight * 100).ToString("F")}% [{dWeight}]");
            Console.WriteLine($"Total E Weight: {(eWeight / totalWeight * 100).ToString("F")}% [{eWeight}]");
        }

        private void CountSwapVotes()
        {
            IEnumerable<IGrouping<string, CastVote>> grouped = this.castVotes.GroupBy(a => a.Address);
            var votes = new List<CastVote>();
            foreach (IGrouping<string, CastVote> group in grouped)
            {
                IOrderedEnumerable<CastVote> finalVote = group.OrderByDescending(t => t.BlockHeight);
                votes.Add(finalVote.First());
            }

            var totalWeight = Money.Satoshis(votes.Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);
            var noWeight = Money.Satoshis(votes.Where(v => !v.InFavour).Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);
            var yesWeight = Money.Satoshis(votes.Where(v => v.InFavour).Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);

            Console.WriteLine($"Total Weight: {totalWeight} STRAT");
            Console.WriteLine($"Total No Weight: {(noWeight / totalWeight * 100).ToString("F")}% [{noWeight}]");
            Console.WriteLine($"Total Yes Weight: {(yesWeight / totalWeight * 100).ToString("F")}% [{yesWeight}]");
        }

        private async Task ProcessBlockForCollateralVoteTransactionsAsync(BlockTransactionDetailsModel block, int blockHeight)
        {
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

                    var isCollateralVote = potentialVote.Substring(1, 1);
                    if (isCollateralVote != "C")
                        continue;

                    var collateralVote = potentialVote.Substring(2, 1);
                    if (!new[] { "A", "B", "C", "D", "E" }.Contains(collateralVote))
                    {
                        Console.WriteLine($"Invalid vote found '{collateralVote}'; height {blockHeight}.");
                        continue;
                    }

                    // Verify the sender address is a valid Strat address
                    var potentialStratAddress = potentialVote.Substring(3);
                    ValidatedAddress validateResult = await $"http://localhost:{this.StratisNetworkApiPort}/api"
                        .AppendPathSegment("node/validateaddress")
                        .SetQueryParams(new { address = potentialStratAddress })
                        .GetJsonAsync<ValidatedAddress>();

                    if (!validateResult.IsValid)
                    {
                        Console.WriteLine($"Invalid STRAT address: '{potentialStratAddress}'");
                        continue;
                    }

                    AddressBalancesResult balance = await $"http://localhost:{this.StratisNetworkApiPort}/api"
                        .AppendPathSegment($"blockstore/{BlockStoreRouteEndPoint.GetAddressesBalances}")
                        .SetQueryParams(new { addresses = potentialStratAddress, minConfirmations = 0 })
                        .GetJsonAsync<AddressBalancesResult>();

                    Money determinedBalance = balance.Balances[0].Balance;

                    // Check if the last transaction that spends from the given address was a burn transaction.
                    // If so, the amount of the burn takes precedence for the purposes of the vote weight.
                    LastBalanceDecreaseTransactionModel lastBalanceDecreaseTransaction = await $"http://localhost:{this.StratisNetworkApiPort}/api"
                        .AppendPathSegment($"blockstore/{BlockStoreRouteEndPoint.GetLastBalanceDecreaseTransaction}")
                        .SetQueryParams(new { addresses = potentialStratAddress, minConfirmations = 0 })
                        .GetJsonAsync<LastBalanceDecreaseTransactionModel>();

                    if (lastBalanceDecreaseTransaction.BlockHeight > blockHeight)
                    {
                        foreach (Vout txOut in lastBalanceDecreaseTransaction.Transaction.VOut)
                        {
                            if (txOut.ScriptPubKey.Type == "nulldata" && Money.Coins(txOut.Value) > determinedBalance)
                            {
                                Console.WriteLine($"Detected that address '{potentialStratAddress}' burnt {determinedBalance} at height {lastBalanceDecreaseTransaction.BlockHeight}");

                                determinedBalance = Money.Coins(txOut.Value);
                            }
                        }
                    }

                    this.collateralVotes.Add(new CollateralVote() { Address = potentialStratAddress, Balance = determinedBalance, Selection = collateralVote, BlockHeight = blockHeight });
                    Console.WriteLine($"Collateral vote found at height {blockHeight}; Selection '{collateralVote}'");
                }
                catch (Exception)
                {
                }
            }
        }

        private async Task ProcessBlockForSwapVoteTransactionsAsync(BlockTransactionDetailsModel block, int blockHeight)
        {
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
                        ValidatedAddress validateResult = await $"http://localhost:{this.StratisNetworkApiPort}/api"
                            .AppendPathSegment("node/validateaddress")
                            .SetQueryParams(new { address = potentialStratAddress })
                            .GetJsonAsync<ValidatedAddress>();

                        if (!validateResult.IsValid)
                        {
                            Console.WriteLine($"Invalid STRAT address: '{potentialStratAddress}'");
                            continue;
                        }

                        AddressBalancesResult balance = await $"http://localhost:{this.StratisNetworkApiPort}/api"
                                .AppendPathSegment($"blockstore/{BlockStoreRouteEndPoint.GetAddressesBalances}")
                                .SetQueryParams(new { addresses = potentialStratAddress, minConfirmations = 0 })
                                .GetJsonAsync<AddressBalancesResult>();

                        if (isVoteValue == "0")
                        {
                            this.castVotes.Add(new CastVote() { Address = potentialStratAddress, Balance = balance.Balances[0].Balance, InFavour = false, BlockHeight = blockHeight });
                            Console.WriteLine($"'No' vote found at height {blockHeight}.");
                        }

                        if (isVoteValue == "1")
                        {
                            this.castVotes.Add(new CastVote() { Address = potentialStratAddress, Balance = balance.Balances[0].Balance, InFavour = true, BlockHeight = blockHeight });
                            Console.WriteLine($"'Yes' vote found at height {blockHeight}.");
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }

    public enum VoteType
    {
        CollateralVote,
        SwapVote,
    }
}
