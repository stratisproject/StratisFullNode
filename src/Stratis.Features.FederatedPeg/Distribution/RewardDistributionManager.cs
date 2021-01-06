using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Features.FederatedPeg.Wallet;
using Stratis.Features.PoA.Collateral;

namespace Stratis.Features.FederatedPeg.Distribution
{
    /// <summary>
    /// Constructs the list of recipients for the mainchain reward sharing.
    /// Only runs on the sidechain.
    /// </summary>
    public sealed class RewardDistributionManager : IRewardDistributionManager
    {
        private const int DefaultEpoch = 240;

        private readonly Network network;
        private readonly ChainIndexer chainIndexer;
        private readonly IConsensusManager consensusManager;
        private readonly ILogger logger;

        private readonly int epoch;
        private readonly int epochWindow;

        private readonly Dictionary<uint256, Block> blocksByHashDictionary = new Dictionary<uint256, Block>();
        private readonly Dictionary<uint256, int?> commitmentHeightsByHash = new Dictionary<uint256, int?>();

        // The reward each miner receives upon distribution is computed as a proportion of the overall accumulated reward since the last distribution.
        // The proportion is based on how many blocks that miner produced in the period (each miner is identified by their block's coinbase's scriptPubKey).
        // It is therefore not in any miner's advantage to delay or skip producing their blocks as it will affect their proportion of the produced blocks.
        // We pay no attention to whether a miner has been kicked since the last distribution or not.
        // If they produced an accepted block, they get their reward.

        public RewardDistributionManager(Network network, ChainIndexer chainIndexer, IConsensusManager consensusManager, ILoggerFactory loggerFactory)
        {
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.consensusManager = consensusManager;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.epoch = this.network.Consensus.MaxReorgLength == 0 ? DefaultEpoch : (int)this.network.Consensus.MaxReorgLength;
            this.epochWindow = this.epoch * 2;

            if (this.network.RewardClaimerBlockInterval > 0)
            {
                // If the amount of blocks that the sidechain will advance in the time that the reward intervals are, is more
                // than the default epoch then use that amount so that there aren't any gaps.
                var mainchainTargetSpacingSeconds = 45;
                var sidechainAdvancement = (int)Math.Round(this.network.RewardClaimerBlockInterval * mainchainTargetSpacingSeconds / this.network.Consensus.TargetSpacing.TotalSeconds, MidpointRounding.AwayFromZero);
                if (sidechainAdvancement > this.epoch)
                    this.epoch = sidechainAdvancement;
            }
        }

        /// <inheritdoc />
        public List<Recipient> Distribute(int heightOfRecordedDistributionDeposit, Money totalReward)
        {
            var encoder = new CollateralHeightCommitmentEncoder(this.logger);

            // First determine the main chain blockheight of the recorded deposit less max reorg * 2 (epoch window)
            var applicableMainChainDepositHeight = heightOfRecordedDistributionDeposit - this.epochWindow;
            this.logger.LogDebug($"{nameof(applicableMainChainDepositHeight)} : {applicableMainChainDepositHeight}");

            // Then find the header on the sidechain that contains the applicable commitment height.
            int sidechainTipHeight = this.chainIndexer.Tip.Height;

            ChainedHeader currentHeader = this.chainIndexer.GetHeader(sidechainTipHeight);

            do
            {
                this.blocksByHashDictionary.TryGetValue(currentHeader.HashBlock, out Block blockToCheck);

                if (blockToCheck == null)
                {
                    blockToCheck = this.consensusManager.GetBlockData(currentHeader.HashBlock).Block;
                    this.blocksByHashDictionary.TryAdd(currentHeader.HashBlock, blockToCheck);
                }

                // Do we have this commitment height cached already?
                this.commitmentHeightsByHash.TryGetValue(currentHeader.HashBlock, out int? commitmentHeightToCheck);
                if (commitmentHeightToCheck == null)
                {
                    // If not extract from the block.
                    (int? heightOfMainChainCommitment, _) = encoder.DecodeCommitmentHeight(blockToCheck.Transactions[0]);
                    if (heightOfMainChainCommitment != null)
                    {
                        commitmentHeightToCheck = heightOfMainChainCommitment.Value;
                        this.commitmentHeightsByHash.Add(currentHeader.HashBlock, commitmentHeightToCheck);
                    }
                }

                if (commitmentHeightToCheck != null)
                {
                    this.logger.LogDebug($"{currentHeader} : {nameof(commitmentHeightToCheck)}={commitmentHeightToCheck}");

                    if (commitmentHeightToCheck <= applicableMainChainDepositHeight)
                        break;
                }

                currentHeader = currentHeader.Previous;

            } while (currentHeader.Height != 0);

            // Get the set of miners (more specifically, the scriptPubKeys they generated blocks with) to distribute rewards to.
            // Based on the computed 'common block height' we define the distribution epoch:
            int sidechainStartHeight = currentHeader.Height;
            this.logger.LogDebug($"Initial {nameof(sidechainStartHeight)} : {sidechainStartHeight}");

            // This is a special case which will not be the case on the live network.
            if (sidechainStartHeight < this.epoch)
                sidechainStartHeight = 0;

            // If the sidechain start is more than the epoch, then deduct the epoch window.
            if (sidechainStartHeight > this.epoch)
                sidechainStartHeight -= this.epoch;

            this.logger.LogDebug($"Adjusted {nameof(sidechainStartHeight)} : {sidechainStartHeight}");

            var blocksMinedEach = new Dictionary<Script, long>();

            var totalBlocks = CalculateBlocksMinedPerMiner(blocksMinedEach, sidechainStartHeight, currentHeader.Height);
            List<Recipient> recipients = ConstructRecipients(heightOfRecordedDistributionDeposit, blocksMinedEach, totalBlocks, totalReward);
            return recipients;
        }

