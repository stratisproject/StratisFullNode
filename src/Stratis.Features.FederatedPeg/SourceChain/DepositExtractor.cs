using System.Collections.Generic;
using System.Linq;
using NBitcoin;
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

        public DepositExtractor(IFederatedPegSettings federatedPegSettings, Network network, IOpReturnDataReader opReturnDataReader)
        {
            this.depositScript = federatedPegSettings.MultiSigRedeemScript.PaymentScript;
            this.federatedPegSettings = federatedPegSettings;
            this.network = network;
            this.opReturnDataReader = opReturnDataReader;
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
                    "qeyK7poxBE1wy8H24a7AcpLySCsfiqAo6A", 2_177_900, 0)
                }
            },

            { "CirrusMain", new List<IDeposit> {
                new Deposit(
                    uint256.Parse("6179ee3332348948641210e2c9358a41aa8aaf2924d783e52bc4cec47deef495") /* Tx of deposit being redone */, DepositRetrievalType.Normal,
                    new Money(239, MoneyUnit.BTC), "XNrgftud4ExFL7dXEHjPPx3JX22qvFy39v", 2500000, 0),
                new Deposit(
                    uint256.Parse("b8417bdbe7690609b2fff47d6d8b39bed69993df6656d7e10197c141bdacdd90") /* Tx of deposit being redone */, DepositRetrievalType.Normal,
                    new Money(640, MoneyUnit.BTC), "XCDeD5URnSbExLsuVYu7yufpbmb6KvaX8D", 2500000, 0),
                new Deposit(
                    uint256.Parse("375ed4f028d3ef32397641a0e307e1705fc3f8ba76fa776ce33fff53f52b0e1c") /* Tx of deposit being redone */, DepositRetrievalType.Large,
                    new Money(1649, MoneyUnit.BTC), "XCrHzx5AUnNChCj8MP9j82X3Y7gLAtULtj", 2500000, 0),
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

            return new Deposit(transaction.GetHash(), depositRetrievalType, amount, targetAddress, blockHeight, blockHash);
        }
    }
}