using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public sealed class DepositExtractor : IDepositExtractor
    {
        // Conversion transaction deposits smaller than this threshold will be ignored. Denominated in STRAX.
        public const decimal ConversionTransactionMinimum = 90_000;

        private readonly Script depositScript;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly Network network;
        private readonly IOpReturnDataReader opReturnDataReader;

        public DepositExtractor(IFederatedPegSettings federatedPegSettings, Network network, IOpReturnDataReader opReturnDataReader)
        {
            this.depositScript = federatedPegSettings.MultiSigRedeemScript.PaymentScript;
            this.federatedPegSettings = federatedPegSettings;
            this.network = network;
            this.opReturnDataReader = opReturnDataReader;
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
                if (amount < Money.Coins(ConversionTransactionMinimum))
                    return null;

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


            return new Deposit(transaction.GetHash(), depositRetrievalType, amount, targetAddress, (DestinationChain)targetChain, blockHeight, blockHash);
        }
    }
}