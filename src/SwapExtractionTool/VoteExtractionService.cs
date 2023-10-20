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

        public VoteExtractionService(string apiUrl, int stratisNetworkApiPort) : base(apiUrl, stratisNetworkApiPort)
        {
        }

        public async Task RunAsync(VoteType voteType, int startBlock, int endBlock)
        {
            if (endBlock == -1)
            {
                endBlock = await RetrieveBlockHeightAsync();
            }

            Console.WriteLine($"Scanning for {voteType} votes from block {startBlock} to {endBlock}...");

            for (int height = startBlock; height < endBlock; height++)
            {
                if (height % 100 == 0)
                {
                    Console.WriteLine($"Checking block {height}...");
                }

                BlockTransactionDetailsModel block = await RetrieveBlockAtHeightAsync(height);
                if (block == null)
                    break;

                if (voteType == VoteType.SwapVote)
                    await ProcessBlockForSwapVoteTransactionsAsync(block, height);
            }
            
            if (voteType == VoteType.SwapVote)
                CountSwapVotes();
        }

        private void CountSwapVotes()
        {
            IEnumerable<IGrouping<string, CastVote>> grouped = this.castVotes.GroupBy(a => a.Address);
            var votes = new List<CastVote>();
            foreach (IGrouping<string, CastVote> group in grouped)
            {
                IOrderedEnumerable<CastVote> finalVote = group.OrderByDescending(t => t.BlockHeight);

                if (group.Count() > 1)
                {
                    Console.WriteLine($"Address {finalVote.First().Address} voted {group.Count()} times, using most recent vote at height {finalVote.First().BlockHeight} with current balance {finalVote.First().Balance}");
                }

                votes.Add(finalVote.First());
            }

            var totalWeight = Money.Satoshis(votes.Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);
            var noWeight = Money.Satoshis(votes.Where(v => !v.InFavour).Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);
            var yesWeight = Money.Satoshis(votes.Where(v => v.InFavour).Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC);

            Console.WriteLine($"Total Weight: {totalWeight} STRAX");
            Console.WriteLine($"Total No Weight: {(totalWeight > 0 ? (noWeight / totalWeight * 100) : 0).ToString("F")}% [{noWeight}]");
            Console.WriteLine($"Total Yes Weight: {(totalWeight > 0 ? (yesWeight / totalWeight * 100) : 0).ToString("F")}% [{yesWeight}]");
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
                        // Verify the sender address is a valid Strax address
                        var potentialStratAddress = potentialVote.Substring(2);
                        ValidatedAddress validateResult = await $"{this.StratisNetworkApiUrl}:{this.StratisNetworkApiPort}/api"
                            .AppendPathSegment("node/validateaddress")
                            .SetQueryParams(new { address = potentialStratAddress })
                            .GetJsonAsync<ValidatedAddress>();

                        if (!validateResult.IsValid)
                        {
                            Console.WriteLine($"Invalid STRAT address: '{potentialStratAddress}'");
                            continue;
                        }

                        AddressBalancesResult balance = await $"{this.StratisNetworkApiUrl}:{this.StratisNetworkApiPort}/api"
                                .AppendPathSegment($"blockstore/{BlockStoreRouteEndPoint.GetAddressesBalances}")
                                .SetQueryParams(new { addresses = potentialStratAddress, minConfirmations = 0 })
                                .GetJsonAsync<AddressBalancesResult>();

                        if (isVoteValue == "0")
                        {
                            this.castVotes.Add(new CastVote() { Address = potentialStratAddress, Balance = balance.Balances[0].Balance, InFavour = false, BlockHeight = blockHeight });
                            Console.WriteLine($"'No' vote found at height {blockHeight}. Address {potentialStratAddress} current balance {balance.Balances[0].Balance}");
                        }

                        if (isVoteValue == "1")
                        {
                            this.castVotes.Add(new CastVote() { Address = potentialStratAddress, Balance = balance.Balances[0].Balance, InFavour = true, BlockHeight = blockHeight });
                            Console.WriteLine($"'Yes' vote found at height {blockHeight}. Address {potentialStratAddress} current balance {balance.Balances[0].Balance}");
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
        SwapVote,
    }
}
