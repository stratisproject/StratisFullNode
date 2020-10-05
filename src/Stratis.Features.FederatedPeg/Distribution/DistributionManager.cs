using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Features.Collateral;
using Stratis.Features.FederatedPeg.Wallet;

namespace Stratis.Features.FederatedPeg.Distribution
{
    /// <summary>
    /// Constructs the list of recipients for the mainchain reward sharing.
    /// Runs on the sidechain.
    /// </summary>
    public class DistributionManager : IDistributionManager
    {
        private const int DefaultEpoch = 240;
        
        private readonly Network network;
        private readonly ChainIndexer chainIndexer;
        private readonly ILogger logger;

        private int epoch;
        private int lastDistributionHeight;

        // The reward each miner receives upon distribution is computed as a proportion of the overall accumulated reward since the last distribution.
        // The proportion is based on how many blocks that miner produced in the period (each miner is identified by their block's coinbase's scriptPubKey).
        // It is therefore not in any miner's advantage to delay or skip producing their blocks as it will affect their proportion of the produced blocks.
        // We pay no attention to whether a miner has been kicked since the last distribution or not.
        // If they produced an accepted block, they get their reward.

        public DistributionManager(Network network, ChainIndexer chainIndexer, ILoggerFactory loggerFactory)
        {
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.epoch = this.network.Consensus.MaxReorgLength == 0 ? DefaultEpoch : (int)this.network.Consensus.MaxReorgLength;
            this.lastDistributionHeight = 0;
        }

        private int GetDistributionEpochStart(int blockHeight)
        {
            // This is a special case which will not be the case on the live network.
            if (blockHeight < this.epoch)
            {
                return 0;
            }

            return blockHeight - (blockHeight % this.epoch) - this.epoch;
        }

        private int GetDistributionEpochEnd(int blockHeight)
        {
            return blockHeight - (blockHeight % this.epoch);
        }

        /// <inheritdoc />
        public List<Recipient> Distribute(int mainChainHeight, Money totalReward)
        {
            ChainedHeader tip = this.chainIndexer.Tip;

            // We need to determine which block on the sidechain contains a commitment to the height of the mainchain block that originated the reward transfer.
            // We otherwise do not have a common reference point from which to compute the epoch.
            // Look back at most 2x epoch lengths to avoid searching from genesis every time.

            // To avoid miners trying to disrupt the chain by committing to the same height in multiple blocks, we loop forwards and use the first occurrence
            // of a commitment with height >= the search height.

            int blockHeight = 0;

            ChainedHeader currentHeader = this.chainIndexer.Height > (2 * this.epoch) ? this.chainIndexer.GetHeader(this.chainIndexer.Height - (2 * this.epoch)) : this.chainIndexer.Genesis;

            // Cap the maximum number of iterations.
            int iterations = this.chainIndexer.Height - currentHeader.Height;

            var encoder = new CollateralHeightCommitmentEncoder(this.logger);

            for (int i = 0; i < iterations; i++)
            {
                int? commitmentHeight = encoder.DecodeCommitmentHeight(currentHeader.Block.Transactions[0]);

                if (commitmentHeight == null)
                    continue;

                if (commitmentHeight >= mainChainHeight)
                {
                    blockHeight = commitmentHeight.Value;
                    
                    break;
                }

                // We need to ensure we walk forwards along the headers to the original tip, so if there is more than one, find the one on the common fork.
                foreach (ChainedHeader candidateHeader in currentHeader.Next)
                {
                    if (candidateHeader.FindFork(tip) != null)
                        currentHeader = candidateHeader;
                }
            }

            // Get the set of miners (more specifically, the scriptPubKeys they generated blocks with) to distribute rewards to.
            // Based on the computed 'common block height' we define the distribution epoch:
            int startHeight = this.GetDistributionEpochStart(blockHeight);
            int endHeight = this.GetDistributionEpochEnd(blockHeight);

            var blocksMinedEach = new Dictionary<Script, long>();

            long totalBlocks = 0;
            for (int i = startHeight; i <= endHeight; i++)
            {
                ChainedHeader chainedHeader = this.chainIndexer.GetHeader(i);
                Block block = chainedHeader.Block;

                Transaction coinBase = block.Transactions.First();

                // Regard the first 'spendable' scriptPubKey in the coinbase as belonging to the miner's wallet.
                // This avoids trying to recover the pubkey from the block signature.
                Script minerScript = coinBase.Outputs.First(o => !o.ScriptPubKey.IsUnspendable).ScriptPubKey;

                if (!blocksMinedEach.TryGetValue(minerScript, out long minerBlockCount))
                    minerBlockCount = 0;

                blocksMinedEach[minerScript] = ++minerBlockCount;
                totalBlocks++;
            }

            var recipients = new List<Recipient>();

            foreach (Script scriptPubKey in blocksMinedEach.Keys)
            {
                Money amount = totalReward * blocksMinedEach[scriptPubKey] / totalBlocks;

                recipients.Add(new Recipient() { Amount = amount, ScriptPubKey = scriptPubKey});
            }

            return recipients;
        }
    }
}
