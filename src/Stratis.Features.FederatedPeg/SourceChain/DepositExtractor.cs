using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public sealed class DepositExtractor : IDepositExtractor
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
            if (targetAddress == this.network.CirrusRewardDummyAddress)
                depositRetrievalType = DepositRetrievalType.Distribution;
            else if (conversionTransaction)
                depositRetrievalType = DepositRetrievalType.Conversion;
            else if (amount > this.federatedPegSettings.NormalDepositThresholdAmount)
                depositRetrievalType = DepositRetrievalType.Large;
            else if (amount > this.federatedPegSettings.SmallDepositThresholdAmount)
                depositRetrievalType = DepositRetrievalType.Normal;
            else
                depositRetrievalType = DepositRetrievalType.Small;

            return new Deposit(transaction.GetHash(), depositRetrievalType, amount, targetAddress, blockHeight, blockHash);
        }
    }
}