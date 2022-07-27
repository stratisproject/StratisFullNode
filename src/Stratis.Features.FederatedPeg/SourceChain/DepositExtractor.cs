using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Interfaces;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.SmartContracts;
using Block = NBitcoin.Block;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public sealed class DepositExtractor : IDepositExtractor
    {
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly Network network;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly IBlockStore blockStore;
        private readonly ILogger logger;

        private readonly List<uint256> depositsBeingProcessedWithinMaturingWindow;

        public DepositExtractor(IConversionRequestRepository conversionRequestRepository, IFederatedPegSettings federatedPegSettings, Network network, IOpReturnDataReader opReturnDataReader, IBlockStore blockStore)
        {
            this.conversionRequestRepository = conversionRequestRepository;
            this.federatedPegSettings = federatedPegSettings;
            this.network = network;
            this.opReturnDataReader = opReturnDataReader;
            this.blockStore = blockStore;

            this.depositsBeingProcessedWithinMaturingWindow = new List<uint256>();
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
        public async Task<IReadOnlyList<IDeposit>> ExtractDepositsFromBlock(Block block, int blockHeight, IRetrievalTypeConfirmations confirmationsByRetrievalType)
        {
            List<IDeposit> deposits;

            if (DepositsToInject.TryGetValue(this.network.Name, out List<IDeposit> depositsList))
                deposits = depositsList.Where(d => d.BlockNumber == blockHeight).ToList();
            else
                deposits = new List<IDeposit>();

            // Only the sidechain interacts with burn requests from the multisig contract.
            if (!this.federatedPegSettings.IsMainChain)
                ProcessInterFluxBurnRequests(deposits, blockHeight, confirmationsByRetrievalType);

            foreach (IDeposit deposit in deposits)
            {
                ((Deposit)deposit).BlockHash = block.GetHash();
            }

            DepositRetrievalType[] retrievalTypes = confirmationsByRetrievalType.GetRetrievalTypes();

            // If it's an empty block (i.e. only the coinbase transaction is present), there's no deposits inside.
            if (block.Transactions.Count > 1)
            {
                uint256 blockHash = block.GetHash();

                foreach (Transaction transaction in block.Transactions)
                {
                    IDeposit deposit = await this.ExtractDepositFromTransaction(transaction, blockHeight, blockHash).ConfigureAwait(false);

                    if (deposit == null)
                        continue;

                    if (retrievalTypes.Any(t => t == deposit.RetrievalType))
                        deposits.Add(deposit);
                }
            }

            return deposits;
        }

        /// <summary>
        /// If there are any burn requests scheduled at the given block height, add them to the list of deposits.
        /// </summary>
        /// <param name="deposits">Add burn requests to this list of deposits.</param>
        /// <param name="inspectForDepositsAtHeight">The block height to inspect.</param>
        /// <param name="confirmationsByRetrievalType">The various retrieval types and their required confirmations.</param>
        private void ProcessInterFluxBurnRequests(List<IDeposit> deposits, int inspectForDepositsAtHeight, IRetrievalTypeConfirmations confirmationsByRetrievalType)
        {
            List<ConversionRequest> burnRequests = this.conversionRequestRepository.GetAllBurn(true);

            if (burnRequests == null)
                return;

            // We only process burns with destination chain 'STRAX' here, as SRC20->ERC20 burns are processed separately.
            foreach (ConversionRequest burnRequest in burnRequests.Where(b => inspectForDepositsAtHeight >= b.BlockHeight && b.DestinationChain == DestinationChain.STRAX))
            {
                if (inspectForDepositsAtHeight == burnRequest.BlockHeight)
                {
                    // Note: the wei-to-satoshi scaling has already been performed inside the InteropPoller.
                    this.logger.LogInformation($"Processing burn request '{burnRequest.RequestId}' to '{burnRequest.DestinationAddress}' for {burnRequest.Amount.FormatAsFractionalValue(8)} STRAX at height {inspectForDepositsAtHeight}.");

                    Deposit deposit = CreateDeposit(burnRequest, inspectForDepositsAtHeight);
                    if (deposit == null)
                        continue;

                    deposits.Add(deposit);

                    // It shouldn't be in here but just check anyway.
                    if (!this.depositsBeingProcessedWithinMaturingWindow.Contains(deposit.Id))
                        this.depositsBeingProcessedWithinMaturingWindow.Add(deposit.Id);

                    continue;
                }

                if (inspectForDepositsAtHeight > burnRequest.BlockHeight)
                {
                    DepositRetrievalType retrievalType = DetermineDepositRetrievalType(burnRequest.Amount.GetLow64());
                    var requiredConfirmations = confirmationsByRetrievalType.GetDepositConfirmations(burnRequest.BlockHeight, retrievalType);

                    // If the inspection height is now equal to the burn request's processing height plus
                    // the required confirmations, set it to processed.
                    if (inspectForDepositsAtHeight == burnRequest.BlockHeight + requiredConfirmations)
                    {
                        burnRequest.Processed = true;
                        burnRequest.RequestStatus = ConversionRequestStatus.Processed;

                        this.conversionRequestRepository.Save(burnRequest);

                        this.logger.LogInformation($"Marking burn request '{burnRequest.RequestId}' to '{burnRequest.DestinationAddress}' as processed at height {inspectForDepositsAtHeight}.");

                        continue;
                    }

                    // If the deposit is still not processed and the inspection height is within the
                    // request's processing height plus the required confirmations, add it to the set of deposits.
                    // This could have happened if the node restarted whilst the deposit was maturing.
                    if (inspectForDepositsAtHeight < burnRequest.BlockHeight + requiredConfirmations)
                    {
                        Deposit deposit = CreateDeposit(burnRequest, inspectForDepositsAtHeight);
                        if (deposit == null)
                            continue;

                        if (this.depositsBeingProcessedWithinMaturingWindow.Contains(deposit.Id))
                        {
                            this.logger.LogDebug($"Burn request '{burnRequest.RequestId}' is already being processed within the maturity window.");
                            continue;
                        }

                        deposits.Add(deposit);

                        this.depositsBeingProcessedWithinMaturingWindow.Add(deposit.Id);

                        this.logger.LogInformation($"Re-injecting burn request '{burnRequest.RequestId}' to '{burnRequest.DestinationAddress}' that was processed at {burnRequest.BlockHeight} and will mature at {burnRequest.BlockHeight + requiredConfirmations}.");

                        continue;
                    }
                }
            }
        }

        private Deposit CreateDeposit(ConversionRequest burnRequest, int inspectForDepositsAtHeight)
        {
            // We use the transaction ID from the Ethereum chain as the request ID for the withdrawal.
            // To parse it into a uint256 we need to trim the leading hex marker from the string.
            uint256 depositId;
            try
            {
                depositId = new uint256(burnRequest.RequestId.Replace("0x", ""));
            }
            catch (Exception)
            {
                return null;
            }

            DepositRetrievalType depositRetrievalType = DetermineDepositRetrievalType(burnRequest.Amount.GetLow64());
            var deposit = new Deposit(
                depositId,
                depositRetrievalType,
                Money.Satoshis(burnRequest.Amount.GetLow64()),
                burnRequest.DestinationAddress,
                DestinationChain.STRAX,
                inspectForDepositsAtHeight,
                0);

            return deposit;
        }

        /// <inheritdoc />
        public Task<IDeposit> ExtractDepositFromTransaction(Transaction transaction, int blockHeight, uint256 blockHash)
        {
            // If there are no deposits to the multsig (i.e. cross chain transfers) do nothing.
            if (!DepositValidationHelper.TryGetDepositsToMultisig(this.network, transaction, FederatedPegSettings.CrossChainTransferMinimum, out List<TxOut> depositsToMultisig))
                return Task.FromResult((IDeposit)null);

            Money amount = depositsToMultisig.Sum(o => o.Value);

            // If there are deposits to the multsig (i.e. cross chain transfers), try and extract and validate the address by the specified destination chain.

            // However, we need to check for distribution transactions first, as these should be processed regardless of whether the op return address is invalid.
            foreach (TxIn input in transaction.Inputs)
            {
                Transaction previousTransaction = this.blockStore.GetTransactionById(input.PrevOut.Hash);
                TxOut utxo = previousTransaction.Outputs[input.PrevOut.N];

                if (utxo.ScriptPubKey == StraxCoinstakeRule.CirrusRewardScript)
                {
                    return Task.FromResult((IDeposit)new Deposit(transaction.GetHash(), DepositRetrievalType.Distribution, amount, this.network.CirrusRewardDummyAddress, DestinationChain.STRAX, blockHeight, blockHash));
                }
            }

            if (!DepositValidationHelper.TryGetTarget(transaction, this.opReturnDataReader, out bool conversionTransaction, out string targetAddress, out int targetChain))
                return Task.FromResult((IDeposit)null);

            DepositRetrievalType depositRetrievalType;

            if (conversionTransaction)
            {
                if (this.federatedPegSettings.IsMainChain && amount < DepositValidationHelper.ConversionTransactionMinimum)
                {
                    this.logger.LogWarning($"Ignoring conversion transaction '{transaction.GetHash()}' with amount {amount} which is below the threshold of {DepositValidationHelper.ConversionTransactionMinimum}.");
                    return Task.FromResult((IDeposit)null);
                }

                this.logger.LogInformation("Received conversion deposit transaction '{0}' for an amount of {1}.", transaction.GetHash(), amount);

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

            return Task.FromResult((IDeposit)new Deposit(transaction.GetHash(), depositRetrievalType, amount, targetAddress, (DestinationChain)targetChain, blockHeight, blockHash));
        }

        private DepositRetrievalType DetermineDepositRetrievalType(ulong satoshiAmount)
        {
            if (satoshiAmount > this.federatedPegSettings.NormalDepositThresholdAmount)
                return DepositRetrievalType.Large;

            if (satoshiAmount > this.federatedPegSettings.SmallDepositThresholdAmount)
                return DepositRetrievalType.Normal;

            return DepositRetrievalType.Small;
        }
    }
}
