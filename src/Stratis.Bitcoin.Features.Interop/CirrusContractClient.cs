using System;
using System.Numerics;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Interfaces;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Serialization;

namespace Stratis.Bitcoin.Features.Interop
{
    /// <summary>
    /// A helper class to make smart contract calls via API against the Cirrus chain.
    /// </summary>
    /// <remarks>This is not a drop-in replacement for IEthClient, but it does implement several of the same methods.</remarks>
    public interface ICirrusContractClient
    {
        /// <summary>
        /// Submits a multisig transaction with its encoded parameters set to invoke the Mint method on a target SRC20 contract.
        /// </summary>
        /// <param name="contractAddress">The SRC20 contract address that tokens should be minted for.</param>
        /// <param name="destinationAddress">The Cirrus address that the SRC20 tokens should be minted and assigned to.</param>
        /// <param name="amount">The amount of SRC20 tokens that should be minted.</param>
        /// <returns>The transactionId of the mint request submitted to the Cirrus multisig wallet contract.</returns>
        /// <remarks>The target SRC20 contract must obviously support the IMintable interface.</remarks>
        Task<MultisigTransactionIdentifiers> MintAsync(string contractAddress, string destinationAddress, Money amount);

        /// <summary>
        /// Retrieves the receipt for a given smart contract invocation.
        /// </summary>
        /// <param name="txHash">The txid of the Cirrus transaction containing the smart contract call.</param>
        /// <returns>The <see cref="ReceiptResponse"/> of the given receipt.</returns>
        Task<ReceiptResponse> GetReceiptAsync(string txHash);

        /// <summary>
        /// Gets the number of on-chain confirmations a given txid has. This would be the number of confirmations on the Cirrus chain.
        /// </summary>
        /// <param name="transactionHash">The txid of the Cirrus transaction to retrieve the number of confirmations for.</param>
        /// <returns>The number of confirmations, and the block hash the transaction appears in, if any.</returns>
        Task<(int ConfirmationCount, string BlockHash)> GetConfirmationsAsync(string transactionHash);

        /// <summary>
        /// Retrieves the number of confirmations a given multisig transactionId has. This is retrieved by invoking the Confirmations method of the multisig contract.
        /// </summary>
        /// <param name="transactionId">The integer multisig contract transaction identifier to retrieve the number of multisig confirmations for.</param>
        /// <returns>The number of confirmations.</returns>
        /// <remarks>This does not relate to the number of elapsed blocks.</remarks>
        Task<int> GetMultisigConfirmationCountAsync(BigInteger transactionId);

        /// <summary>
        /// Calls the Confirm method on the Cirrus multisig contract, adding a confirmation for the supplied transactionId if invoked by one of the contract owners.
        /// </summary>
        Task<string> ConfirmTransactionAsync(BigInteger transactionId);
    }

    /// <inheritdoc />
    public class CirrusContractClient : ICirrusContractClient
    {
        public const string MultisigConfirmMethodName = "Confirm";
        public const string MultisigSubmitMethodName = "Submit";
        public const string SRC20MintMethodName = "Mint";

        private readonly InteropSettings interopSettings;
        private readonly IBlockStore blockStore;
        private readonly ChainIndexer chainIndexer;
        private readonly Serializer serializer;

        /// <summary>
        /// The constructor.
        /// </summary>
        /// <param name="interopSettings">The settings for the interoperability feature.</param>
        public CirrusContractClient(InteropSettings interopSettings, IBlockStore blockStore, ChainIndexer chainIndexer)
        {
            this.interopSettings = interopSettings;
            this.blockStore = blockStore;
            this.chainIndexer = chainIndexer;
            this.serializer = new Serializer(new ContractPrimitiveSerializerV2(this.chainIndexer.Network));
        }

