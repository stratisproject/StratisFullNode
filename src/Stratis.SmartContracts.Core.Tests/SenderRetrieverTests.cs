using System.Collections.Generic;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.Core.Util;
using Stratis.SmartContracts.Networks;
using Xunit;

namespace Stratis.SmartContracts.Core.Tests
{
    public class SenderRetrieverTests
    {
        private readonly Mock<ICoinView> coinView;
        private readonly Network network;
        private readonly SenderRetriever senderRetriever;

        public SenderRetrieverTests()
        {
            this.coinView = new Mock<ICoinView>();
            this.network = new SmartContractsRegTest();
            this.senderRetriever = new SenderRetriever();
        }

        [Fact]
        public void MissingOutput_Returns_False()
        {
            // Construct transaction.
            Transaction transaction = this.network.CreateTransaction();
            transaction.Inputs.Add(new TxIn());

            // Setup coinview to return as if the PrevOut does not exist.
            var fetchResponse = new FetchCoinsResponse();

            this.coinView.Setup(x => x.FetchCoins(It.IsAny<OutPoint[]>()))
                .Returns(fetchResponse);

            var blockTxs = new List<Transaction>();

            // Retriever fails but doesn't throw exception
            GetSenderResult result = this.senderRetriever.GetSender(transaction, this.coinView.Object, blockTxs);
            Assert.False(result.Success);
            Assert.Equal(SenderRetriever.OutputsNotInCoinView, result.Error);
        }

        [Fact]
        public void SpentOutput_Returns_False()
        {
            // Construct transaction.
            Transaction transaction = this.network.CreateTransaction();
            transaction.Inputs.Add(new TxIn(new OutPoint(uint256.One, 0)));

            // Setup coinview to return as if the PrevOut is spent (i.e. Coins is null).
            var unspentOutput = new UnspentOutput(transaction.Inputs[0].PrevOut, null);

            var unspentOutputArray = new UnspentOutput[]
            {
                unspentOutput
            };

            var fetchResponse = new FetchCoinsResponse();
            fetchResponse.UnspentOutputs.Add(unspentOutputArray[0].OutPoint, unspentOutput);

            this.coinView.Setup(x => x.FetchCoins(It.IsAny<OutPoint[]>()))
                .Returns(fetchResponse);

            var blockTxs = new List<Transaction>();

            // Retriever fails but doesn't throw exception
            GetSenderResult result = this.senderRetriever.GetSender(transaction, this.coinView.Object, blockTxs);
            Assert.False(result.Success);
            Assert.Equal(SenderRetriever.OutputAlreadySpent, result.Error);
        }

        [Fact]
        public void InvalidPrevOutIndex_Returns_False()
        {
            // Construct transaction with a reference to prevout index of 2.
            Transaction transaction = this.network.CreateTransaction();
            transaction.Inputs.Add(new TxIn(new OutPoint(uint256.One, 2)));

            // Here we emulate the output not being found within the UTXO set at all. Spent-ness would be indicated
            // by the Coins field being null. The UnspentOutputs dictionary being empty indicates that the UTXO was not found.
            // This looks very much like the 'missing output' test, but in practice it would be a distinct case, perhaps better left to an integration test.
            var fetchResponse = new FetchCoinsResponse();

            this.coinView.Setup(x => x.FetchCoins(It.IsAny<OutPoint[]>()))
                .Returns(fetchResponse);

            var blockTxs = new List<Transaction>();

            // Retriever fails but doesn't throw IndexOutOfRangeException
            GetSenderResult result = this.senderRetriever.GetSender(transaction, this.coinView.Object, blockTxs);
            Assert.False(result.Success);
            Assert.Equal(SenderRetriever.OutputsNotInCoinView, result.Error);
        }

        [Fact]
        public void InvalidPrevOutIndex_InsideBlock_Returns_False()
        {
            // Construct transaction with a reference to prevout index of 2.
            Transaction prevOutTransaction = this.network.CreateTransaction();
            prevOutTransaction.AddOutput(0, new Script());
            Transaction transaction = this.network.CreateTransaction();
            transaction.Inputs.Add(new TxIn(new OutPoint(prevOutTransaction, 2)));

            // Put referenced PrevOut as if it was earlier in the block
            var blockTxs = new List<Transaction>
            {
                prevOutTransaction
            };

            // Retriever fails but doesn't throw IndexOutOfRangeException
            GetSenderResult result = this.senderRetriever.GetSender(transaction, null, blockTxs);
            Assert.False(result.Success);
            Assert.Equal(SenderRetriever.InvalidOutputIndex, result.Error);
        }

        [Fact]
        public void NoCoinViewOrTransactions_Returns_False()
        {
            // Construct transaction.
            Transaction transaction = this.network.CreateTransaction();
            transaction.Inputs.Add(new TxIn());

            // Retriever fails - no transactions to draw from
            GetSenderResult result = this.senderRetriever.GetSender(transaction, null, null);
            Assert.False(result.Success);
            Assert.Equal(SenderRetriever.UnableToGetSender, result.Error);
        }
    }
}