        private long CalculateBlocksMinedPerMiner(Dictionary<Script, long> blocksMinedEach, int sidechainStartHeight, int sidechainEndHeight)
        {
            long totalBlocks = 0;

            for (int currentHeight = sidechainStartHeight; currentHeight <= sidechainEndHeight; currentHeight++)
            {
                ChainedHeader chainedHeader = this.chainIndexer.GetHeader(currentHeight);
                if (chainedHeader.Block == null)
                    chainedHeader.Block = this.consensusManager.GetBlockData(chainedHeader.HashBlock).Block;

                Transaction coinBase = chainedHeader.Block.Transactions.First();

                // Regard the first 'spendable' scriptPubKey in the coinbase as belonging to the miner's wallet.
                // This avoids trying to recover the pubkey from the block signature.
                Script minerScript = coinBase.Outputs.First(o => !o.ScriptPubKey.IsUnspendable).ScriptPubKey;

                // If the POA miner at the time did not have a wallet address, the script length can be 0.
                // In this case the block shouldn't count as it was "not mined by anyone".
                if (!Script.IsNullOrEmpty(minerScript))
                {
                    if (!blocksMinedEach.TryGetValue(minerScript, out long minerBlockCount))
                        minerBlockCount = 0;

                    blocksMinedEach[minerScript] = ++minerBlockCount;

                    totalBlocks++;
                }
                else
                    this.logger.LogDebug($"A block was mined with an empty script at height '{currentHeight}' (the miner probably did not have a wallet at the time.");
            }

            var minerLog = new StringBuilder();
            minerLog.AppendLine($"Total Blocks      = {totalBlocks}");
            minerLog.AppendLine($"Side Chain Start  = {sidechainStartHeight}");
            minerLog.AppendLine($"Side Chain End    = {sidechainEndHeight}");

            foreach (KeyValuePair<Script, long> item in blocksMinedEach)
            {
                minerLog.AppendLine($"{item.Key} - {item.Value}");
            }

            this.logger.LogDebug(minerLog.ToString());

            return totalBlocks;
        }

        private List<Recipient> ConstructRecipients(int heightOfRecordedDistributionDeposit, Dictionary<Script, long> blocksMinedEach, long totalBlocks, Money totalReward)
        {
            var recipients = new List<Recipient>();

            foreach (Script scriptPubKey in blocksMinedEach.Keys)
            {
                Money amount = totalReward * blocksMinedEach[scriptPubKey] / totalBlocks;

                // Only convert to P2PKH if it is a pay-to-pubkey script. Leave the other types alone; the mempool should filter out anything that isn't allowed.
                // Note that the node wallets can detect transactions with a destination of either the P2PK or P2PKH scriptPubKey corresponding to one of their pubkeys.
                if (scriptPubKey.IsScriptType(ScriptType.P2PK))
                {
                    PubKey pubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey);
                    Script p2pkh = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(pubKey);

                    recipients.Add(new Recipient() { Amount = amount, ScriptPubKey = p2pkh });
                }
                else
                    recipients.Add(new Recipient() { Amount = amount, ScriptPubKey = scriptPubKey });
            }

            var recipientLog = new StringBuilder();
            foreach (Recipient recipient in recipients)
            {
                recipientLog.AppendLine($"{recipient.ScriptPubKey} - {recipient.Amount}");
            }
            this.logger.LogDebug(recipientLog.ToString());

            this.logger.LogInformation($"Reward distribution at main chain height {heightOfRecordedDistributionDeposit} will distribute {totalReward} STRAX between {recipients.Count} mining keys.");

            return recipients;
        }
    }
}
