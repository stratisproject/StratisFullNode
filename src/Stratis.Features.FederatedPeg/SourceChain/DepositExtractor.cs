using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Features.FederatedPeg.Conversion;
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

        public DepositExtractor(IFederatedPegSettings federatedPegSettings, Network network)
        {
            this.depositScript = federatedPegSettings.MultiSigRedeemScript.PaymentScript;
            this.federatedPegSettings = federatedPegSettings;
            this.network = network;
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
            
            DestinationChain targetChain;
            
            byte[] opReturnBytes = OpReturnDataReader.SelectBytesContentFromOpReturn(transaction).FirstOrDefault();

            if (opReturnBytes != null && InterFluxOpReturnEncoder.TryDecode(opReturnBytes, out int destinationChain, out string targetAddress))
            {
                targetChain = (DestinationChain) destinationChain;
            }
            else
                return null;

            Money amount = depositsToMultisig.Sum(o => o.Value);

            DepositRetrievalType depositRetrievalType;

            if (targetChain == DestinationChain.STRAX)
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
            else
            {
                if (amount < Money.Coins(ConversionTransactionMinimum))
                    return null;

                if (amount > this.federatedPegSettings.NormalDepositThresholdAmount)
                    depositRetrievalType = DepositRetrievalType.ConversionLarge;
                else if (amount > this.federatedPegSettings.SmallDepositThresholdAmount)
                    depositRetrievalType = DepositRetrievalType.ConversionNormal;
                else
                    depositRetrievalType = DepositRetrievalType.ConversionSmall;
            }

            return new Deposit(transaction.GetHash(), depositRetrievalType, amount, targetAddress, targetChain, blockHeight, blockHash);
        }
    }
}