using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.Tests.Common.MockChain;
using Xunit;

namespace Stratis.SmartContracts.IntegrationTests
{
    public sealed class ReserveUtxoServiceIntegrationTests
    {
        [Fact]
        public void CanReserveAndUnreserveUtxosAsync()
        {
            using (var chain = new PoWMockChain(2))
            {
                MockChainNode sender = chain.Nodes[0];

                // Mine some coins so we have balance
                int maturity = (int)sender.CoreNode.FullNode.Network.Consensus.CoinbaseMaturity;
                sender.MineBlocks(maturity + 1);

                // Compile the Smart Contract
                ContractCompilationResult compilationResult = ContractCompiler.CompileFile("SmartContracts/StorageDemo.cs");
                Assert.True(compilationResult.Success);

                // Stop the wallet sync manager so that wallet's spendable inputs does not get updated.
                sender.CoreNode.FullNode.NodeService<IWalletSyncManager>().Stop();

                // Send the first transaction
                var responseOne = sender.SendCreateContractTransaction(compilationResult.Compilation, 0, feeAmount: 0.001M, gasPrice: SmartContractMempoolValidator.MinGasPrice, gasLimit: SmartContractFormatLogic.GasLimitMaximum / 2);
                Assert.True(responseOne.Success);

                // Send the second transaction, which should fail as the utxo was already reserved with transaction one.
                var responseTwo = sender.SendCreateContractTransaction(compilationResult.Compilation, 0, feeAmount: 0.001M, gasPrice: SmartContractMempoolValidator.MinGasPrice, gasLimit: SmartContractFormatLogic.GasLimitMaximum / 2);
                Assert.Null(responseTwo); // This needs to be done better so that we can check the actual message.
            }
        }
    }
}
