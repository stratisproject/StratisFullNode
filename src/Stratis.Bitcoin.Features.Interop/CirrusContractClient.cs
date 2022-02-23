using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Settings;
using Stratis.Bitcoin.Features.SmartContracts.Models;
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
        /// <param name="amount">The amount of SRC20 tokens that should be minted. This is the full-precision amount multiplied by 10^contract_specific_decimals to give an integral amount.</param>
        /// <returns>The transactionId of the mint request submitted to the Cirrus multisig wallet contract.</returns>
        /// <remarks>The target SRC20 contract must obviously support the IMintable interface.</remarks>
        Task<MultisigTransactionIdentifiers> MintAsync(string contractAddress, string destinationAddress, BigInteger amount);

        /// <summary>
        /// Retrieves the receipt for a given smart contract invocation.
        /// </summary>
        /// <param name="txHash">The txid of the Cirrus transaction containing the smart contract call.</param>
        /// <returns>The <see cref="CirrusReceiptResponse"/> of the given receipt.</returns>
        Task<CirrusReceiptResponse> GetReceiptAsync(string txHash);

        /// <summary>
        /// Returns a block at the requested height.
        /// </summary>
        /// <param name="blockHeight">The requested height.</param>
        /// <returns>The requested block</returns>
        Task<NBitcoin.Block> GetBlockByHeightAsync(int blockHeight);

        Task<ConsensusTipModel> GetConsensusTipAsync();

        Task<TransactionVerboseModel> GetRawTransactionAsync(string transactionId);

        /// <summary>
        /// Gets the number of on-chain confirmations a given txid has. This would be the number of confirmations on the Cirrus chain.
        /// </summary>
        /// <param name="blockHeight">The height of the block that included the Cirrus transaction to retrieve the number of confirmations for.</param>
        /// <returns>The number of confirmations, and the block hash the transaction appears in, if any.</returns>
        public (int ConfirmationCount, string BlockHash) GetConfirmations(int blockHeight);

        /// <summary>
        /// Retrieves the number of confirmations a given multisig transactionId has. This is retrieved by invoking the Confirmations method of the multisig contract.
        /// </summary>
        /// <param name="transactionId">The integer multisig contract transaction identifier to retrieve the number of multisig confirmations for.</param>
        /// <returns>The number of confirmations.</returns>
        /// <remarks>This does not relate to the number of elapsed blocks.</remarks>
        Task<int> GetMultisigConfirmationCountAsync(BigInteger transactionId, ulong blockHeight);

        /// <summary>
        /// Calls the Confirm method on the Cirrus multisig contract, adding a confirmation for the supplied transactionId if invoked by one of the contract owners.
        /// </summary>
        /// <returns>If succesfull, the transaction hash of the confirmation transaction, else null and the error message.</returns>
        Task<(string TransactionHash, string Message)> ConfirmTransactionAsync(BigInteger transactionId);
    }

    /// <inheritdoc />
    public class CirrusContractClient : ICirrusContractClient
    {
        private const int GetReceiptWaitTimeSeconds = 180;
        public const string MultisigConfirmMethodName = "Confirm";
        public const string MultisigSubmitMethodName = "Submit";
        public const string SRC20MintMethodName = "Mint";

        private readonly CirrusInteropSettings cirrusInteropSettings;
        private readonly ChainIndexer chainIndexer;
        private readonly Serializer serializer;

        /// <summary>
        /// The constructor.
        /// </summary>
        /// <param name="interopSettings">The settings for the interoperability feature.</param>
        /// <param name="chainIndexer">The chain indexer instance so that we can retrieve the current chain height.</param>
        public CirrusContractClient(InteropSettings interopSettings, ChainIndexer chainIndexer)
        {
            this.cirrusInteropSettings = interopSettings.GetSettings<CirrusInteropSettings>();
            this.chainIndexer = chainIndexer;
            this.serializer = new Serializer(new ContractPrimitiveSerializerV2(this.chainIndexer.Network));
        }

        /// <inheritdoc />
        public async Task<MultisigTransactionIdentifiers> MintAsync(string contractAddress, string destinationAddress, BigInteger amount)
        {
            BuildCallContractTransactionResponse response;
            try
            {
                Address mintRecipient = destinationAddress.ToAddress(this.chainIndexer.Network);

                // Pack the parameters of the Mint method invocation into the format used by the multisig contract.
                byte[] accountBytes = this.serializer.Serialize(mintRecipient);
                byte[] accountBytesPadded = new byte[accountBytes.Length + 1];
                accountBytesPadded[0] = 9; // 9 = Address
                Array.Copy(accountBytes, 0, accountBytesPadded, 1, accountBytes.Length);

                byte[] amountBytes = this.serializer.Serialize(new UInt256(amount.ToByteArray()));
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
                    WalletName = this.cirrusInteropSettings.CirrusWalletCredentials.WalletName,
                    AccountName = this.cirrusInteropSettings.CirrusWalletCredentials.AccountName,
                    ContractAddress = this.cirrusInteropSettings.CirrusMultisigContractAddress,
                    MethodName = MultisigSubmitMethodName,
                    Amount = "0",
                    FeeAmount = "0.04", // TODO: Use proper fee estimation, as the fee may be higher with larger multisigs
                    Password = this.cirrusInteropSettings.CirrusWalletCredentials.WalletPassword,
                    GasPrice = 100,
                    GasLimit = 250_000,
                    Sender = this.cirrusInteropSettings.CirrusSmartContractActiveAddress,
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

                using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(180)))
                {
                    response = await this.cirrusInteropSettings.CirrusClientUrl
                    .AppendPathSegment("api/smartcontracts/build-and-send-call")
                    .PostJsonAsync(request, cancellation.Token)
                    .ReceiveJson<BuildCallContractTransactionResponse>()
                    .ConfigureAwait(false);

                    if (!response.Success)
                    {
                        return new MultisigTransactionIdentifiers
                        {
                            Message = response.Message,
                            TransactionHash = response.TransactionId?.ToString(),
                            TransactionId = -1
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new MultisigTransactionIdentifiers
                {
                    Message = $"Exception occurred trying to build and send the mint transaction: {ex}",
                    TransactionHash = "",
                    TransactionId = -1
                };
            }

            try
            {
                CirrusReceiptResponse receipt = await this.GetReceiptAsync(response.TransactionId.ToString()).ConfigureAwait(false);

                // If we still haven't mined the tx, return a message and fail the transfer.
                if (receipt == null)
                    return new MultisigTransactionIdentifiers
                    {
                        Message = $"The mint smart contract transaction did not get mined in {GetReceiptWaitTimeSeconds} seconds or something else went wrong, txid '{response.TransactionId}'.",
                        TransactionHash = "",
                        TransactionId = -1
                    };

                // This indicates an issue with the smart contract call, so return the receipt message.
                if (!receipt.Success)
                    return new MultisigTransactionIdentifiers
                    {
                        BlockHeight = receipt.BlockNumber.HasValue ? (int)receipt.BlockNumber : -1,
                        Message = $"Error calling the mint smart contract method for '{response.TransactionId}': {receipt.Error}",
                        TransactionHash = receipt.TransactionHash,
                        TransactionId = -1
                    };

                return new MultisigTransactionIdentifiers
                {
                    BlockHeight = receipt.BlockNumber.HasValue ? (int)receipt.BlockNumber : -1,
                    TransactionHash = receipt.TransactionHash,
                    TransactionId = int.Parse(receipt.ReturnValue)
                };
            }
            catch (Exception ex)
            {
                return new MultisigTransactionIdentifiers
                {
                    Message = $"Exception occurred trying to retrieve the receipt: {ex}",
                    TransactionHash = "",
                    TransactionId = -1
                };
            }
        }

        /// <inheritdoc />
        public async Task<CirrusReceiptResponse> GetReceiptAsync(string txHash)
        {
            CirrusReceiptResponse result = null;

            // We have to wait for the transaction to be mined.
            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(GetReceiptWaitTimeSeconds)))
            {
                while (true)
                {
                    if (cancellation.IsCancellationRequested)
                        break;

                    // We have to use our own model for this, as the ReceiptResponse used inside the node does not have public setters on its properties.
                    IFlurlResponse response = await this.cirrusInteropSettings.CirrusClientUrl
                        .AppendPathSegment("api/smartcontracts/receipt")
                        .SetQueryParam("txHash", txHash)
                        .AllowAnyHttpStatus()
                        .GetAsync()
                        .ConfigureAwait(false);

                    if (response.StatusCode == (int)HttpStatusCode.OK)
                    {
                        result = await response.GetJsonAsync<CirrusReceiptResponse>().ConfigureAwait(false);
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5));

                    continue;
                }
            }

            return result;
        }

        public async Task<NBitcoin.Block> GetBlockByHeightAsync(int blockHeight)
        {
            string blockHash = await this.cirrusInteropSettings.CirrusClientUrl
                .AppendPathSegment("api/Consensus/getblockhash")
                .SetQueryParam("height", blockHeight)
                .GetJsonAsync<string>()
                .ConfigureAwait(false);

            if (blockHash == null)
                return null;

            var hexResponse = await this.cirrusInteropSettings.CirrusClientUrl
                .AppendPathSegment("api/BlockStore/block")
                .SetQueryParam("Hash", blockHash)
                .SetQueryParam("ShowTransactionDetails", false)
                .SetQueryParam("OutputJson", false)
                .GetStringAsync()
                .ConfigureAwait(false);

            var block = NBitcoin.Block.Parse(JsonConvert.DeserializeObject<string>(hexResponse), this.chainIndexer.Network.Consensus.ConsensusFactory);
            return block;
        }

        public async Task<ConsensusTipModel> GetConsensusTipAsync()
        {
            ConsensusTipModel response = await this.cirrusInteropSettings.CirrusClientUrl
                .AppendPathSegment("api/Consensus/tip")
                .GetJsonAsync<ConsensusTipModel>()
                .ConfigureAwait(false);

            return response;
        }

        public async Task<TransactionVerboseModel> GetRawTransactionAsync(string transactionId)
        {
            TransactionVerboseModel response = await this.cirrusInteropSettings.CirrusClientUrl
                .AppendPathSegment("api/Node/getrawtransaction")
                .SetQueryParam("trxid", transactionId)
                .SetQueryParam("verbose", true)
                .GetJsonAsync<TransactionVerboseModel>()
                .ConfigureAwait(false);

            return response;
        }

        /// <inheritdoc />
        public (int ConfirmationCount, string BlockHash) GetConfirmations(int blockHeight)
        {
            if (blockHeight == 0 || blockHeight == -1)
                return (0, string.Empty);

            ChainedHeader header = this.chainIndexer.GetHeader(blockHeight);

            int confirmationCount = this.chainIndexer.Height - header.Height;

            return (confirmationCount, header.HashBlock.ToString());
        }

        /// <inheritdoc />
        public async Task<int> GetMultisigConfirmationCountAsync(BigInteger transactionId, ulong blockHeight)
        {
            var request = new LocalCallContractRequest
            {
                BlockHeight = blockHeight,
                Amount = "0",
                ContractAddress = this.cirrusInteropSettings.CirrusMultisigContractAddress,
                GasLimit = 250_000,
                GasPrice = 100,
                MethodName = "Confirmations",
                Parameters = new[] { "7#" + transactionId },
                Sender = this.cirrusInteropSettings.CirrusSmartContractActiveAddress
            };

            try
            {
                LocalExecutionResponse result = await this.cirrusInteropSettings.CirrusClientUrl
                    .AppendPathSegment("api/smartcontracts/local-call")
                    .AllowAnyHttpStatus()
                    .PostJsonAsync(request)
                    .ReceiveJson<LocalExecutionResponse>()
                    .ConfigureAwait(false);

                if (result.Return == null)
                    return 0;

                return (int)(long)result.Return;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<(string TransactionHash, string Message)> ConfirmTransactionAsync(BigInteger transactionId)
        {
            BuildCallContractTransactionResponse response;

            try
            {
                var request = new BuildCallContractTransactionRequest
                {
                    WalletName = this.cirrusInteropSettings.CirrusWalletCredentials.WalletName,
                    AccountName = this.cirrusInteropSettings.CirrusWalletCredentials.AccountName,
                    ContractAddress = this.cirrusInteropSettings.CirrusMultisigContractAddress,
                    MethodName = MultisigConfirmMethodName,
                    Amount = "0",
                    FeeAmount = "0.04", // TODO: Use proper fee estimation, as the fee may be higher with larger multisigs
                    Password = this.cirrusInteropSettings.CirrusWalletCredentials.WalletPassword,
                    GasPrice = 100,
                    GasLimit = 250_000,
                    Sender = this.cirrusInteropSettings.CirrusSmartContractActiveAddress,
                    Parameters = new string[]
                    {
                    // TransactionId - this is the integer assigned by the multisig contract that is used to identify which request is being confirmed
                    "7#" + ((ulong)transactionId).ToString()
                    }
                };

                response = await this.cirrusInteropSettings.CirrusClientUrl
                    .AppendPathSegment("api/smartcontracts/build-and-send-call")
                    .PostJsonAsync(request)
                    .ReceiveJson<BuildCallContractTransactionResponse>()
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return (null, $"Exception occurred trying to build and call the confirmation transaction for '{transactionId}': {ex}");
            }

            try
            {
                CirrusReceiptResponse receipt = await GetReceiptAsync(response.TransactionId.ToString()).ConfigureAwait(false);

                // If we still haven't mined the tx, return a message and fail the transfer.
                if (receipt == null)
                    return (null, $"The confirm transaction did not get mined in {GetReceiptWaitTimeSeconds} seconds or something else went wrong, txid '{response.TransactionId}'.");

                // This indicates an issue with the smart contract call, so return the receipt message.
                if (!receipt.Success)
                    return (null, $"Error confirming the transfer '{response.TransactionId}': {receipt.Error}");

                return (receipt.TransactionHash, null);
            }
            catch (Exception ex)
            {
                return (null, $"Exception occurred trying to retrieve the receipt: {ex}");
            }
        }
    }

    public class ConsensusTipModel
    {
        public string TipHash { get; set; }

        public int TipHeight { get; set; }
    }

    public class CirrusReceiptResponse
    {
        [JsonProperty("transactionHash")]
        public string TransactionHash { get; set; }

        [JsonProperty("blockHash")]
        public string BlockHash { get; set; }

        [JsonProperty("blockNumber")]
        public ulong? BlockNumber { get; set; }

        [JsonProperty("postState")]
        public string PostState { get; set; }

        [JsonProperty("gasUsed")]
        public ulong GasUsed { get; set; }

        [JsonProperty("from")]
        public string From { get; set; }

        [JsonProperty("to")]
        public string To { get; set; }

        [JsonProperty("newContractAddress")]
        public string NewContractAddress { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("returnValue")]
        public string ReturnValue { get; set; }

        [JsonProperty("bloom")]
        public string Bloom { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("logs")]
        public CirrusLogResponse[] Logs { get; set; }
    }

    public class CirrusLogResponse
    {
        [JsonProperty("address")]
        public string Address { get; set; }

        [JsonProperty("topics")]
        public string[] Topics { get; set; }

        [JsonProperty("data")]
        public string Data { get; set; }

        [JsonProperty("log")]
        public CirrusLogData Log { get; set; }
    }

    public class CirrusLogData
    {
        [JsonProperty("event")]
        public string Event { get; set; }

        [JsonProperty("data")]
        public IDictionary<string, object> Data { get; set; }
    }
}
