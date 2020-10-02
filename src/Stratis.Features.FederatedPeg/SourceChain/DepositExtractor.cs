using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Distribution;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using TracerAttributes;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    [NoTrace]
    public class DepositExtractor : IDepositExtractor
    {
        /// <summary>
        /// This deposit extractor implementation only looks for a very specific deposit format.
        /// Deposits will have 2 outputs when there is no change.
        /// </summary>
        private const int ExpectedNumberOfOutputsNoChange = 2;

        /// <summary> Deposits will have 3 outputs when there is change.</summary>
        private const int ExpectedNumberOfOutputsChange = 3;

        private readonly Script depositScript;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILogger logger;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly IDistributionStore distributionStore;

        public DepositExtractor(
            ILoggerFactory loggerFactory,
            IFederatedPegSettings federatedPegSettings,
            IOpReturnDataReader opReturnDataReader,
            IDistributionStore distributionStore)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            // Note: MultiSigRedeemScript.PaymentScript equals MultiSigAddress.ScriptPubKey
            this.depositScript =
                federatedPegSettings.MultiSigRedeemScript?.PaymentScript ??
                federatedPegSettings.MultiSigAddress?.ScriptPubKey;

            this.federatedPegSettings = federatedPegSettings;
            this.opReturnDataReader = opReturnDataReader;
            this.distributionStore = distributionStore;
        }

        /// <inheritdoc />
        public IReadOnlyList<IDeposit> ExtractDepositsFromBlock(Block block, int blockHeight, DepositRetrievalType depositRetrievalType)
        {
            var deposits = new List<IDeposit>();

            // If it's an empty block, there's no deposits inside.
            if (block.Transactions.Count <= 1)
                return deposits;

            uint256 blockHash = block.GetHash();

            foreach (Transaction transaction in block.Transactions)
            {
                IDeposit deposit = this.ExtractDepositFromTransaction(transaction, blockHeight, blockHash, depositRetrievalType);
                if (deposit == null)
                    continue;

                if (depositRetrievalType == DepositRetrievalType.Small && deposit.Amount <= this.federatedPegSettings.SmallDepositThresholdAmount)
                {
                    deposits.Add(deposit);
                    continue;
                }

                if (depositRetrievalType == DepositRetrievalType.Normal && deposit.Amount > this.federatedPegSettings.SmallDepositThresholdAmount && deposit.Amount <= this.federatedPegSettings.NormalDepositThresholdAmount)
                {
                    deposits.Add(deposit);
                    continue;
                }

                if (depositRetrievalType == DepositRetrievalType.Large && deposit.Amount > this.federatedPegSettings.NormalDepositThresholdAmount)
                {
                    deposits.Add(deposit);
                    continue;
                }
            }

            return deposits;
        }


        /// <inheritdoc />
        public IDeposit ExtractDepositFromTransaction(Transaction transaction, int blockHeight, uint256 blockHash, DepositRetrievalType depositRetrievalType)
        {
            // Coinbase transactions can't have deposits.
            if (transaction.IsCoinBase)
                return null;

            // Deposits have a certain structure.
            if (transaction.Outputs.Count != ExpectedNumberOfOutputsNoChange && transaction.Outputs.Count != ExpectedNumberOfOutputsChange)
                return null;

            var depositsToMultisig = transaction.Outputs.Where(output =>
                output.ScriptPubKey == this.depositScript &&
                output.Value >= FederatedPegSettings.CrossChainTransferMinimum).ToList();

            if (!depositsToMultisig.Any())
                return null;

            if (!this.opReturnDataReader.TryGetTargetAddress(transaction, out string targetAddress))
                return null;

            // Check if this deposit is intended for distribution to the miners. This is identified by a specific destination address in the deposit OP_RETURN.
            // A distribution deposit is otherwise exactly the same as a regular deposit transaction. It just gets stored separately for special handling.
            // This could be moved to its own method but it seems unnecessary to iterate over each transaction twice.
            if (targetAddress == DistributionAddress)
            {
                // The mainchain height gets committed to in each block produced by a sidechain miner.
                // We mandate that reward distributions to sidechain miners are performed every 'n' mainchain blocks.
                // This is so that the federation has some means of calibrating when to start coordinating distribution attempts, rather than trying to send partial transactions ad-hoc.
                // 'n' should be sufficiently large so that there is ample time for the federation to coordinate creating a distribution transaction amongst themselves.
                // If the nth mainchain block does not specifically appear in a commitment, the first block with a commitment higher than the expected height is a distribution block.
                // If consecutive distribution periods are missed in the commitments (this is exceedingly unlikely given the lower block time on the sidechain) it doesn't really matter,
                // as the distribution amounts can accumulate until a block is committed to.
                // The reward each miner receives upon distribution is computed as a proportion of the overall accumulated reward since the last distribution.
                // The proportion is based on how many blocks that miner produced in the period (each miner is identified by their block's coinbase's scriptPubKey).
                // It is therefore not in any miner's advantage to delay or skip producing their blocks as it will affect their proportion of the produced blocks.
                // We pay no attention to whether a miner has been kicked since the last distribution or not.
                // If they produced an accepted block, they get their reward.

                // TODO: Need to ensure that the order of handling distribution transactions vs regular deposit transactions is consistent, to avoid scrambled UTXO selections

                this.distributionStore.AddToStore(new DistributionRecord()
                {
                    BlockHash = blockHash,
                    BlockHeight = blockHeight,
                    CommitmentHeight = 0,
                    Processed = false,
                    TransactionId = transaction.GetHash()
                });

                // We need to be able to access the store from the sidechain, whereas the deposit containing the distribution
                this.distributionStore.Save();

                // We don't want to regard this as a deposit, so bypass the rest of the processing.
                return null;
            }

            this.logger.LogDebug("Processing a received deposit transaction with address: {0}. Transaction hash: {1}.", targetAddress, transaction.GetHash());

            return new Deposit(transaction.GetHash(), depositRetrievalType, depositsToMultisig.Sum(o => o.Value), targetAddress, blockHeight, blockHash);
        }

        /// <inheritdoc />
        public MaturedBlockDepositsModel ExtractBlockDeposits(ChainedHeaderBlock blockToExtractDepositsFrom, DepositRetrievalType depositRetrievalType)
        {
            Guard.NotNull(blockToExtractDepositsFrom, nameof(blockToExtractDepositsFrom));

            var maturedBlockModel = new MaturedBlockInfoModel()
            {
                BlockHash = blockToExtractDepositsFrom.ChainedHeader.HashBlock,
                BlockHeight = blockToExtractDepositsFrom.ChainedHeader.Height,
                BlockTime = blockToExtractDepositsFrom.ChainedHeader.Header.Time
            };

            IReadOnlyList<IDeposit> deposits = ExtractDepositsFromBlock(blockToExtractDepositsFrom.Block, blockToExtractDepositsFrom.ChainedHeader.Height, depositRetrievalType);

            return new MaturedBlockDepositsModel(maturedBlockModel, deposits);
        }
    }
}