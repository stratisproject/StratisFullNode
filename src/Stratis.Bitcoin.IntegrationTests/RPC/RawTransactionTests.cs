﻿using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.IntegrationTests.Common.ReadyData;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.IntegrationTests.RPC
{
    public class RawTransactionTests
    {
        private readonly Network network;

        public RawTransactionTests()
        {
            this.network = new StraxRegTest();
        }

        private Money GetTotalInputValue(CoreNode node, Transaction fundedTransaction)
        {
            Money totalInputs = 0;
            foreach (TxIn input in fundedTransaction.Inputs)
            {
                Transaction inputSource = node.CreateRPCClient().GetRawTransaction(input.PrevOut.Hash);

                TxOut prevOut = inputSource.Outputs[input.PrevOut.N];

                totalInputs += prevOut.Value;
            }

            return totalInputs;
        }

        /// <summary>
        /// Common checks that need to be performed on all funded raw transactions.
        /// As we cannot broadcast them to fully validate them these primarily relate to the choice of inputs.
        /// </summary>
        /// <returns>The computed fee amount.</returns>
        private Money CheckFunding(CoreNode node, Transaction fundedTransaction)
        {
            Assert.NotNull(fundedTransaction);
            Assert.NotEmpty(fundedTransaction.Inputs);

            // Need to check that the found inputs adequately fund the outputs.
            Money totalOutputs = fundedTransaction.TotalOut;
            Money totalInputs = this.GetTotalInputValue(node, fundedTransaction);
            Assert.True(totalInputs >= totalOutputs);

            // Return the computed fee as a convenience to the caller.
            return totalInputs - totalOutputs;
        }

        [Fact]
        public void CanFundRawTransactionWithoutOptions()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateStratisPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                var tx = this.network.CreateTransaction();
                var dest = new Key().ScriptPubKey;
                tx.Outputs.Add(new TxOut(Money.Coins(1.0m), dest));

                FundRawTransactionResponse funded = node.CreateRPCClient().FundRawTransaction(tx);

                Money fee = CheckFunding(node, funded.Transaction);

                Assert.Equal(new Money(this.network.MinRelayTxFee), fee);
                Assert.True(funded.ChangePos > -1);
            }
        }

        [Fact]
        public void CanFundRawTransactionWithChangeAddressSpecified()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateStratisPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                var tx = this.network.CreateTransaction();
                var dest = new Key().ScriptPubKey;
                tx.Outputs.Add(new TxOut(Money.Coins(1.0m), dest));

                // We specifically don't want to use the first available account as that is where the node has been mining to, and that is where the
                // fundrawtransaction RPC will by default get a change address from.
                // TODO: Investigate why WithReadyBlockchainData is not setting the node.WalletName and node.WalletPassword fields
                var account = node.FullNode.WalletManager().GetUnusedAccount("mywallet", "password");

                var walletAccountReference = new WalletAccountReference("mywallet", account.Name);
                var changeAddress = node.FullNode.WalletManager().GetUnusedChangeAddress(walletAccountReference);

                var options = new FundRawTransactionOptions()
                {
                    ChangeAddress = BitcoinAddress.Create(changeAddress.Address, this.network).ToString()
                };

                FundRawTransactionResponse funded = node.CreateRPCClient().FundRawTransaction(tx, options);

                Money fee = CheckFunding(node, funded.Transaction);

                Assert.Equal(new Money(this.network.MinRelayTxFee), fee);
                Assert.True(funded.ChangePos > -1);
            }
        }

        [Fact]
        public void CanFundRawTransactionWithChangePositionSpecified()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateStratisPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                var tx = this.network.CreateTransaction();
                tx.Outputs.Add(new TxOut(Money.Coins(1.1m), new Key().ScriptPubKey));
                tx.Outputs.Add(new TxOut(Money.Coins(1.2m), new Key().ScriptPubKey));
                tx.Outputs.Add(new TxOut(Money.Coins(1.3m), new Key().ScriptPubKey));
                tx.Outputs.Add(new TxOut(Money.Coins(1.4m), new Key().ScriptPubKey));

                Money totalSent = tx.TotalOut;

                // We specifically don't want to use the first available account as that is where the node has been mining to, and that is where the
                // fundrawtransaction RPC will by default get a change address from.
                var account = node.FullNode.WalletManager().GetUnusedAccount("mywallet", "password");

                var walletAccountReference = new WalletAccountReference("mywallet", account.Name);
                var changeAddress = node.FullNode.WalletManager().GetUnusedChangeAddress(walletAccountReference);

                var options = new FundRawTransactionOptions()
                {
                    ChangeAddress = BitcoinAddress.Create(changeAddress.Address, this.network).ToString(),
                    ChangePosition = 2
                };

                FundRawTransactionResponse funded = node.CreateRPCClient().FundRawTransaction(tx, options);

                Money fee = this.CheckFunding(node, funded.Transaction);

                Money totalInputs = this.GetTotalInputValue(node, funded.Transaction);

                Assert.Equal(new Money(this.network.MinRelayTxFee), fee);
                
                Assert.Equal(2, funded.ChangePos);
                Assert.Equal(changeAddress.ScriptPubKey, funded.Transaction.Outputs[funded.ChangePos].ScriptPubKey);
                
                // Check that the value of the change in the specified position is the expected value.
                Assert.Equal(totalInputs - totalSent - fee, funded.Transaction.Outputs[funded.ChangePos].Value);
            }
        }

        [Fact]
        public void CanSignRawTransaction()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateStratisPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                var tx = this.network.CreateTransaction();
                tx.Outputs.Add(new TxOut(Money.Coins(1.0m), new Key()));
                FundRawTransactionResponse funded = node.CreateRPCClient().FundRawTransaction(tx);

                node.CreateRPCClient().WalletPassphrase("password", 600);

                Transaction signed = node.CreateRPCClient().SignRawTransaction(funded.Transaction);

                Assert.NotNull(signed);
                Assert.NotEmpty(signed.Inputs);

                foreach (var input in signed.Inputs)
                {
                    Assert.NotNull(input.ScriptSig);

                    // Basic sanity check that the transaction has actually been signed.
                    // A segwit transaction would fail this check but we aren't checking that here.
                    // In any case, the mempool count test shows definitively if the transaction passes validation.
                    Assert.NotEqual(input.ScriptSig, Script.Empty);
                }

                node.CreateRPCClient().SendRawTransaction(signed);

                TestBase.WaitLoop(() => node.CreateRPCClient().GetRawMempool().Length == 1);
                TestHelper.MineBlocks(node, 1);
                TestBase.WaitLoop(() => node.CreateRPCClient().GetRawMempool().Length == 0);
            }
        }

        [Fact]
        public void CannotSignRawTransactionWithUnownedUtxo()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateStratisPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();
                CoreNode node2 = builder.CreateStratisPosNode(this.network).WithWallet().Start();

                TestHelper.ConnectAndSync(node, node2);
                TestBase.WaitLoop(() => node2.CreateRPCClient().GetBlockCount() >= 150);

                (HdAddress addressUsed, _) = TestHelper.MineBlocks(node2, 1);
                TransactionData otherFunds = addressUsed.Transactions.First();

                var tx = this.network.CreateTransaction();
                tx.Outputs.Add(new TxOut(Money.Coins(1.0m), new Key()));
                FundRawTransactionResponse funded = node.CreateRPCClient().FundRawTransaction(tx);

                // Add an additional (and unnecessary, but that doesn't matter) input belonging to the second node's wallet that the first node will not be able to sign for.
                funded.Transaction.Inputs.Add(new TxIn(new OutPoint(otherFunds.Id, otherFunds.Index)));

                node.CreateRPCClient().WalletPassphrase("password", 600);

                Assert.Throws<RPCException>(() => node.CreateRPCClient().SignRawTransaction(funded.Transaction));
            }
        }
    }
}
