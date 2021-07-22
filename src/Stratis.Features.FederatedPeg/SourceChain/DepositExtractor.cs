using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NLog;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public sealed class DepositExtractor : IDepositExtractor
    {
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly Network network;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly ILogger logger;

        public DepositExtractor(IConversionRequestRepository conversionRequestRepository, IFederatedPegSettings federatedPegSettings, Network network, IOpReturnDataReader opReturnDataReader)
        {
            this.conversionRequestRepository = conversionRequestRepository;
            this.federatedPegSettings = federatedPegSettings;
            this.network = network;
            this.opReturnDataReader = opReturnDataReader;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        private static Dictionary<string, List<IDeposit>> DepositsToInject = new Dictionary<string, List<IDeposit>>()
        {
            { "CirrusRegTest", new List<IDeposit> {
                new Deposit(
                    0x1 /* Tx of deposit being redone */, DepositRetrievalType.Small, new Money(10, MoneyUnit.BTC),
                    "qZc3WCqj8dipxUau1q18rT6EMBN6LRZ44A", DestinationChain.STRAX, 85, 0)
                }
            },

            { "CirrusTest", new List<IDeposit> {
                new Deposit(
                    uint256.Parse("7691bf9838ebdede6db0cb466d93c1941d13894536dc3a5db8289ad04b28d12c"), DepositRetrievalType.Small, new Money(20, MoneyUnit.BTC),
                    "qeyK7poxBE1wy8H24a7AcpLySCsfiqAo6A",DestinationChain.STRAX, 2_178_200, 0)
                }
            },

            { "CirrusMain", new List<IDeposit> {
                new Deposit(
                    uint256.Parse("6179ee3332348948641210e2c9358a41aa8aaf2924d783e52bc4cec47deef495") /* Tx of deposit being redone */, DepositRetrievalType.Normal,
                    new Money(239, MoneyUnit.BTC), "XNrgftud4ExFL7dXEHjPPx3JX22qvFy39v",DestinationChain.STRAX, 2_450_000, 0),
                new Deposit(
                    uint256.Parse("b8417bdbe7690609b2fff47d6d8b39bed69993df6656d7e10197c141bdacdd90") /* Tx of deposit being redone */, DepositRetrievalType.Normal,
                    new Money(640, MoneyUnit.BTC), "XCDeD5URnSbExLsuVYu7yufpbmb6KvaX8D", DestinationChain.STRAX, 2_450_000, 0),
                new Deposit(
                    uint256.Parse("375ed4f028d3ef32397641a0e307e1705fc3f8ba76fa776ce33fff53f52b0e1c") /* Tx of deposit being redone */, DepositRetrievalType.Large,
                    new Money(1649, MoneyUnit.BTC), "XCrHzx5AUnNChCj8MP9j82X3Y7gLAtULtj",DestinationChain.STRAX, 2_450_000, 0),
                }
            }
        };

        /// <inheritdoc />
        public async Task<IReadOnlyList<IDeposit>> ExtractDepositsFromBlock(Block block, int blockHeight, DepositRetrievalType[] depositRetrievalTypes)
        {
            List<IDeposit> deposits;

            if (DepositsToInject.TryGetValue(this.network.Name, out List<IDeposit> depositsList))
                deposits = depositsList.Where(d => d.BlockNumber == blockHeight).ToList();
            else
                deposits = new List<IDeposit>();

            // Only the sidechain interacts with burn requests from the multisig contract.
            if (!this.federatedPegSettings.IsMainChain)
                ProcessInterFluxBurnRequests(deposits, blockHeight);

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
                    IDeposit deposit = await this.ExtractDepositFromTransaction(transaction, blockHeight, blockHash).ConfigureAwait(false);

                    if (deposit == null)
                        continue;

                    if (depositRetrievalTypes.Any(t => t == deposit.RetrievalType))
                        deposits.Add(deposit);
                }
            }

            return deposits;
        }

        /// <summary>
        /// If there are any burn requests scheduled at the given block height, add them to the list of deposits.
        /// </summary>
        /// <param name="deposits">Add burn requests to this list of deposits.</param>
        /// <param name="blockHeight">The block height to inspect.</param>
        private void ProcessInterFluxBurnRequests(List<IDeposit> deposits, int blockHeight)
        {
            // Check if this is the target height for a conversion transaction from wSTRAX back to STRAX.
            // These get returned before any other withdrawal transactions in the block to ensure consistent ordering.
            List<ConversionRequest> burnRequests = this.conversionRequestRepository.GetAllBurn(true);

            if (burnRequests == null)
                return;

            foreach (ConversionRequest burnRequest in burnRequests)
            {
                if (burnRequest.BlockHeight == blockHeight)
                    this.logger.Info($"Processing burn request '{burnRequest.RequestId}' to '{burnRequest.DestinationAddress}' for {new Money(burnRequest.Amount)} STRAX.");
                else
                {
                    this.logger.Info($"Burn request '{burnRequest.RequestId}' to '{burnRequest.DestinationAddress}' for {new Money(burnRequest.Amount)} STRAX, will be processed at height {burnRequest.BlockHeight}.");
                    continue;
                }

                // We use the transaction ID from the Ethereum chain as the request ID for the withdrawal.
                // To parse it into a uint256 we need to trim the leading hex marker from the string.
                uint256 depositId;
                try
                {
                    depositId = new uint256(burnRequest.RequestId.Replace("0x", ""));
                }
                catch (Exception)
                {
                    continue;
                }

                DepositRetrievalType depositRetrievalType = DetermineDepositRetrievalType(Money.Satoshis(burnRequest.Amount));
                var deposit = new Deposit(
                    depositId,
                    depositRetrievalType,
                    Money.Satoshis(burnRequest.Amount),
                    burnRequest.DestinationAddress,
                    DestinationChain.STRAX,
                    blockHeight,
                    0);

                deposits.Add(deposit);

                // Immediately flag it as processed & persist so that it can't be added again.
                burnRequest.Processed = true;
                burnRequest.RequestStatus = ConversionRequestStatus.Processed;

                this.conversionRequestRepository.Save(burnRequest);
            }
        }

        /// <inheritdoc />
        public async Task<IDeposit> ExtractDepositFromTransaction(Transaction transaction, int blockHeight, uint256 blockHash)
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
                if (this.federatedPegSettings.IsMainChain && amount < DepositValidationHelper.ConversionTransactionMinimum)
                {
                    this.logger.Warn($"Ignoring conversion transaction '{transaction.GetHash()}' with amount {amount} which is below the threshold of {DepositValidationHelper.ConversionTransactionMinimum}.");
                    return null;
                }

                this.logger.Info("Received conversion deposit transaction '{0}' for an amount of {1}.", transaction.GetHash(), amount);

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
                else
                {
                    depositRetrievalType = DetermineDepositRetrievalType(amount);
                }
            }

            return new Deposit(transaction.GetHash(), depositRetrievalType, amount, targetAddress, (DestinationChain)targetChain, blockHeight, blockHash);
        }

        private DepositRetrievalType DetermineDepositRetrievalType(Money amount)
        {
            if (amount > this.federatedPegSettings.NormalDepositThresholdAmount)
                return DepositRetrievalType.Large;

            if (amount > this.federatedPegSettings.SmallDepositThresholdAmount)
                return DepositRetrievalType.Normal;

            return DepositRetrievalType.Small;
        }
    }
}