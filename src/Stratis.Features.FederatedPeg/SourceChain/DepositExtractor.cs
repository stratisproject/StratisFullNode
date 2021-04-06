﻿using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public sealed class DepositExtractor : IDepositExtractor
    {
        // Conversion transaction deposits smaller than this threshold will be ignored. Denominated in STRAX.
        public const decimal ConversionTransactionMinimum = 90_000;

        /// <summary>
        /// This deposit extractor implementation only looks for a very specific deposit format.
        /// Deposits will have 2 outputs when there is no change.
        /// </summary>
        private const int ExpectedNumberOfOutputsNoChange = 2;

        /// <summary> Deposits will have 3 outputs when there is change.</summary>
        private const int ExpectedNumberOfOutputsChange = 3;

        private readonly Script depositScript;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly Network network;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly IExternalApiPoller externalApiPoller;
        private readonly ILogger logger;

        public DepositExtractor(IFederatedPegSettings federatedPegSettings, Network network, IOpReturnDataReader opReturnDataReader, IExternalApiPoller externalApiPoller)
        {
            this.depositScript = federatedPegSettings.MultiSigRedeemScript.PaymentScript;
            this.federatedPegSettings = federatedPegSettings;
            this.network = network;
            this.opReturnDataReader = opReturnDataReader;
            this.externalApiPoller = externalApiPoller;
            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <inheritdoc />
        public IReadOnlyList<IDeposit> ExtractDepositsFromBlock(Block block, int blockHeight, DepositRetrievalType[] depositRetrievalTypes)
        {
            var deposits = new List<IDeposit>();

            // If it's an empty block (i.e. only the coinbase transaction is present), there's no deposits inside.
            if (block.Transactions.Count > 1)
            {
                uint256 blockHash = block.GetHash();

                foreach (Transaction transaction in block.Transactions)
                {
                    IDeposit deposit = this.ExtractDepositFromTransaction(transaction, blockHeight, blockHash);

                    if (deposit == null)
                        continue;

                    if (depositRetrievalTypes.Any(t => t == deposit.RetrievalType))
                        deposits.Add(deposit);
                }
            }

            return deposits;
        }

        /// <inheritdoc />
        public IDeposit ExtractDepositFromTransaction(Transaction transaction, int blockHeight, uint256 blockHash)
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

            // Check the common case first.
            bool conversionTransaction = false;
            if (!this.opReturnDataReader.TryGetTargetAddress(transaction, out string targetAddress))
            {
                if (!this.opReturnDataReader.TryGetTargetEthereumAddress(transaction, out targetAddress))
                {
                    return null;
                }
                
                conversionTransaction = true;
            }

            Money amount = depositsToMultisig.Sum(o => o.Value);

            DepositRetrievalType depositRetrievalType;

            if (conversionTransaction)
            {
                // Instead of a fixed minimum, check that the deposit size at least covers the fee.
                decimal minimumDeposit = this.externalApiPoller.EstimateConversionTransactionFee();

                if (amount < Money.Coins(minimumDeposit))
                {
                    this.logger.Warn("Received deposit of {0}, but computed minimum deposit fee is {1}. Ignoring deposit.", amount, minimumDeposit);

                    return null;
                }

                this.logger.Info("Received conversion transaction deposit of {0}, subtracting estimated fee of {1}.", amount, minimumDeposit);

                if (amount > this.federatedPegSettings.NormalDepositThresholdAmount)
                    depositRetrievalType = DepositRetrievalType.ConversionLarge;
                else if (amount > this.federatedPegSettings.SmallDepositThresholdAmount)
                    depositRetrievalType = DepositRetrievalType.ConversionNormal;
                else
                    depositRetrievalType = DepositRetrievalType.ConversionSmall;
            }
            else
            {
                if (targetAddress == this.network.CirrusRewardDummyAddress)
                    depositRetrievalType = DepositRetrievalType.Distribution;
                else if (amount > this.federatedPegSettings.NormalDepositThresholdAmount)
                    depositRetrievalType = DepositRetrievalType.Large;
                else if (amount > this.federatedPegSettings.SmallDepositThresholdAmount)
                    depositRetrievalType = DepositRetrievalType.Normal;
                else
                    depositRetrievalType = DepositRetrievalType.Small;
            }

            return new Deposit(transaction.GetHash(), depositRetrievalType, amount, targetAddress, blockHeight, blockHash);
        }
    }
}