using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.Models;

namespace SwapExtractionTool
{
    public sealed class VoteExtractionService : ExtractionBase
    {
        private readonly List<CastVote> castVotes = new List<CastVote>();

        public VoteExtractionService(int stratisNetworkApiPort, Network straxNetwork) : base(stratisNetworkApiPort, straxNetwork)
        {
        }

        public async Task RunAsync(int startBlock)
        {
            Console.WriteLine($"Scanning for votes...");

            for (int height = startBlock; height < EndHeight; height++)
            {
                BlockTransactionDetailsModel block = await RetrieveBlockAtHeightAsync(height);
                if (block == null)
                    break;

                await ProcessBlockForVoteTransactionsAsync(block, height);
            }

            IEnumerable<IGrouping<string, CastVote>> grouped = this.castVotes.GroupBy(a => a.Address);
            var finalVotes = new List<CastVote>();
            foreach (IGrouping<string, CastVote> group in grouped)
            {
                IOrderedEnumerable<CastVote> finalVote = group.OrderByDescending(t => t.BlockHeight);
                finalVotes.Add(finalVote.First());
            }

            Console.WriteLine($"Total No Votes: {finalVotes.Count(v => !v.InFavour)} [Weight : {Money.Satoshis(finalVotes.Where(v => !v.InFavour).Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC)}]");
            Console.WriteLine($"Total Yes Votes: {finalVotes.Count(v => v.InFavour)} [Weight : {Money.Satoshis(finalVotes.Where(v => v.InFavour).Sum(v => v.Balance)).ToUnit(MoneyUnit.BTC)}]");
        }

        private async Task ProcessBlockForVoteTransactionsAsync(BlockTransactionDetailsModel block, int blockHeight)
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
                        ValidatedAddress validateResult = await $"http://localhost:{StratisNetworkApiPort}/api"
                            .AppendPathSegment("node/validateaddress")
                            .SetQueryParams(new { address = potentialStratAddress })
                            .GetJsonAsync<ValidatedAddress>();

                        if (!validateResult.IsValid)
                        {
                            Console.WriteLine($"Invalid STRAT address: '{potentialStratAddress}'");
                            continue;
                        }

                        AddressBalancesResult balance = await $"http://localhost:{StratisNetworkApiPort}/api"
                                .AppendPathSegment("blockstore/getaddressesbalances")
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
}
