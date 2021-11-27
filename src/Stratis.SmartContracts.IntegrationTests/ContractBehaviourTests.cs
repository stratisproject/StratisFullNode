using System.Text;
using NBitcoin;
using Nethereum.RLP;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.Core.Util;
using Stratis.SmartContracts.RuntimeObserver;
using Stratis.SmartContracts.Tests.Common.MockChain;
using Xunit;

namespace Stratis.SmartContracts.IntegrationTests
{
    public abstract class ContractBehaviourTests<T> : IClassFixture<T> where T : class, IMockChainFixture
    {
        private readonly IMockChain mockChain;
        private readonly MockChainNode node1;
        private readonly MockChainNode node2;

        private readonly IAddressGenerator addressGenerator;
        private readonly ISenderRetriever senderRetriever;

        protected ContractBehaviourTests(T fixture)
        {
            this.mockChain = fixture.Chain;
            this.node1 = this.mockChain.Nodes[0];
            this.node2 = this.mockChain.Nodes[1];
            this.addressGenerator = new AddressGenerator();
            this.senderRetriever = new SenderRetriever();
        }

        [Fact]
        public void Persisting_Nothing_Null_And_Empty_Byte_Arrays_Are_The_Same()
        {
            // Demonstrates some potentially unusual behaviour when saving contract state.

            // Ensure fixture is funded.
            this.mockChain.MineBlocks(1);

            // Deploy contract
            ContractCompilationResult compilationResult = ContractCompiler.CompileFile("SmartContracts/BehaviourTest.cs");

            Assert.True(compilationResult.Success);
            BuildCreateContractTransactionResponse preResponse = this.node1.SendCreateContractTransaction(compilationResult.Compilation, 0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);
            Assert.NotNull(this.node1.GetCode(preResponse.NewContractAddress));

            uint256 currentHash = this.node1.GetLastBlock().GetHash();

            // Call CheckData() and confirm that it succeeds
            BuildCallContractTransactionResponse response = this.node1.SendCallContractTransaction(
                nameof(BehaviourTest.DataIsByte0),
                preResponse.NewContractAddress,
                0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            NBitcoin.Block lastBlock = this.node1.GetLastBlock();

            // Blocks progressed
            Assert.NotEqual(currentHash, lastBlock.GetHash());

            ReceiptResponse receipt = this.node1.GetReceipt(response.TransactionId.ToString());
            Assert.True(receipt.Success);

            // Invoke PersistEmptyString. It should succeed
             response = this.node1.SendCallContractTransaction(
                nameof(BehaviourTest.PersistEmptyString),
                preResponse.NewContractAddress,
                0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            lastBlock = this.node1.GetLastBlock();

            // Blocks progressed
            Assert.NotEqual(currentHash, lastBlock.GetHash());

            receipt = this.node1.GetReceipt(response.TransactionId.ToString());
            Assert.True(receipt.Success);

            // The storage value should be null, but the contract only sees it as a byte[0]
            Assert.Null(this.node1.GetStorageValue(preResponse.NewContractAddress, nameof(BehaviourTest.Data)));

            // Now call CheckData() to confirm that it's a byte[0]
            response = this.node1.SendCallContractTransaction(
                nameof(BehaviourTest.DataIsByte0),
                preResponse.NewContractAddress,
                0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            lastBlock = this.node1.GetLastBlock();

            // Blocks progressed
            Assert.NotEqual(currentHash, lastBlock.GetHash());
            
            receipt = this.node1.GetReceipt(response.TransactionId.ToString());
            Assert.True(receipt.Success);

            // Now call PersistNull, which should fail if the null is not returned as byte[0]
            response = this.node1.SendCallContractTransaction(
                nameof(BehaviourTest.PersistNull),
                preResponse.NewContractAddress,
                0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            lastBlock = this.node1.GetLastBlock();

            receipt = this.node1.GetReceipt(response.TransactionId.ToString());
            Assert.True(receipt.Success);

            // The storage value should be null
            Assert.Null(this.node1.GetStorageValue(preResponse.NewContractAddress, nameof(BehaviourTest.Data)));

            // Now call CheckData() to confirm that it's a byte[0]
            response = this.node1.SendCallContractTransaction(
                nameof(BehaviourTest.DataIsByte0),
                preResponse.NewContractAddress,
                0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            lastBlock = this.node1.GetLastBlock();

            // Blocks progressed
            Assert.NotEqual(currentHash, lastBlock.GetHash());

            receipt = this.node1.GetReceipt(response.TransactionId.ToString());
            Assert.True(receipt.Success);
        }

        [Fact]
        public void Local_Call_At_Height()
        {
            // Demonstrates some potentially unusual behaviour when saving contract state.
            var localExecutor = this.mockChain.Nodes[0].CoreNode.FullNode.NodeService<ILocalExecutor>();

            // Ensure fixture is funded.
            this.mockChain.MineBlocks(1);

            // Deploy contract
            ContractCompilationResult compilationResult = ContractCompiler.CompileFile("SmartContracts/StorageDemo.cs");

            Assert.True(compilationResult.Success);
            BuildCreateContractTransactionResponse preResponse = this.node1.SendCreateContractTransaction(compilationResult.Compilation, 0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);
            Assert.NotNull(this.node1.GetCode(preResponse.NewContractAddress));

            uint256 currentHash = this.node1.GetLastBlock().GetHash();

            NBitcoin.Block lastBlock = this.node1.GetLastBlock();

            ReceiptResponse receipt = this.node1.GetReceipt(preResponse.TransactionId.ToString());
            Assert.True(receipt.Success);

            // Get initial state at height
            var call = new ContractTxData(1, 100, (Gas)250000, preResponse.NewContractAddress.ToUint160(this.node1.CoreNode.FullNode.Network), nameof(StorageDemo.GetCounterValue));
            var initialState = localExecutor.Execute((ulong)this.node1.CoreNode.FullNode.ChainIndexer.Height, this.mockChain.Nodes[0].MinerAddress.Address.ToUint160(this.node1.CoreNode.FullNode.Network), 0, call);
            
            Assert.Equal(12345, (int)initialState.Return);

            // Call Counter() and confirm that it succeeds
            BuildCallContractTransactionResponse response = this.node1.SendCallContractTransaction(
                nameof(StorageDemo.Increment),
                preResponse.NewContractAddress,
                0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            lastBlock = this.node1.GetLastBlock();

            // Blocks progressed
            Assert.NotEqual(currentHash, lastBlock.GetHash());

            // Get local state at height 2 after calling counter
            initialState = localExecutor.Execute((ulong)this.node1.CoreNode.FullNode.ChainIndexer.Height, this.mockChain.Nodes[0].MinerAddress.Address.ToUint160(this.node1.CoreNode.FullNode.Network), 0, call);

            Assert.Equal(12346, (int)initialState.Return);

            // Get local state at previous height before calling counter
            initialState = localExecutor.Execute((ulong)this.node1.CoreNode.FullNode.ChainIndexer.Height - 1, this.mockChain.Nodes[0].MinerAddress.Address.ToUint160(this.node1.CoreNode.FullNode.Network), 0, call);

            Assert.Equal(12345, (int)initialState.Return);
        }

        [Fact]
        public void Local_Call_Should_Produce_Logs_And_Transfers()
        {
            // Demonstrates some potentially unusual behaviour when saving contract state.
            var localExecutor = this.mockChain.Nodes[0].CoreNode.FullNode.NodeService<ILocalExecutor>();

            // Ensure fixture is funded.
            this.mockChain.MineBlocks(1);

            // Deploy contract
            ContractCompilationResult compilationResult = ContractCompiler.CompileFile("SmartContracts/LocalCallTests.cs");

            Assert.True(compilationResult.Success);
            BuildCreateContractTransactionResponse preResponse = this.node1.SendCreateContractTransaction(compilationResult.Compilation, 10);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);
            Assert.NotNull(this.node1.GetCode(preResponse.NewContractAddress));
            Assert.Equal(1000000000UL, this.node1.GetContractBalance(preResponse.NewContractAddress));

            uint256 currentHash = this.node1.GetLastBlock().GetHash();

            NBitcoin.Block lastBlock = this.node1.GetLastBlock();

            ReceiptResponse receipt = this.node1.GetReceipt(preResponse.TransactionId.ToString());
            Assert.True(receipt.Success);

            // Create a log in a local call
            var call = new ContractTxData(1, 100, (Gas)250000, preResponse.NewContractAddress.ToUint160(this.node1.CoreNode.FullNode.Network), nameof(LocalCallTests.CreateLog));
            var createLogResult = localExecutor.Execute((ulong)this.node1.CoreNode.FullNode.ChainIndexer.Height, this.mockChain.Nodes[0].MinerAddress.Address.ToUint160(this.node1.CoreNode.FullNode.Network), 0, call);

            Assert.NotEmpty(createLogResult.Logs);
            RLPCollection collection = (RLPCollection)RLP.Decode(createLogResult.Logs[0].Data);
            var loggedData = Encoding.UTF8.GetString(collection[0].RLPData);
            Assert.Equal(nameof(LocalCallTests.CreateLog), loggedData);

            // Create a transfer in a local call
            call = new ContractTxData(1, 100, (Gas)250000, preResponse.NewContractAddress.ToUint160(this.node1.CoreNode.FullNode.Network), nameof(LocalCallTests.CreateTransfer));
            var createTransferResult = localExecutor.Execute((ulong)this.node1.CoreNode.FullNode.ChainIndexer.Height, this.mockChain.Nodes[0].MinerAddress.Address.ToUint160(this.node1.CoreNode.FullNode.Network), 0, call);

            Assert.NotEmpty(createTransferResult.InternalTransfers);
            Assert.Equal(Address.Zero.ToUint160(), createTransferResult.InternalTransfers[0].To);
            Assert.Equal(1UL, createTransferResult.InternalTransfers[0].Value);
        }

        [Fact]
        public void Local_Internal_Call_Should_Transfer_Value()
        {
            // Ref: https://github.com/stratisproject/StratisFullNode/pull/662#issuecomment-902379004
            var localExecutor = this.mockChain.Nodes[0].CoreNode.FullNode.NodeService<ILocalExecutor>();

            // Ensure fixture is funded.
            this.mockChain.MineBlocks(1);

            // Deploy contract 1
            ContractCompilationResult compilationResult = ContractCompiler.CompileFile("SmartContracts/ValueTransferTest.cs");

            Assert.True(compilationResult.Success);
            BuildCreateContractTransactionResponse valueTransferContractResponse = this.node1.SendCreateContractTransaction(compilationResult.Compilation, 0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            Assert.NotNull(this.node1.GetCode(valueTransferContractResponse.NewContractAddress));
            Assert.Equal(0UL, this.node1.GetContractBalance(valueTransferContractResponse.NewContractAddress));

            ReceiptResponse receipt = this.node1.GetReceipt(valueTransferContractResponse.TransactionId.ToString());
            Assert.True(receipt.Success);

            // Deploy contract 2
            var compilationResult2 = ContractCompiler.CompileFile("SmartContracts/ValueTransferRecipient.cs");

            Assert.True(compilationResult2.Success);
            var recipientResponse = this.node1.SendCreateContractTransaction(compilationResult2.Compilation, 0);
            this.mockChain.WaitAllMempoolCount(1);
            this.mockChain.MineBlocks(1);

            Assert.NotNull(this.node1.GetCode(recipientResponse.NewContractAddress));
            Assert.Equal(0UL, this.node1.GetContractBalance(recipientResponse.NewContractAddress));

            ReceiptResponse receipt2 = this.node1.GetReceipt(recipientResponse.TransactionId.ToString());
            Assert.True(receipt2.Success);

            uint256 currentHash = this.node1.GetLastBlock().GetHash();

            NBitcoin.Block lastBlock = this.node1.GetLastBlock();

            // Create a log in a local call.
            var parameters = new object[]
            {
                recipientResponse.NewContractAddress.ToAddress(this.node1.CoreNode.FullNode.Network)
            };

            var call = new ContractTxData(1, 100, (Gas)250000, valueTransferContractResponse.NewContractAddress.ToUint160(this.node1.CoreNode.FullNode.Network), nameof(ValueTransferTest.CanForwardValueCall), parameters);
            var internalTransferResult = localExecutor.Execute((ulong)this.node1.CoreNode.FullNode.ChainIndexer.Height, this.mockChain.Nodes[0].MinerAddress.Address.ToUint160(this.node1.CoreNode.FullNode.Network), 10, call);

            Assert.False(internalTransferResult.Revert);
            
            //Assert.NotEmpty(createLogResult.Logs);
            //RLPCollection collection = (RLPCollection)RLP.Decode(createLogResult.Logs[0].Data);
            //var loggedData = Encoding.UTF8.GetString(collection[0].RLPData);
            //Assert.Equal(nameof(LocalCallTests.CreateLog), loggedData);

            //// Create a transfer in a local call
            //call = new ContractTxData(1, 100, (Gas)250000, preResponse.NewContractAddress.ToUint160(this.node1.CoreNode.FullNode.Network), nameof(LocalCallTests.CreateTransfer));
            //var createTransferResult = localExecutor.Execute((ulong)this.node1.CoreNode.FullNode.ChainIndexer.Height, this.mockChain.Nodes[0].MinerAddress.Address.ToUint160(this.node1.CoreNode.FullNode.Network), 0, call);

            ////Assert.NotEmpty(createTransferResult.InternalTransfers);
            ////Assert.Equal(Address.Zero.ToUint160(), createTransferResult.InternalTransfers[0].To);
            ////Assert.Equal(1UL, createTransferResult.InternalTransfers[0].Value);
        }
    }

    public class PoAContractBehaviourTests : ContractBehaviourTests<PoAMockChainFixture>
    {
        public PoAContractBehaviourTests(PoAMockChainFixture fixture) : base(fixture)
        {
        }
    }

    public class PoWContractBehaviourTests : ContractBehaviourTests<PoWMockChainFixture>
    {
        public PoWContractBehaviourTests(PoWMockChainFixture fixture) : base(fixture)
        {
        }
    }
}
