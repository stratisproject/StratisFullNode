using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Primitives;
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

        private readonly ChainIndexer chainIndexer;
        private readonly IConsensusManager consensusManager;
        private readonly CollateralHeightCommitmentEncoder encoder;
        private readonly int epoch;
        private readonly int epochWindow;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IFederationHistory federationHistory;

        private readonly Dictionary<Script, long> blocksMinedEach = new Dictionary<Script, long>();
        private readonly Dictionary<uint256, Transaction> commitmentTransactionByHashDictionary = new Dictionary<uint256, Transaction>();
        private readonly Dictionary<uint256, int?> commitmentHeightsByHash = new Dictionary<uint256, int?>();

        // The reward each miner receives upon distribution is computed as a proportion of the overall accumulated reward since the last distribution.
        // The proportion is based on how many blocks that miner produced in the period (each miner is identified by their block's coinbase's scriptPubKey).
        // It is therefore not in any miner's advantage to delay or skip producing their blocks as it will affect their proportion of the produced blocks.
        // We pay no attention to whether a miner has been kicked since the last distribution or not.
        // If they produced an accepted block, they get their reward.

        public RewardDistributionManager(Network network, ChainIndexer chainIndexer, IConsensusManager consensusManager, IFederationHistory federationHistory)
        {
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.consensusManager = consensusManager;
            this.logger = LogManager.GetCurrentClassLogger();
            this.federationHistory = federationHistory;

            this.encoder = new CollateralHeightCommitmentEncoder();
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

        public List<Recipient> DistributeToMultisigNodes(int blockHeight, Money fee)
        {
            // Retrieve all the multisig members at the current block height
            List<IFederationMember> federation = this.federationHistory.GetFederationForBlock(this.chainIndexer.GetHeader(blockHeight));

            var multisigs = new List<CollateralFederationMember>();

            foreach (IFederationMember member in federation)
            {
                if (!(member is CollateralFederationMember collateralMember))
                    continue;

                if (!collateralMember.IsMultisigMember)
                    continue;

                multisigs.Add(collateralMember);
            }

            // Inspect the last (size of current federation) less maxerorg blocks to determine
            // which of them were mined by a multisig member.
            var multiSigMinerScripts = new List<Script>();
            var startHeight = this.chainIndexer.Tip.Height - this.epoch;
            for (int i = federation.Count; i >= 0; i--)
            {
                ChainedHeader chainedHeader = this.chainIndexer.GetHeader(startHeight - i);

                var collateralFederationMember = this.federationHistory.GetFederationMemberForBlock(chainedHeader) as CollateralFederationMember;
                if (collateralFederationMember != null && collateralFederationMember.IsMultisigMember)
                {
                    if (chainedHeader.Block == null)
                        chainedHeader.Block = this.consensusManager.GetBlockData(chainedHeader.HashBlock).Block;

                    Transaction coinBase = chainedHeader.Block.Transactions[0];

                    Script minerScript = coinBase.Outputs.First(o => !o.ScriptPubKey.IsUnspendable).ScriptPubKey;

                    // If the POA miner at the time did not have a wallet address, the script length can be 0.
                    // In this case the block shouldn't count as it was "not mined by anyone".
                    if (Script.IsNullOrEmpty(minerScript))
                        continue;

                    if (!multiSigMinerScripts.Contains(minerScript))
                        multiSigMinerScripts.Add(minerScript);
                }
            }

            Money feeReward = fee / multiSigMinerScripts.Count;

            var multiSigRecipients = new List<Recipient>();

            foreach (Script multiSigMinerScript in multiSigMinerScripts)
            {
                if (multiSigMinerScript.IsScriptType(ScriptType.P2PK))
                {
                    PubKey pubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(multiSigMinerScript);
                    Script p2pkh = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(pubKey);

                    multiSigRecipients.Add(new Recipient() { Amount = feeReward, ScriptPubKey = p2pkh });
                }
                else
                    multiSigRecipients.Add(new Recipient() { Amount = feeReward, ScriptPubKey = multiSigMinerScript });

            }

            return multiSigRecipients;
        }

        /// <inheritdoc />
        public List<Recipient> Distribute(int heightOfRecordedDistributionDeposit, Money totalReward)
        {
            // First determine the main chain blockheight of the recorded deposit less max reorg * 2 (epoch window)
            var applicableMainChainDepositHeight = heightOfRecordedDistributionDeposit - this.epochWindow;
            this.logger.Trace("{0} : {1}", nameof(applicableMainChainDepositHeight), applicableMainChainDepositHeight);

            // Then find the header on the sidechain that contains the applicable commitment height.
            int sidechainTipHeight = this.chainIndexer.Tip.Height;

            ChainedHeader currentHeader = this.chainIndexer.GetHeader(sidechainTipHeight);

            do
            {
                this.commitmentTransactionByHashDictionary.TryGetValue(currentHeader.HashBlock, out Transaction transactionToCheck);

                if (transactionToCheck == null)
                {
                    transactionToCheck = this.consensusManager.GetBlockData(currentHeader.HashBlock).Block.Transactions[0];
                    this.commitmentTransactionByHashDictionary.TryAdd(currentHeader.HashBlock, transactionToCheck);
                }

                // Do we have this commitment height cached already?
                this.commitmentHeightsByHash.TryGetValue(currentHeader.HashBlock, out int? commitmentHeightToCheck);
                if (commitmentHeightToCheck == null)
                {
                    // If not extract from the block.
                    (int? heightOfMainChainCommitment, _) = this.encoder.DecodeCommitmentHeight(transactionToCheck);
                    if (heightOfMainChainCommitment != null)
                    {
                        commitmentHeightToCheck = heightOfMainChainCommitment.Value;
                        this.commitmentHeightsByHash.Add(currentHeader.HashBlock, commitmentHeightToCheck);
                    }
                }

                if (commitmentHeightToCheck != null)
                {
                    this.logger.Trace("{0} : {1}={2}", currentHeader, nameof(commitmentHeightToCheck), commitmentHeightToCheck);

                    if (commitmentHeightToCheck <= applicableMainChainDepositHeight)
                        break;
                }

                currentHeader = currentHeader.Previous;

            } while (currentHeader.Height != 0);

            // Get the set of miners (more specifically, the scriptPubKeys they generated blocks with) to distribute rewards to.
            // Based on the computed 'common block height' we define the distribution epoch:
            int sidechainStartHeight = currentHeader.Height;
            this.logger.Trace("Initial {0} : {1}", nameof(sidechainStartHeight), sidechainStartHeight);

            // This is a special case which will not be the case on the live network.
            if (sidechainStartHeight < this.epoch)
                sidechainStartHeight = 0;

            // If the sidechain start is more than the epoch, then deduct the epoch window.
            if (sidechainStartHeight > this.epoch)
                sidechainStartHeight -= this.epoch;

            this.logger.Trace("Adjusted {0} : {1}", nameof(sidechainStartHeight), sidechainStartHeight);

            // Ensure that the dictionary is cleared on every run.
            // As this is a static class, new instances of this dictionary will
            // only be cleaned up once the node shuts down. It is therefore better
            // to use a single instance to work with.
            this.blocksMinedEach.Clear();

            var totalBlocks = CalculateBlocksMinedPerMiner(sidechainStartHeight, currentHeader.Height);
            return ConstructRecipients(heightOfRecordedDistributionDeposit, totalBlocks, totalReward);
        }

        private long CalculateBlocksMinedPerMiner(int sidechainStartHeight, int sidechainEndHeight)
        {
            long totalBlocks = 0;

            for (int currentHeight = sidechainStartHeight; currentHeight <= sidechainEndHeight; currentHeight++)
            {
                ChainedHeader chainedHeader = this.chainIndexer.GetHeader(currentHeight);
                if (chainedHeader.Block == null)
                    chainedHeader.Block = this.consensusManager.GetBlockData(chainedHeader.HashBlock).Block;

                Transaction coinBase = chainedHeader.Block.Transactions[0];

                // Regard the first 'spendable' scriptPubKey in the coinbase as belonging to the miner's wallet.
                // This avoids trying to recover the pubkey from the block signature.
                Script minerScript = coinBase.Outputs.First(o => !o.ScriptPubKey.IsUnspendable).ScriptPubKey;

                // If the POA miner at the time did not have a wallet address, the script length can be 0.
                // In this case the block shouldn't count as it was "not mined by anyone".
                if (!Script.IsNullOrEmpty(minerScript))
                {
                    if (!this.blocksMinedEach.TryGetValue(minerScript, out long minerBlockCount))
                        minerBlockCount = 0;

                    this.blocksMinedEach[minerScript] = ++minerBlockCount;

                    totalBlocks++;
                }
                else
                    this.logger.Trace("A block was mined with an empty script at height '{0}' (the miner probably did not have a wallet at the time.", currentHeight);
            }

            /*
             * TODO: Uncomment this if debugging is ever required, otherwise implement a IsDebug setting on NodeSettings.
             * 
            var minerLog = new StringBuilder();
            minerLog.AppendLine($"Total Blocks      = {totalBlocks}");
            minerLog.AppendLine($"Side Chain Start  = {sidechainStartHeight}");
            minerLog.AppendLine($"Side Chain End    = {sidechainEndHeight}");

            foreach (KeyValuePair<Script, long> item in blocksMinedEach)
            {
                minerLog.AppendLine($"{item.Key} - {item.Value}");
            }

            this.logger.Debug(minerLog.ToString());
            */

            return totalBlocks;
        }

        private List<Recipient> ConstructRecipients(int heightOfRecordedDistributionDeposit, long totalBlocks, Money totalReward)
        {
            var recipients = new List<Recipient>();

            foreach (Script scriptPubKey in this.blocksMinedEach.Keys)
            {
                Money amount = totalReward * this.blocksMinedEach[scriptPubKey] / totalBlocks;

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

            /*
             * TODO: Uncomment this if debugging is ever required, otherwise implement a IsDebug setting on NodeSettings.
             * 
            var recipientLog = new StringBuilder();
            foreach (Recipient recipient in recipients)
            {
                recipientLog.AppendLine($"{recipient.ScriptPubKey} - {recipient.Amount}");
            }
            this.logger.LogDebug(recipientLog.ToString());
            */

            this.logger.Info("Reward distribution at main chain height {0} will distribute {1} STRAX between {2} mining keys.", heightOfRecordedDistributionDeposit, totalReward, recipients.Count);

            return recipients;
        }
    }
}
