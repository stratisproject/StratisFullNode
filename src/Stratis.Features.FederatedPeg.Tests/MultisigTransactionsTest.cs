using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Features.FederatedPeg.Wallet;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    public class MultisigTransactionsTest
    {
        [Fact]
        public void TransactionsByDepositDictCantContainNullWithdrawals()
        {
            var multiSigTransactions = new MultiSigTransactions();

            // Add the transaction to the collection.
            var spendingDetails1 = new SpendingDetails()
            {
                WithdrawalDetails = new WithdrawalDetails()
                {
                    Amount = Money.Zero,
                    MatchingDepositId = 2
                }
            };
            
            var spendingDetails2 = new SpendingDetails()
            {
                WithdrawalDetails = new WithdrawalDetails()
                {
                    Amount = Money.Zero,
                    MatchingDepositId = 2
                }
            };

            TransactionData txData1Fact() => new TransactionData()
            {
                Amount = Money.Zero,
                Id = 1,
                Index = 0,
                SpendingDetails = spendingDetails1
            };

            TransactionData txData2Fact() => new TransactionData()
            {
                Amount = Money.Zero,
                Id = 2,
                Index = 0,
                SpendingDetails = spendingDetails2
            };

            TransactionData txData1 = txData1Fact();
            TransactionData txData2 = txData2Fact();

            multiSigTransactions.Add(txData1);
            multiSigTransactions.Add(txData2);

            // Retrieve the transaction.
            List<TransactionData> txDataMatch = multiSigTransactions.GetSpendingTransactionsByDepositId(2).Single().txList;
            Assert.Contains(txDataMatch, txDataMatch => txData1.Id == txDataMatch.Id && txData1.Index == txDataMatch.Index);
            Assert.Contains(txDataMatch, txDataMatch => txData2.Id == txDataMatch.Id && txData2.Index == txDataMatch.Index);

            multiSigTransactions.Remove(txData1Fact());
            multiSigTransactions.Remove(txData2Fact());

            multiSigTransactions.Add(txData1);
            multiSigTransactions.Add(txData2);

            // Replace the SpendingDetails with one containing WithdrawalDetails set to null.
            var spendingDetails3 = new SpendingDetails()
            {
                WithdrawalDetails = null
            };

            var spendingDetails4 = new SpendingDetails()
            {
                WithdrawalDetails = null
            };

            // Remove and restore one.
            txData2.SpendingDetails = spendingDetails4;
            Assert.Single(multiSigTransactions.GetSpendingTransactionsByDepositId(2).Single().txList);
            txData2.SpendingDetails = spendingDetails2;

            // Retrieve the transaction again.
            List<TransactionData> txDataMatch2 = multiSigTransactions.GetSpendingTransactionsByDepositId(2).Single().txList;
            Assert.Contains(txDataMatch2, txDataMatch => txData1.Id == txDataMatch.Id && txData1.Index == txDataMatch.Index);
            Assert.Contains(txDataMatch2, txDataMatch => txData2.Id == txDataMatch.Id && txData2.Index == txDataMatch.Index);

            // Remove and restore one.
            txData1.SpendingDetails = spendingDetails3;
            Assert.Single(multiSigTransactions.GetSpendingTransactionsByDepositId(2).Single().txList);
            txData1.SpendingDetails = spendingDetails1;

            // Retrieve the transaction again.
            List<TransactionData> txDataMatch3 = multiSigTransactions.GetSpendingTransactionsByDepositId(2).Single().txList;
            Assert.Contains(txDataMatch3, txDataMatch => txData1.Id == txDataMatch.Id && txData1.Index == txDataMatch.Index);
            Assert.Contains(txDataMatch3, txDataMatch => txData2.Id == txDataMatch.Id && txData2.Index == txDataMatch.Index);

            // Remove and restore both.
            txData1.SpendingDetails = spendingDetails3;
            txData2.SpendingDetails = spendingDetails4;
            Assert.Empty(multiSigTransactions.GetSpendingTransactionsByDepositId(2).Single().txList);
            txData1.SpendingDetails = spendingDetails1;
            txData2.SpendingDetails = spendingDetails2;

            // Retrieve the transaction again.
            List<TransactionData> txDataMatch4 = multiSigTransactions.GetSpendingTransactionsByDepositId(2).Single().txList;
            Assert.Contains(txDataMatch4, txDataMatch => txData1.Id == txDataMatch.Id && txData1.Index == txDataMatch.Index);
            Assert.Contains(txDataMatch4, txDataMatch => txData2.Id == txDataMatch.Id && txData2.Index == txDataMatch.Index);
        }
    }
}
