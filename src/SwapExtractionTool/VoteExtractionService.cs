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
        private readonly Dictionary<string, CollateralVote> collateralVotes = new Dictionary<string, CollateralVote>();
        private readonly BlockExplorerClient blockExplorerClient;

        public VoteExtractionService(int stratisNetworkApiPort, Network straxNetwork, BlockExplorerClient blockExplorerClient) : base(stratisNetworkApiPort, straxNetwork)
        {
            this.blockExplorerClient = blockExplorerClient;
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
            var votes = this.collateralVotes.Values;

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

                // Before checking if it's a vote, check if it's a swap transaction as we now know it has an OP_RETURN.
                // Ignore any transactions that have inconsequential OP_RETURN values.
                if (opReturnOutput.Value >= 1.0m)
                {
                    // For the purposes of speeding up this search, it doesn't matter per se whether the burn transaction has a valid destination address in it.

                    TransactionModel tx = this.blockExplorerClient.GetTransaction(transaction.TxId);

                    // Check the inputs of the transaction to see if it was funded by one of the vote addresses.
                    string address = null;
                    foreach (In input in tx._in)
                    {
                        if (input.hash == null)
                            continue;

                        // The block explorer calls this 'hash' but it is in fact the address that funded the input.
                        // It is possible that several voting addresses consolidated together in one swap transaction, so just take the first one.
                        if (this.collateralVotes.ContainsKey(input.hash))
                        {
                            if (address == null)
                            {
                                // We will assign the entire burn amount to the first found voting address.
                                address = input.hash;
                            }
                            else
                            {
                                // However, we have to recompute the balance of all the vote addresses present in the inputs that we are not going to assign the burn to.
                                // This is because they will not necessarily be revisited later if there are no further transactions affecting them.
                                // We presume that since they are participating in a burn subsequent to their initial vote, their balance will drop.
                                if (!address.Equals(input.hash))
                                {
                                    AddressBalancesResult balance = await $"http://localhost:{this.StratisNetworkApiPort}/api"
                                        .AppendPathSegment($"blockstore/{BlockStoreRouteEndPoint.GetAddressesBalances}")
                                        .SetQueryParams(new { addresses = input.hash, minConfirmations = 0 })
                                        .GetJsonAsync<AddressBalancesResult>();

                                    Console.WriteLine($"Reset balance for '{input.hash}' to {balance.Balances[0].Balance} due to burn transaction {transaction.TxId} at height {blockHeight}");

                                    this.collateralVotes[input.hash].Balance = balance.Balances[0].Balance;
                                }
                            }
                        }
                    }

                    if (address != null)
                    {
                        this.collateralVotes[address].BlockHeight = blockHeight;
                        this.collateralVotes[address].Balance = Money.Coins(opReturnOutput.Value);

                        Console.WriteLine($"Detected that address '{address}' burnt {opReturnOutput.Value} via transaction {transaction.TxId} at height {blockHeight}");

                        // We can now skip checking if this output was a vote.
                        continue;
                    }
                }

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

                    if (!this.collateralVotes.ContainsKey(potentialStratAddress))
                    {
                        Console.WriteLine($"Collateral vote found for {potentialStratAddress} at height {blockHeight}; Selection '{collateralVote}'; Balance {determinedBalance}");
                        
                        this.collateralVotes.Add(potentialStratAddress, new CollateralVote() { Address = potentialStratAddress, Balance = determinedBalance, Selection = collateralVote, BlockHeight = blockHeight });
                    }
                    else
                    {
                        Console.WriteLine($"Updating existing vote for {potentialStratAddress} at height {blockHeight}; Selection '{collateralVote}'; Balance {determinedBalance}");
                        
                        this.collateralVotes[potentialStratAddress] = new CollateralVote() { Address = potentialStratAddress, Balance = determinedBalance, Selection = collateralVote, BlockHeight = blockHeight };
                    }
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
