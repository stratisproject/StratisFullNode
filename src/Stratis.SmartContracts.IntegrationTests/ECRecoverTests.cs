using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.SCL.Crypto;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Networks;
using Stratis.SmartContracts.Tests.Common.MockChain;
using Xunit;
using EcRecoverProvider = Stratis.SCL.Crypto.ECRecover;
using Key = NBitcoin.Key;

namespace Stratis.SmartContracts.IntegrationTests
{
    public class ECRecoverTests
    {
        // 2 things to test: 

        // 1) That we have the ECDSA code and can make it available.

        [Fact]
        public void CanSignAndRetrieveSender()
        {
            var network = new SmartContractsRegTest();
            var privateKey = new Key();
            Address address = privateKey.PubKey.GetAddress(network).ToString().ToAddress(network);
            byte[] message = new byte[] { 0x69, 0x76, 0xAA };

            // Sign a message
            byte[] offChainSignature = SignMessage(privateKey, message);

            // Get the address out of the signature
            EcRecoverProvider.TryGetSigner(message, offChainSignature, out Address recoveredAddress);

            // Check that the address matches that generated from the private key.
            Assert.Equal(address, recoveredAddress);
        }

        [Fact]
        public void GetSigner_Returns_Address_Zero_When_Message_Or_Signature_Null()
        {
            var network = new SmartContractsRegTest();
            var privateKey = new Key();
            Address address = privateKey.PubKey.GetAddress(network).ToString().ToAddress(network);
            byte[] message = new byte[] { 0x69, 0x76, 0xAA };

            // Sign a message
            byte[] offChainSignature = SignMessage(privateKey, message);

            // Get the address out of the signature
            Assert.False(EcRecoverProvider.TryGetSigner(null, offChainSignature, out Address recoveredAddress));

            Assert.Equal(Address.Zero, recoveredAddress);

            Assert.False(EcRecoverProvider.TryGetSigner(message, null, out Address recoveredAddress2));

            Assert.Equal(Address.Zero, recoveredAddress2);
        }

        /// <summary>
        /// Signs a message, returning an ECDSA signature.
        /// </summary>
        /// <param name="privateKey">The private key used to sign the message.</param>
        /// <param name="message">The complete message to be signed.</param>
        /// <returns>The ECDSA signature prepended with header information specifying the correct value of recId.</returns>
        private static byte[] SignMessage(Key privateKey, byte[] message)
        {
            uint256 hashedUint256 = new uint256(SHA3.Keccak256(message));

            return privateKey.SignCompact(hashedUint256);
        }

        [Fact]
        public void CanCallEcRecoverContractWithValidSignatureAsync()
        {
            using (PoWMockChain chain = new PoWMockChain(2))
            {
                var node1 = chain.Nodes[0];

                node1.MineBlocks(1);

                var network = chain.Nodes[0].CoreNode.FullNode.Network;

                var privateKey = new Key();
                string address = privateKey.PubKey.GetAddress(network).ToString();
                byte[] message = new byte[] { 0x69, 0x76, 0xAA };
                byte[] signature = SignMessage(privateKey, message);

                // TODO: If the incorrect parameters are passed to the constructor, the contract does not get properly created ('Method does not exist on contract'), but a success response is still returned?

                byte[] contract = ContractCompiler.CompileFile("SmartContracts/EcRecoverContract.cs").Compilation;
                string[] createParameters = new string[] { string.Format("{0}#{1}", (int)MethodParameterDataType.Address, address) };
                BuildCreateContractTransactionResponse createResult = node1.SendCreateContractTransaction(contract, 1, createParameters);

                Assert.NotNull(createResult);
                Assert.True(createResult.Success);

                node1.WaitMempoolCount(1);
                node1.MineBlocks(1);

                string[] callParameters = new string[]
                {
                    string.Format("{0}#{1}", (int)MethodParameterDataType.ByteArray, message.ToHexString()),
                    string.Format("{0}#{1}", (int)MethodParameterDataType.ByteArray, signature.ToHexString())
                };

                BuildCallContractTransactionResponse response = node1.SendCallContractTransaction("CheckThirdPartySignature", createResult.NewContractAddress, 1, callParameters);
                Assert.NotNull(response);
                Assert.True(response.Success);

                node1.WaitMempoolCount(1);
                node1.MineBlocks(1);

                ReceiptResponse receipt = node1.GetReceipt(response.TransactionId.ToString());

                Assert.NotNull(receipt);
                Assert.True(receipt.Success);
                Assert.Equal("True", receipt.ReturnValue);
            }
        }

        [Fact]
        public void CanCallEcRecoverContractWithInvalidSignatureAsync()
        {
            using (PoWMockChain chain = new PoWMockChain(2))
            {
                var node1 = chain.Nodes[0];

                node1.MineBlocks(1);

                var network = chain.Nodes[0].CoreNode.FullNode.Network;

                var privateKey = new Key();
                string address = privateKey.PubKey.GetAddress(network).ToString();
                byte[] message = new byte[] { 0x69, 0x76, 0xAA };

                // Make the signature with a key unrelated to the third party signer for the contract.
                byte[] signature = SignMessage(new Key(), message);

                // TODO: If the incorrect parameters are passed to the constructor, the contract does not get properly created ('Method does not exist on contract'), but a success response is still returned?

                byte[] contract = ContractCompiler.CompileFile("SmartContracts/EcRecoverContract.cs").Compilation;
                string[] createParameters = new string[] { string.Format("{0}#{1}", (int)MethodParameterDataType.Address, address) };
                BuildCreateContractTransactionResponse createResult = node1.SendCreateContractTransaction(contract, 1, createParameters);

                Assert.NotNull(createResult);
                Assert.True(createResult.Success);

                node1.WaitMempoolCount(1);
                node1.MineBlocks(1);

                string[] callParameters = new string[]
                {
                    string.Format("{0}#{1}", (int)MethodParameterDataType.ByteArray, message.ToHexString()),
                    string.Format("{0}#{1}", (int)MethodParameterDataType.ByteArray, signature.ToHexString())
                };

                BuildCallContractTransactionResponse response = node1.SendCallContractTransaction("CheckThirdPartySignature", createResult.NewContractAddress, 1, callParameters);
                Assert.NotNull(response);
                Assert.True(response.Success);

                node1.WaitMempoolCount(1);
                node1.MineBlocks(1);

                ReceiptResponse receipt = node1.GetReceipt(response.TransactionId.ToString());

                Assert.NotNull(receipt);
                Assert.True(receipt.Success);
                Assert.Equal("False", receipt.ReturnValue);
            }
        }

        // 2) That we can enable the method in new contracts without affecting the older contracts

        // TODO
    }
}