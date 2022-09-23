using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Settings;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.Wallet.Models;
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
        /// Submits a multisig transaction with its encoded parameters set to invoke the Mint method on a target SRC721 contract.
        /// </summary>
        /// <param name="contractAddress">The SRC20 contract address that tokens should be minted for.</param>
        /// <param name="destinationAddress">The Cirrus address that the SRC20 tokens should be minted and assigned to.</param>
        /// <param name="tokenId">The tokenId of the SRC721 token to be minted.</param>
        /// <param name="uri">The URI of the SRC721 token to be minted.</param>
        /// <returns>The transactionId of the mint request submitted to the Cirrus multisig wallet contract.</returns>
        Task<MultisigTransactionIdentifiers> MintNftAsync(string contractAddress, string destinationAddress, BigInteger tokenId, string uri);

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

        Task<TransactionVerboseModel> GetRawTransactionAsync(string transactionId);

        Task<WalletStatsModel> GetWalletStatsAsync(string walletName, string accountName, int minConfirmations = 1, bool verbose = false);

        Task<string> ConsolidateAsync(string walletName, string accountName, string walletPassword, bool broadcast = true);

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

        Task<string> GetKeyValueStoreAsync(string address, string key, ulong blockHeight);

        Task<MultisigTransactionIdentifiers> SetKeyValueStoreAsync(string key, string value);
    }

    /// <inheritdoc />
    public class CirrusContractClient : ICirrusContractClient
    {
        private const int GetReceiptWaitTimeSeconds = 180;
        public const string KeyValueGetMethodName = "Get";
        public const string KeyValueSetMethodName = "Set";
        public const string MultisigConfirmMethodName = "Confirm";
        public const string MultisigSubmitMethodName = "Submit";
        public const string SRC20MintMethodName = "Mint";
        public const string SRC721MintMethodName = "Mint";

        private readonly CirrusInteropSettings cirrusInteropSettings;
        private readonly ChainIndexer chainIndexer;
        private readonly Serializer serializer;
        private readonly ILogger logger;

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

            this.logger = LogManager.GetCurrentClassLogger();
        }

        private async Task<MultisigTransactionIdentifiers> MultisigContractCallInternalAsync(string contractAddress, string methodName, string methodDataHex)
        {
            BuildCallContractTransactionResponse response;
            try
            {
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
                    // Destination - in the case of a mint this is the SRC20/SRC721 contract that the mint will be invoked against, *not* the Cirrus address the minted tokens will be sent to
                    "9#" + contractAddress,
                    // MethodName
                    "4#" + methodName,
                    // Data - this is an analogue of the ABI-encoded data used in Ethereum contract calls
                    "10#" + methodDataHex
                    }
                };

                this.logger.LogDebug($"{nameof(contractAddress)}:{contractAddress} {nameof(methodName)}:{methodName} {nameof(methodDataHex)}:{methodDataHex}");

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

        private byte[] GetMintData(Address mintRecipient, BigInteger amount)
        {
            // Pack the parameters of the Mint method invocation into the format used by the multisig contract.
            byte[] accountBytes = this.serializer.Serialize(mintRecipient);
            byte[] accountBytesPadded = CreatePaddedParameterArray(accountBytes, 9); // 9 = Address

            byte[] amountBytes = this.serializer.Serialize(new UInt256(amount.ToByteArray()));
            byte[] amountBytesPadded = CreatePaddedParameterArray(amountBytes, 12); // 12 = UInt256

            return this.serializer.Serialize(new byte[][]
            {
                accountBytesPadded,
                amountBytesPadded
            });
        }

        /// <inheritdoc />
        public async Task<MultisigTransactionIdentifiers> MintAsync(string contractAddress, string destinationAddress, BigInteger amount)
        {
            try
            {
                Address mintRecipient = destinationAddress.ToAddress(this.chainIndexer.Network);

                byte[] mintData = GetMintData(mintRecipient, amount);

                string mintDataHex = BitConverter.ToString(mintData).Replace("-", "");

                return await MultisigContractCallInternalAsync(contractAddress, SRC20MintMethodName, mintDataHex).ConfigureAwait(false);
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
        }

        private byte[] GetMintNftData(Address mintRecipient, BigInteger tokenId, string uri)
        {
            // Pack the parameters of the SRC721 Mint method invocation into the format used by the multisig contract.
            byte[] accountBytes = this.serializer.Serialize(mintRecipient);
            byte[] accountBytesPadded = CreatePaddedParameterArray(accountBytes, 9); // 9 = Address

            byte[] tokenIdBytes = this.serializer.Serialize(new UInt256(tokenId.ToByteArray()));
            byte[] tokenIdBytesPadded = CreatePaddedParameterArray(tokenIdBytes, 12); // 12 = UInt256

            byte[] uriBytes = this.serializer.Serialize(uri);
            byte[] uriBytesPadded = CreatePaddedParameterArray(uriBytes, 4); // 4 = String

            return this.serializer.Serialize(new byte[][]
            {
                accountBytesPadded,
                tokenIdBytesPadded,
                uriBytesPadded
            });
        }

        /// <inheritdoc />
        public async Task<MultisigTransactionIdentifiers> MintNftAsync(string contractAddress, string destinationAddress, BigInteger tokenId, string uri)
        {
            try
            {
                Address mintRecipient = destinationAddress.ToAddress(this.chainIndexer.Network);

                byte[] mintData = GetMintNftData(mintRecipient, tokenId, uri);

                string mintDataHex = BitConverter.ToString(mintData).Replace("-", "");

                return await MultisigContractCallInternalAsync(contractAddress, SRC721MintMethodName, mintDataHex).ConfigureAwait(false);
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
            string blockHash;

            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(GetReceiptWaitTimeSeconds)))
            {
                blockHash = await this.cirrusInteropSettings.CirrusClientUrl
                .AppendPathSegment("api/Consensus/getblockhash")
                .SetQueryParam("height", blockHeight)
                .GetJsonAsync<string>(cancellation.Token)
                .ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(blockHash))
                return null;

            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(GetReceiptWaitTimeSeconds)))
            {
                var hexResponse = await this.cirrusInteropSettings.CirrusClientUrl
                .AppendPathSegment("api/BlockStore/block")
                .SetQueryParam("Hash", blockHash)
                .SetQueryParam("ShowTransactionDetails", false)
                .SetQueryParam("OutputJson", false)
                .GetStringAsync(cancellation.Token)
                .ConfigureAwait(false);

                var block = NBitcoin.Block.Parse(JsonConvert.DeserializeObject<string>(hexResponse), this.chainIndexer.Network.Consensus.ConsensusFactory);
                return block;
            }
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

        public async Task<WalletStatsModel> GetWalletStatsAsync(string walletName, string accountName, int minConfirmations = 1, bool verbose = false)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(180)))
            {
                WalletStatsModel response = await this.cirrusInteropSettings.CirrusClientUrl
                    .AppendPathSegment("api/Wallet/wallet-stats")
                    .SetQueryParam("WalletName", walletName)
                    .SetQueryParam("AccountName", accountName)
                    .SetQueryParam("MinConfirmations", minConfirmations)
                    .SetQueryParam("Verbose", verbose)
                    .GetJsonAsync<WalletStatsModel>(cancellation.Token)
                    .ConfigureAwait(false);

                return response;
            }
        }

        public async Task<string> ConsolidateAsync(string walletName, string accountName, string walletPassword, bool broadcast = true)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(600)))
            {
                try
                {
                    var consolidate = new ConsolidationRequest
                    {
                        WalletName = walletName,
                        AccountName = accountName,
                        WalletPassword = walletPassword,
                        DestinationAddress = this.cirrusInteropSettings.CirrusSmartContractActiveAddress,
                        Broadcast = broadcast
                    };

                    IFlurlResponse response = await this.cirrusInteropSettings.CirrusClientUrl
                        .AppendPathSegment("api/Wallet/consolidate")
                        .AllowAnyHttpStatus()
                        .PostJsonAsync(consolidate, cancellation.Token)
                        .ConfigureAwait(false);

                    if (response.StatusCode == (int)HttpStatusCode.OK)
                    {
                        string transactionHex = await response.GetJsonAsync<string>().ConfigureAwait(false);

                        // Ensure the response is a valid transaction so that we can return success.
                        Transaction transaction = this.chainIndexer.Network.Consensus.ConsensusFactory.CreateTransaction(transactionHex);

                        return transaction.GetHash().ToString();
                    }
                }
                catch
                {
                }

                return null;
            }
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

                using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(180)))
                {
                    response = await this.cirrusInteropSettings.CirrusClientUrl
                        .AppendPathSegment("api/smartcontracts/build-and-send-call")
                        .PostJsonAsync(request, cancellation.Token)
                        .ReceiveJson<BuildCallContractTransactionResponse>()
                        .ConfigureAwait(false);

                    if (!response.Success)
                    {
                        return (null, $"Error confirming transfer '{response.TransactionId}': Possible transaction build and call timeout.");
                    }
                }
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

        /// <inheritdoc />
        public async Task<string> GetKeyValueStoreAsync(string address, string key, ulong blockHeight)
        {
            var request = new LocalCallContractRequest
            {
                BlockHeight = blockHeight,
                Amount = "0",
                ContractAddress = this.cirrusInteropSettings.CirrusKeyValueStoreContractAddress,
                GasLimit = 250_000,
                GasPrice = 100,
                MethodName = KeyValueGetMethodName,
                Parameters = new[]
                {
                    "9#" + address,
                    "4#" + key
                },
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
                    return null;

                return (string)result.Return;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<MultisigTransactionIdentifiers> SetKeyValueStoreAsync(string key, string value)
        {
            try
            {
                byte[] methodCallData = KeyValueData(key, value);

                string methodCallDataHex = BitConverter.ToString(methodCallData).Replace("-", "");

                return await MultisigContractCallInternalAsync(this.cirrusInteropSettings.CirrusMultisigContractAddress, KeyValueSetMethodName, methodCallDataHex).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new MultisigTransactionIdentifiers
                {
                    Message = $"Exception occurred trying to build and send the KVS Set transaction: {ex}",
                    TransactionHash = "",
                    TransactionId = -1
                };
            }
        }

        private byte[] KeyValueData(string key, string value)
        {
            // Pack the parameters of the KVS Set method invocation into the format used by the multisig contract.
            byte[] keyBytes = this.serializer.Serialize(key);
            byte[] keyBytesPadded = CreatePaddedParameterArray(keyBytes, 4); // 4 = String

            byte[] valueBytes = this.serializer.Serialize(value);
            byte[] valueBytesPadded = CreatePaddedParameterArray(valueBytes, 4); // 4 = String

            return this.serializer.Serialize(new byte[][]
            {
                keyBytesPadded,
                valueBytesPadded
            });
        }

        private byte[] CreatePaddedParameterArray(byte[] paramBytes, int paramType)
        {
            byte[] paramBytesPadded = new byte[paramBytes.Length + 1];
            paramBytesPadded[0] = (byte)paramType;
            Array.Copy(paramBytes, 0, paramBytesPadded, 1, paramBytes.Length);

            return paramBytesPadded;
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
