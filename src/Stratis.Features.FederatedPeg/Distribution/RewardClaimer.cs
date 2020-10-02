using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Signals;

namespace Stratis.Features.FederatedPeg.Distribution
{
    /// <summary>
    /// Automatically constructs cross-chain transfer transactions for the Cirrus block rewards.
    /// This runs on the mainchain only.
    /// </summary>
    public class RewardClaimer
    {
        private readonly Network network;
        private readonly ChainIndexer chainIndexer;
        private readonly ISignals signals;
        private readonly IBroadcasterManager broadcasterManager;
        private readonly ILogger logger;

        private SubscriptionToken blockConnectedSubscription;

        // Rewards have to be 'sent' over to the sidechain by spending the anyone-can-spend reward outputs from each mainchain block.
        // It is already enforced by consensus that these outputs can only be spent directly into the federation multisig.
        // Therefore any node can initiate this cross-chain transfer. We just put it into the federation nodes as they are definitely running mainchain nodes.
        // The miners could run nodes themselves to claim the reward, for instance.

        // The reward does not have to be claimed every block, in future it could be batched a few blocks at a time to save a small amount of transaction throughput/fees if desired.

        public RewardClaimer(Network network, ChainIndexer chainIndexer, ISignals signals, IBroadcasterManager broadcasterManager, ILoggerFactory loggerFactory)
        {
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.signals = signals;
            this.broadcasterManager = broadcasterManager;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.blockConnectedSubscription = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);
        }

        private void OnBlockConnected(BlockConnected blockConnected)
        {
            if (!(this.network.Consensus.Options is PosConsensusOptions options))
                return;

            // Get the minimum stake confirmations for the current network.
            int minStakeConfirmations = options.GetStakeMinConfirmations(this.chainIndexer.Height, this.network);

            if (this.chainIndexer.Height < minStakeConfirmations)
            {
                // If the chain is not at least minStakeConfirmations long then just do nothing.
                return;
            }

            // Get the block that is minStakeConfirmations behind the current tip.
            ChainedHeader chainedHeader = this.chainIndexer.GetHeader(this.chainIndexer.Height - minStakeConfirmations);

            Block maturedBlock = chainedHeader.Block;

            // As this runs on the mainchain we presume there will be a coinstake transaction in the block (but during the PoW era there obviously may not be).
            // If not, just do nothing with this block.
            if (maturedBlock.Transactions.Count < 2 || !blockConnected.ConnectedBlock.Block.Transactions[1].IsCoinStake)
                return;

            // We are only interested in the coinstake, as it is the only transaction that we expect to contain outputs paying the reward script.
            Transaction coinStake = blockConnected.ConnectedBlock.Block.Transactions[1];

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

            // TODO: Revisit the handling of fees here
            builder.Send(this.network.FederationMultisigScript, rewardOutputs.Sum(o => o.Value));

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
    }
}
