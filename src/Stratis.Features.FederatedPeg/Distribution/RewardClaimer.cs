using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Signals;

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
    public class RewardClaimer : IDisposable
    {
        private readonly IBroadcasterManager broadcasterManager;
        private readonly ChainIndexer chainIndexer;
        private readonly IConsensusManager consensusManager;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly ISignals signals;

        private readonly SubscriptionToken blockConnectedSubscription;

        public RewardClaimer(IBroadcasterManager broadcasterManager, ChainIndexer chainIndexer, IConsensusManager consensusManager, ILoggerFactory loggerFactory, Network network, ISignals signals)
        {
            this.broadcasterManager = broadcasterManager;
            this.chainIndexer = chainIndexer;
            this.consensusManager = consensusManager;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.signals = signals;

            this.blockConnectedSubscription = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);
        }

        private void OnBlockConnected(BlockConnected blockConnected)
        {
            // Get the minimum stake confirmations for the current network.
            int minStakeConfirmations = ((PosConsensusOptions)this.network.Consensus.Options).GetStakeMinConfirmations(this.chainIndexer.Height, this.network);

            // Take a local copy of the tip.
            ChainedHeader chainTip = this.chainIndexer.Tip;

            if (chainTip.Height < minStakeConfirmations)
            {
                // If the chain is not at least minStakeConfirmations long then just do nothing.
                return;
            }

            // Get the block that is minStakeConfirmations behind the current tip.
            ChainedHeader chainedHeader = this.chainIndexer.GetHeader(chainTip.Height - minStakeConfirmations);

            Block maturedBlock = chainedHeader.Block;
            if (maturedBlock == null)
                maturedBlock = this.consensusManager.GetBlockData(maturedBlock.GetHash()).Block;

            // If we still don't have the block data, just return.
            if (maturedBlock == null)
            {
                this.logger.LogDebug("Consensus does not have the block data for '{0}'", chainedHeader);
                return;
            }

            // As this runs on the mainchain we presume there will be a coinstake transaction in the block (but during the PoW era there obviously may not be).
            // If not, just do nothing with this block.
            if (maturedBlock.Transactions.Count < 2 || !maturedBlock.Transactions[1].IsCoinStake)
                return;

            // We are only interested in the coinstake, as it is the only transaction that we expect to contain outputs paying the reward script.
            Transaction coinStake = maturedBlock.Transactions[1];

            // Identify any outputs paying the reward script a nonzero amount.
            TxOut[] rewardOutputs = coinStake.Outputs.Where(o => o.ScriptPubKey == StraxCoinstakeRule.CirrusRewardScript && o.Value != 0).ToArray();

            // This shouldn't be the case but check anyway.
            if (rewardOutputs.Length == 0)
                return;

            // Build a transaction using these inputs, paying the federation.
            var builder = new TransactionBuilder(this.network);

            foreach (TxOut txOutput in rewardOutputs)
            {
                // The reward script is P2SH, so we need to inform the builder of the corresponding redeem script to enable it to be spent.
                var coin = ScriptCoin.Create(this.network, coinStake, txOutput, StraxCoinstakeRule.CirrusRewardScriptRedeem);
                builder.AddCoins(coin);
            }

            // An OP_RETURN for a dummy Cirrus address that tells the sidechain federation they can distribute the transaction.
            builder.Send(StraxCoinstakeRule.CirrusTransactionTag, Money.Zero);

            // The mempool will accept a zero-fee transaction as long as it matches this structure, paying to the federation.
            builder.Send(this.network.Federations.GetOnlyFederation().MultisigScript, rewardOutputs.Sum(o => o.Value));

            Transaction builtTransaction = builder.BuildTransaction(true);

            TransactionPolicyError[] errors = builder.Check(builtTransaction);

            if (errors.Length > 0)
            {
                foreach (TransactionPolicyError error in errors)
                    this.logger.LogWarning("Unable to validate reward claim transaction '{0}', error: {1}", builtTransaction.ToHex(), error.ToString());

                // Not much further can be done at this point.
                return;
            }

            // It does not really matter whether the reward has been claimed already, as the transaction will simply be rejected by the other nodes on the network if it has.
            // So just broadcast it anyway.
            this.broadcasterManager.BroadcastTransactionAsync(builtTransaction);
        }

        public void Dispose()
        {
            if (this.blockConnectedSubscription != null)
            {
                this.signals.Unsubscribe(this.blockConnectedSubscription);
            }
        }
    }
}
