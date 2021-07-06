﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using NBitcoin;
using NLog;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.PoA.Collateral.CounterChain;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public sealed class DepositExtractor : IDepositExtractor
    {
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly Network network;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly ICounterChainSettings counterChainSettings;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly ExternalApiClient externalApiClient;
        private readonly ILogger logger;

        public DepositExtractor(IFederatedPegSettings federatedPegSettings, Network network, IOpReturnDataReader opReturnDataReader, ICounterChainSettings counterChainSettings, IHttpClientFactory httpClientFactory)
        {
            this.federatedPegSettings = federatedPegSettings;
            this.network = network;
            this.opReturnDataReader = opReturnDataReader;
            this.counterChainSettings = counterChainSettings;
            this.httpClientFactory = httpClientFactory;
            this.externalApiClient = new ExternalApiClient(this.counterChainSettings.CounterChainApiHost, this.counterChainSettings.CounterChainApiPort, this.httpClientFactory);
            this.logger = LogManager.GetCurrentClassLogger();
        }

        private static Dictionary<string, List<IDeposit>> DepositsToInject = new Dictionary<string, List<IDeposit>>()
        {
            { "CirrusRegTest", new List<IDeposit> {
                new Deposit(
                    0x1 /* Tx of deposit being redone */, DepositRetrievalType.Small, new Money(10, MoneyUnit.BTC),
                    "qZc3WCqj8dipxUau1q18rT6EMBN6LRZ44A", 85, 0)
                }
            },

            { "CirrusTest", new List<IDeposit> {
                new Deposit(
                    uint256.Parse("7691bf9838ebdede6db0cb466d93c1941d13894536dc3a5db8289ad04b28d12c"), DepositRetrievalType.Small, new Money(20, MoneyUnit.BTC),
                    "qeyK7poxBE1wy8H24a7AcpLySCsfiqAo6A", 2_178_200, 0)
                }
            },

            { "CirrusMain", new List<IDeposit> {
                new Deposit(
                    uint256.Parse("6179ee3332348948641210e2c9358a41aa8aaf2924d783e52bc4cec47deef495") /* Tx of deposit being redone */, DepositRetrievalType.Normal,
                    new Money(239, MoneyUnit.BTC), "XNrgftud4ExFL7dXEHjPPx3JX22qvFy39v", 2_450_000, 0),
                new Deposit(
                    uint256.Parse("b8417bdbe7690609b2fff47d6d8b39bed69993df6656d7e10197c141bdacdd90") /* Tx of deposit being redone */, DepositRetrievalType.Normal,
                    new Money(640, MoneyUnit.BTC), "XCDeD5URnSbExLsuVYu7yufpbmb6KvaX8D", 2_450_000, 0),
                new Deposit(
                    uint256.Parse("375ed4f028d3ef32397641a0e307e1705fc3f8ba76fa776ce33fff53f52b0e1c") /* Tx of deposit being redone */, DepositRetrievalType.Large,
                    new Money(1649, MoneyUnit.BTC), "XCrHzx5AUnNChCj8MP9j82X3Y7gLAtULtj", 2_450_000, 0),
                }
            }
        };

        /// <inheritdoc />
        public IReadOnlyList<IDeposit> ExtractDepositsFromBlock(Block block, int blockHeight, DepositRetrievalType[] depositRetrievalTypes)
        {
            List<IDeposit> deposits;

            if (DepositsToInject.TryGetValue(this.network.Name, out List<IDeposit> depositsList))
                deposits = depositsList.Where(d => d.BlockNumber == blockHeight).ToList();
            else
                deposits = new List<IDeposit>();

            foreach (IDeposit deposit in deposits)
            {
                ((Deposit)deposit).BlockHash = block.GetHash();
            }

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
            // If there are no deposits to the multsig (i.e. cross chain transfers) do nothing.
            if (!DepositValidationHelper.TryGetDepositsToMultisig(this.network, transaction, FederatedPegSettings.CrossChainTransferMinimum, out List<TxOut> depositsToMultisig))
                return null;

            // If there are deposits to the multsig (i.e. cross chain transfers), try and extract and validate the address by the specfied destination chain.
            if (!DepositValidationHelper.TryGetTarget(transaction, this.opReturnDataReader, out bool conversionTransaction, out string targetAddress, out int targetChain))
                return null;

            Money amount = depositsToMultisig.Sum(o => o.Value);

            DepositRetrievalType depositRetrievalType;

            if (conversionTransaction)
            {
                // Instead of a fixed minimum, check that the deposit size at least covers the fee.
                // It will be checked again when the interop poller processes the resulting conversion request.
                string feeString;
                try
                {
                    feeString = this.externalApiClient.EstimateConversionTransactionFeeAsync().GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    this.logger.Error("Error accessing fee API: " + e);

                    return null;
                }

                if (!decimal.TryParse(feeString, out decimal minimumDeposit))
                {
                    this.logger.Warn("Failed to retrieve estimated fee from API. Ignoring deposit.");

                    return null;
                }

                if (minimumDeposit == decimal.MinusOne)
                {
                    this.logger.Warn("Estimated fee information currently unavailable. Ignoring deposit.");

                    return null;
                }

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