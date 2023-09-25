using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.FederatedPeg.Distribution
{
    /// <summary>
    /// Automatically constructs cross-chain transfer transactions for the Cirrus block rewards (mainchain execution only).
    /// <para>
    /// Rewards have to be 'sent' over to the sidechain by spending the anyone-can-spend reward outputs from each mainchain block.
    /// It is already enforced by consensus that these outputs can only be spent directly into the federation multisig.
    /// Therefore any node can initiate this cross-chain transfer. We just put the functionality into the federation nodes as they are definitely running mainchain nodes.
    /// The miners could run nodes themselves to claim the reward, for instance.
    /// The reward cross chain transfer does not have to be initiated every block, in future it could be batched a few blocks at a time to save a small amount of transaction throughput/fees if desired.
    /// </para>
    /// </summary>
    public sealed class RewardClaimer : IDisposable
    {
        private const string LastDistributionHeightKey = "rewardClaimerLastDistributionHeight";
        private const int UnspentRewardsToTrack = 200;

        private readonly IBroadcasterManager broadcasterManager;
        private readonly ChainIndexer chainIndexer;
        private readonly IConsensusManager consensusManager;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;
        private readonly IKeyValueRepository keyValueRepository;
        private int lastDistributionHeight;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly ISignals signals;

        private readonly SubscriptionToken blockConnectedSubscription;
        private readonly SubscriptionToken blockDisconnectedSubscription;

        private readonly List<ScriptCoin> unspentRewardOutputs;
        private readonly Dictionary<string, ScriptCoin> unspentRewardLookup;

        public RewardClaimer(
            IBroadcasterManager broadcasterManager,
            ChainIndexer chainIndexer,
            IConsensusManager consensusManager,
            IInitialBlockDownloadState initialBlockDownloadState,
            IKeyValueRepository keyValueRepository,
            Network network,
            ISignals signals)
        {
            this.broadcasterManager = broadcasterManager;
            this.chainIndexer = chainIndexer;
            this.consensusManager = consensusManager;
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.keyValueRepository = keyValueRepository;
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.signals = signals;

            this.unspentRewardOutputs = new List<ScriptCoin>();
            this.unspentRewardLookup = new Dictionary<string, ScriptCoin>();

            // TODO: Populate unspentRewardOutputs for the past 100-200 blocks

            this.blockConnectedSubscription = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);
            this.blockDisconnectedSubscription = this.signals.Subscribe<BlockDisconnected>(this.OnBlockDisconnected);

            LoadLastDistributionHeight();
        }

        public Transaction BuildRewardTransaction(bool batchRewards)
        {
            // Get the minimum stake confirmations for the current network.
            int minStakeConfirmations = ((PosConsensusOptions)this.network.Consensus.Options).GetStakeMinConfirmations(this.chainIndexer.Height, this.network);

            // Take a local copy of the tip.
            ChainedHeader chainTip = this.chainIndexer.Tip;

            if (chainTip.Height < minStakeConfirmations)
            {
                // If the chain is not at least minStakeConfirmations long then just do nothing.
                return null;
            }

            if (batchRewards)
            {
                if (!this.unspentRewardOutputs.Any())
                    return null;

                // The list will be cleared automatically when the spent outputs appear in a block, so we just have to pass it in here.
                return BuildRewardTransaction(this.unspentRewardOutputs);
            }

            // Get the block that is minStakeConfirmations behind the current tip.
            Block maturedBlock = GetMaturedBlock(this.chainIndexer.Tip.Height - minStakeConfirmations);
            if (maturedBlock == null)
                return null;

            // We are only interested in the coinstake, as it is the only transaction that we expect to contain outputs paying the reward script.
            List<ScriptCoin> coins = GetRewardCoins(maturedBlock.Transactions[1]);
            if (!coins.Any())
                return null;

            return BuildRewardTransaction(coins);
        }

        private List<ScriptCoin> GetRewardCoins(Transaction coinStake)
        {
            var coins = new List<ScriptCoin>();

            // Identify any outputs paying the reward script a nonzero amount.
            TxOut[] rewardOutputs = coinStake.Outputs.Where(o => o.ScriptPubKey == StraxCoinstakeRule.CirrusRewardScript && o.Value != 0).ToArray();

            // This shouldn't be the case but check anyway.
            if (rewardOutputs.Length != 0)
            {
                foreach (TxOut txOutput in rewardOutputs)
                {
                    // The reward script is P2SH, so we need to inform the builder of the corresponding redeem script to enable it to be spent.
                    var coin = ScriptCoin.Create(this.network, coinStake, txOutput, StraxCoinstakeRule.CirrusRewardScriptRedeem);

                    coins.Add(coin);
                }
            }

            return coins;
        }

        /// <summary>
        /// Build a transaction using these inputs, paying the federation.
        /// </summary>
        /// <param name="coins">The set of coins to be spend.</param>
        /// <returns>The reward transaction.</returns>
        private Transaction BuildRewardTransaction(List<ScriptCoin> coins)
        {
            var builder = new TransactionBuilder(this.network);

            // Add the coins to spend.
            builder.AddCoins(coins);

            // An OP_RETURN for a dummy Cirrus address that tells the sidechain federation they can distribute the transaction.
            builder.Send(StraxCoinstakeRule.CirrusTransactionTag(this.network.CirrusRewardDummyAddress), Money.Zero);

            // The mempool will accept a zero-fee transaction as long as it matches this structure, paying to the federation.
            builder.Send(this.network.Federations.GetOnlyFederation().MultisigScript.PaymentScript, coins.Sum(o => o.Amount));

            Transaction builtTransaction = builder.BuildTransaction(true);

            // Filter out FeeTooLowPolicyError errors as reward transaction's will not contain any fees.
            IEnumerable<TransactionPolicyError> errors = builder.Check(builtTransaction).Where(e => !(e is FeeTooLowPolicyError));

            if (errors.Any())
            {
                foreach (TransactionPolicyError error in errors)
                    this.logger.LogWarning("Unable to validate reward claim transaction '{0}', error: {1}", builtTransaction.ToHex(), error.ToString());

                // Not much further can be done at this point.
                return null;
            }

            this.logger.LogInformation($"Reward distribution transaction '{builtTransaction.GetHash()}' built; sending {builtTransaction.TotalOut} to federation '{this.network.Federations.GetOnlyFederation().MultisigScript.PaymentScript}'.");
            return builtTransaction;
        }

        private Block GetMaturedBlock(int applicableHeight)
        {
            ChainedHeader chainedHeader = this.chainIndexer.GetHeader(applicableHeight);

            Block maturedBlock = chainedHeader.Block;
            if (maturedBlock == null)
                maturedBlock = this.consensusManager.GetBlockData(chainedHeader.HashBlock).Block;

            // If we still don't have the block data, just return.
            if (maturedBlock == null)
            {
                this.logger.LogDebug("Consensus does not have the block data for '{0}'", chainedHeader);
                return null;
            }

            // As this runs on the mainchain we presume there will be a coinstake transaction in the block (but during the PoW era there obviously may not be).
            // If not, just do nothing with this block.
            if (maturedBlock.Transactions.Count < 2 || !maturedBlock.Transactions[1].IsCoinStake)
                return null;

            return maturedBlock;
        }

        private void OnBlockConnected(BlockConnected blockConnected)
        {
            if (blockConnected.ConnectedBlock.Block.Transactions.Count > 1 && blockConnected.ConnectedBlock.Block.Transactions[1].IsCoinStake)
            {
                foreach (ScriptCoin reward in GetRewardCoins(blockConnected.ConnectedBlock.Block.Transactions[1]))
                {
                    if (reward.Amount == Money.Zero || reward.Outpoint.IsNull)
                        continue;

                    this.unspentRewardOutputs.Add(reward);
                    this.unspentRewardLookup.Add($"{reward.Outpoint.Hash}:{reward.Outpoint.N}", reward);
                }
            }

            // Remove reward outputs that have been spent as inputs in transactions.
            foreach (Transaction tx in blockConnected.ConnectedBlock.Block.Transactions)
            {
                if (tx.IsCoinBase)
                    continue;

                foreach (TxIn txIn in tx.Inputs)
                {
                    if (txIn.PrevOut.IsNull)
                        continue;

                    string prevOut = $"{txIn.PrevOut.Hash}:{txIn.PrevOut.N}";

                    if (this.unspentRewardLookup.TryGetValue(prevOut, out ScriptCoin toRemove))
                    {
                        this.unspentRewardOutputs.Remove(toRemove);
                        this.unspentRewardLookup.Remove(prevOut);
                    }
                }
            }

            TrimUnspentRewards();

            if (this.initialBlockDownloadState.IsInitialBlockDownload())
                return;

            // Check if the current block is after reward batching activation height.
            if (blockConnected.ConnectedBlock.ChainedHeader.Height >= this.network.RewardClaimerBatchActivationHeight)
            {
                // Check if the block connected height is equal or below the last distribution height.
                // This is could happen due to a reorg and therefore we do nothing.
                if (blockConnected.ConnectedBlock.ChainedHeader.Height <= (this.lastDistributionHeight + 1))
                {
                    this.logger.LogInformation($"Reward claiming skipped as block window already processed; Block connected at {blockConnected.ConnectedBlock.ChainedHeader.Height}; Last distribution at {this.lastDistributionHeight}.");
                    return;
                }

                // Check if the reward claimer should be triggered.
                if (blockConnected.ConnectedBlock.ChainedHeader.Height > this.network.RewardClaimerBatchActivationHeight &&
                    blockConnected.ConnectedBlock.ChainedHeader.Height % this.network.RewardClaimerBlockInterval == 0)
                {
                    // If this node "skipped" a reward claimer run, then just bring the last claimer height up to
                    // the last applicable round as the assumption is that some other node would have processed the reward claiming
                    // properly.
                    if (blockConnected.ConnectedBlock.ChainedHeader.Height - (this.lastDistributionHeight + 1) > this.network.RewardClaimerBlockInterval)
                    {
                        this.lastDistributionHeight = blockConnected.ConnectedBlock.ChainedHeader.Height - this.network.RewardClaimerBlockInterval - 1;
                        this.logger.LogInformation($"[Reward Batching] The last reward window was skipped, resetting to {this.lastDistributionHeight}.");
                    }

                    this.logger.LogInformation($"[Reward Batching] Triggered at height {blockConnected.ConnectedBlock.ChainedHeader.Height}.");

                    BuildAndCompleteRewardClaim(true, this.lastDistributionHeight + this.network.RewardClaimerBlockInterval);
                }
                else
                    this.logger.LogInformation($"[Reward Batching] The next distribution will be triggered at block {this.lastDistributionHeight + 1 + this.network.RewardClaimerBlockInterval}.");
            }
            else
            {
                this.logger.LogInformation($"Per block reward claiming in effect until block {this.network.RewardClaimerBatchActivationHeight} (rewards are not batched).");
                BuildAndCompleteRewardClaim(false, blockConnected.ConnectedBlock.ChainedHeader.Height);
            }
        }

        private void OnBlockDisconnected(BlockDisconnected blockDisconnected)
        {
            /* Adding back reward outputs upon reorg is a bit tricky, as we don't have the amounts of the transaction inputs without an additional data source.

            foreach (Transaction tx in blockDisconnected.DisconnectedBlock.Block.Transactions)
            {
                if (tx.IsCoinBase || tx.IsCoinStake)
                    continue;

                // Check if the federation is being paid.
                if (tx.Outputs.All(t => t.ScriptPubKey != this.network.Federations.GetOnlyFederation().MultisigScript))
                    continue;

                // Paying the federation is an insufficient indication that it is a reward claim cross-chain transfer, so we need to inspect the scriptSigs to see if they are for the anyone-can-spend script.
                foreach (TxIn txIn in tx.Inputs)
                {
                    if (txIn.GetSigner(this.network) != StraxCoinstakeRule.CirrusRewardScriptRedeem.Hash)
                        continue;

                    // Recreating the ScriptCoin is a lot messier in this case as we can't necessarily look up the original transaction without guaranteeing txindex is available.
                    ScriptCoin reward = ScriptCoin.Create(this.network, txIn.PrevOut.Hash, txIn.PrevOut.N, value, StraxCoinstakeRule.CirrusRewardScript, StraxCoinstakeRule.CirrusRewardScriptRedeem);

                    this.unspentRewardOutputs.Add(reward);
                    this.unspentRewardLookup.Add($"{reward.Outpoint.Hash}:{reward.Outpoint.N}", reward);
                }
            }

            TrimUnspentRewards();
            */
        }

        private void TrimUnspentRewards()
        {
            if (this.unspentRewardOutputs.Count <= UnspentRewardsToTrack)
                return;

            int excess = this.unspentRewardOutputs.Count - UnspentRewardsToTrack;

            List<ScriptCoin> excessEntries = this.unspentRewardOutputs.TakeLast(excess).ToList();

            foreach (ScriptCoin excessEntry in excessEntries)
            {
                this.unspentRewardOutputs.Remove(excessEntry);
                this.unspentRewardLookup.Remove($"{excessEntry.Outpoint.Hash}:{excessEntry.Outpoint.N}");
            }
        }

        private void BuildAndCompleteRewardClaim(bool batchRewards, int newLastDistributionHeight)
        {
            Transaction transaction = BuildRewardTransaction(batchRewards);

            if (transaction == null)
                return;

            this.lastDistributionHeight = newLastDistributionHeight;

            SaveLastDistributionHeight();

            // It does not really matter whether the reward has been claimed already, as the transaction will simply be rejected by the other nodes on the network if it has.
            // So just broadcast it anyway.
            this.broadcasterManager.BroadcastTransactionAsync(transaction);
        }

        private void LoadLastDistributionHeight()
        {
            if (this.network.RewardClaimerBatchActivationHeight == 0 && !this.network.IsRegTest())
                throw new Exception("The network's reward claimer height for version 2 has not been set.");

            // Load from database
            this.lastDistributionHeight = this.keyValueRepository.LoadValueJson<int>(LastDistributionHeightKey);


            // If this has never been loaded, set this to the activation height.
            if (this.lastDistributionHeight == 0)
                this.lastDistributionHeight = this.network.RewardClaimerBatchActivationHeight;

            this.logger.LogInformation($"Last reward distribution height set to {this.lastDistributionHeight}.");
        }

        private void SaveLastDistributionHeight()
        {
            this.keyValueRepository.SaveValueJson(LastDistributionHeightKey, this.lastDistributionHeight);
            this.logger.LogInformation($"Last reward distribution saved as {this.lastDistributionHeight}.");
        }

        public void Dispose()
        {
            if (this.blockConnectedSubscription != null)
                this.signals.Unsubscribe(this.blockConnectedSubscription);

            if (this.blockDisconnectedSubscription != null)
                this.signals.Unsubscribe(this.blockDisconnectedSubscription);
        }
    }
}