        /// <inheritdoc />
        public async Task<MultisigTransactionIdentifiers> MintAsync(string contractAddress, string destinationAddress, Money amount)
        {
            Address mintRecipient = destinationAddress.ToAddress(this.chainIndexer.Network);

            // Pack the parameters of the Mint method invocation into the format used by the multisig contract.
            byte[] accountBytes = this.serializer.Serialize(mintRecipient);
            byte[] accountBytesPadded = new byte[accountBytes.Length + 1];
            accountBytesPadded[0] = 9; // 9 = Address
            Array.Copy(accountBytes, 0, accountBytesPadded, 1, accountBytes.Length);
            
            byte[] amountBytes = this.serializer.Serialize((UInt256)amount.Satoshi);
            byte[] amountBytesPadded = new byte[amountBytes.Length + 1];
            amountBytesPadded[0] = 12; // 12 = UInt256
            Array.Copy(amountBytes, 0, amountBytesPadded, 1, amountBytes.Length);

            byte[] output = this.serializer.Serialize(new byte[][]
            {
                accountBytesPadded,
                amountBytesPadded
            });
            
            string mintDataHex = BitConverter.ToString(output).Replace("-", "");

            var request = new BuildCallContractTransactionRequest
            {
                WalletName = this.interopSettings.WalletCredentials.WalletName,
                AccountName = this.interopSettings.WalletCredentials.AccountName,
                ContractAddress = this.interopSettings.CirrusMultisigContractAddress,
                MethodName = MultisigSubmitMethodName,
                Amount = "0",
                FeeAmount = "0.04", // TODO: Use proper fee estimation, as the fee may be higher with larger multisigs
                Password = this.interopSettings.WalletCredentials.WalletPassword,
                GasPrice = 100,
                GasLimit = 250_000,
                Sender = this.interopSettings.CirrusSmartContractActiveAddress,
                Parameters = new string[]
                {
                    // Destination - this is the SRC20 contract that the mint will be invoked against, *not* the Cirrus address the minted tokens will be sent to
                    "9#" + contractAddress,
                    // MethodName
                    "4#" + SRC20MintMethodName,
                    // Data - this is an analogue of the ABI-encoded data used in Ethereum contract calls
                    "10#" + mintDataHex
                }
            };

            BuildCallContractTransactionResponse response = await this.interopSettings.CirrusClientUrl
                .AppendPathSegment("api/smartcontracts/build-and-send-call")
                .PostJsonAsync(request)
                .ReceiveJson<BuildCallContractTransactionResponse>()
                .ConfigureAwait(false);

            ReceiptResponse receipt = await this.GetReceiptAsync(response.TransactionId.ToString()).ConfigureAwait(false);

            if (!receipt.Success)
            {
                return new MultisigTransactionIdentifiers
                {
                    TransactionHash = "",
                    TransactionId = -1
                };
            }
            
            return new MultisigTransactionIdentifiers
            {
                TransactionHash = receipt.TransactionHash,
                TransactionId = int.Parse(receipt.ReturnValue)
            };
        }

        /// <inheritdoc />
        public async Task<ReceiptResponse> GetReceiptAsync(string txHash)
        {
            ReceiptResponse response = await this.interopSettings.CirrusClientUrl
                .AppendPathSegment("api/smartcontracts/receipt")
                .SetQueryParam("txHash", txHash)
                .GetJsonAsync<ReceiptResponse>()
                .ConfigureAwait(false);

            return response;
        }

        /// <inheritdoc />
        public async Task<(int ConfirmationCount, string BlockHash)> GetConfirmationsAsync(string txId)
        {
            uint256 block = this.blockStore.GetBlockIdByTransactionId(uint256.Parse(txId));

            if (block == null)
                return (0, string.Empty);

            ChainedHeader header = this.chainIndexer.GetHeader(block);

            int confirmationCount = this.chainIndexer.Height - header.Height;

            return (confirmationCount, header.HashBlock.ToString());
        }

        /// <inheritdoc />
        public async Task<int> GetMultisigConfirmationCountAsync(BigInteger transactionId)
        {
            var request = new LocalCallContractRequest
            {
                Amount = "0",
                ContractAddress = this.interopSettings.CirrusMultisigContractAddress,
                GasLimit = 250_000,
                GasPrice = 100,
                MethodName = "Confirmations",
                Parameters = new[] { "7#" + transactionId },
                Sender = this.interopSettings.CirrusSmartContractActiveAddress
            };

            int confirmationCount = await this.interopSettings.CirrusClientUrl
                .AppendPathSegment("api/smartcontracts/local-call")
                .PostJsonAsync(request)
                .ReceiveJson<int>()
                .ConfigureAwait(false);

            return confirmationCount;
        }

        /// <inheritdoc />
        public async Task<string> ConfirmTransactionAsync(BigInteger transactionId)
        {
            var request = new BuildCallContractTransactionRequest
            {
                WalletName = this.interopSettings.WalletCredentials.WalletName,
                AccountName = this.interopSettings.WalletCredentials.AccountName,
                ContractAddress = this.interopSettings.CirrusMultisigContractAddress,
                MethodName = MultisigConfirmMethodName,
                Amount = "0",
                FeeAmount = "0.04", // TODO: Use proper fee estimation, as the fee may be higher with larger multisigs
                Password = this.interopSettings.WalletCredentials.WalletPassword,
                GasPrice = 100,
                GasLimit = 250_000,
                Sender = this.interopSettings.CirrusSmartContractActiveAddress,
                Parameters = new string[]
                {
                    // TransactionId - this is the integer assigned by the multisig contract that is used to identify which request is being confirmed
                    "7#" + ((ulong)transactionId).ToString()
                }
            };

            BuildCallContractTransactionResponse response = await this.interopSettings.CirrusClientUrl
                .AppendPathSegment("api/smartcontracts/build-and-send-call")
                .PostJsonAsync(request)
                .ReceiveJson<BuildCallContractTransactionResponse>()
                .ConfigureAwait(false);

            ReceiptResponse receipt = await this.GetReceiptAsync(response.TransactionId.ToString()).ConfigureAwait(false);

            return receipt.TransactionHash;
        }
    }
}
