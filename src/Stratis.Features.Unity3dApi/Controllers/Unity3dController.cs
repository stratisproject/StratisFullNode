using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.BlockStore.Controllers;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Features.Wallet.Controllers;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.CLR.Local;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.Unity3dApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class Unity3dController : Controller
    {
        private readonly IAddressIndexer addressIndexer;

        private readonly IBlockStore blockStore;

        private readonly IChainState chainState;

        private readonly Network network;

        private readonly ICoinView coinView;

        private readonly WalletController walletController;

        private readonly ChainIndexer chainIndexer;

        private readonly IStakeChain stakeChain;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        private readonly IContractPrimitiveSerializer primitiveSerializer;

        private readonly IStateRepositoryRoot stateRoot;

        private readonly IContractAssemblyCache contractAssemblyCache;

        private readonly IReceiptRepository receiptRepository;

        private readonly ISmartContractTransactionService smartContractTransactionService;

        private readonly ILocalExecutor localExecutor;

        private readonly INFTTransferIndexer NFTTransferIndexer;

        public Unity3dController(ILoggerFactory loggerFactory, IAddressIndexer addressIndexer,
            IBlockStore blockStore, IChainState chainState, Network network, ICoinView coinView, WalletController walletController, ChainIndexer chainIndexer, INFTTransferIndexer NFTTransferIndexer,
            IStakeChain stakeChain = null,
            IContractPrimitiveSerializer primitiveSerializer = null, IStateRepositoryRoot stateRoot = null, IContractAssemblyCache contractAssemblyCache = null,
            IReceiptRepository receiptRepository = null, ISmartContractTransactionService smartContractTransactionService = null, ILocalExecutor localExecutor = null)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.addressIndexer = Guard.NotNull(addressIndexer, nameof(addressIndexer));
            this.blockStore = Guard.NotNull(blockStore, nameof(blockStore));
            this.chainState = Guard.NotNull(chainState, nameof(chainState));
            this.network = Guard.NotNull(network, nameof(network));
            this.coinView = Guard.NotNull(coinView, nameof(coinView));
            this.walletController = Guard.NotNull(walletController, nameof(walletController));
            this.chainIndexer = Guard.NotNull(chainIndexer, nameof(chainIndexer));
            this.stakeChain = stakeChain;
            this.NFTTransferIndexer = NFTTransferIndexer;

            this.primitiveSerializer = primitiveSerializer;
            this.stateRoot = stateRoot;
            this.contractAssemblyCache = contractAssemblyCache;
            this.receiptRepository = receiptRepository;
            this.smartContractTransactionService = smartContractTransactionService;
            this.localExecutor = localExecutor;
        }

        /// <summary>
        /// Gets UTXOs for specified address.
        /// </summary>
        /// <returns>See <see cref="GetUTXOsResponseModel"/>.</returns>
        /// <param name="address">Address to get UTXOs for.</param>
        [Route("getutxosforaddress")]
        [HttpGet]
        public GetUTXOsResponseModel GetUTXOsForAddress([FromQuery] string address)
        {
            VerboseAddressBalancesResult balancesResult = this.addressIndexer.GetAddressIndexerState(new[] {address});

            if (balancesResult.BalancesData == null || balancesResult.BalancesData.Count != 1)
            {
                this.logger.LogWarning("No balances found for address {0}, Reason: {1}", address, balancesResult.Reason);
                return new GetUTXOsResponseModel() {Reason = balancesResult.Reason};
            }

            BitcoinAddress bitcoinAddress = this.network.CreateBitcoinAddress(address);

            AddressIndexerData addressBalances = balancesResult.BalancesData.First();

            List<AddressBalanceChange> deposits = addressBalances.BalanceChanges.Where(x => x.Deposited).ToList();
            long totalDeposited = deposits.Sum(x => x.Satoshi);
            long totalWithdrawn = addressBalances.BalanceChanges.Where(x => !x.Deposited).Sum(x => x.Satoshi);

            long balanceSat = totalDeposited - totalWithdrawn;
            
            List<int> heights = deposits.Select(x => x.BalanceChangedHeight).Distinct().ToList();
            HashSet<uint256> blocksToRequest = new HashSet<uint256>(heights.Count);
            
            foreach (int height in heights)
            {
                uint256 blockHash = this.chainState.ConsensusTip.GetAncestor(height).Header.GetHash();
                blocksToRequest.Add(blockHash);
            }
            
            List<Block> blocks = this.blockStore.GetBlocks(blocksToRequest.ToList());
            List<OutPoint> collectedOutPoints = new List<OutPoint>(deposits.Count);

            foreach (List<Transaction> txList in blocks.Select(x => x.Transactions))
            {
                foreach (Transaction transaction in txList.Where(x => !x.IsCoinBase && !x.IsCoinStake))
                {
                    for (int i = 0; i < transaction.Outputs.Count; i++)
                    {
                        if (!transaction.Outputs[i].IsTo(bitcoinAddress))
                            continue;

                        collectedOutPoints.Add(new OutPoint(transaction, i));
                    }
                }
            }

            FetchCoinsResponse fetchCoinsResponse = this.coinView.FetchCoins(collectedOutPoints.ToArray());

            GetUTXOsResponseModel response = new GetUTXOsResponseModel()
            {
                BalanceSat = balanceSat,
                UTXOs = new List<UTXOModel>()
            };

            foreach (KeyValuePair<OutPoint, UnspentOutput> unspentOutput in fetchCoinsResponse.UnspentOutputs)
            {
                if (unspentOutput.Value.Coins == null)
                    continue; // spent

                OutPoint outPoint = unspentOutput.Key;
                Money value = unspentOutput.Value.Coins.TxOut.Value;

                response.UTXOs.Add(new UTXOModel(outPoint, value));
            }

            return response;
        }

        /// <summary>Provides balance of the given address confirmed with at least 1 confirmation.</summary>
        /// <param name="address">Address that will be queried.</param>
        /// <returns>A result object containing the balance for each requested address and if so, a message stating why the indexer is not queryable.</returns>
        /// <response code="200">Returns balances for the requested addresses</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("getaddressbalance")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetAddressBalance(string address)
        {
            long money = -1;
            try
            {
                AddressBalancesResult result = this.addressIndexer.GetAddressBalances(new[] { address }, 1);
                money = result.Balances.First().Balance.Satoshi;
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
            }

            if (this.Request.Headers["Accept"] == "application/json")
            {
                GetBalanceResponseModel response = new GetBalanceResponseModel()
                {
                    Balance = money
                };
                return Ok(response);
            }
            else
            {
                return Ok(money);
            }
        }

        /// <summary>
        /// Gets the block header of a block identified by a block hash.
        /// </summary>
        /// <param name="hash">The hash of the block to retrieve.</param>
        /// <returns>Json formatted <see cref="BlockHeaderModel"/>. <c>null</c> if block not found. Returns <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/> formatted error if fails.</returns>
        /// <exception cref="NotImplementedException">Thrown if isJsonFormat = false</exception>"
        /// <exception cref="ArgumentException">Thrown if hash is empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if logger is not provided.</exception>
        /// <remarks>Binary serialization is not supported with this method.</remarks>
        [Route("getblockheader")]
        [HttpGet]
        public BlockHeaderModel GetBlockHeader([FromQuery] string hash)
        {
            try
            {
                Guard.NotEmpty(hash, nameof(hash));

                this.logger.LogDebug("GetBlockHeader {0}", hash);

                BlockHeaderModel model = null;
                BlockHeader blockHeader = this.chainIndexer?.GetHeader(uint256.Parse(hash))?.Header;
                if (blockHeader != null)
                {
                    model = new BlockHeaderModel(blockHeader);
                }

                return model;
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return null;
            }
        }

        /// <summary>
        /// Gets a raw transaction that is present on this full node.
        /// This method gets transaction using block store.
        /// </summary>
        /// <param name="trxid">The transaction ID (a hash of the transaction).</param>
        /// <returns>Json formatted <see cref="TransactionBriefModel"/> or <see cref="TransactionVerboseModel"/>. <c>null</c> if transaction not found. Returns <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/> formatted error if otherwise fails.</returns>
        /// <exception cref="ArgumentNullException">Thrown if fullNode, network, or chain are not available.</exception>
        /// <exception cref="ArgumentException">Thrown if trxid is empty or not a valid<see cref="uint256"/>.</exception>
        /// <remarks>Requires txindex=1, otherwise only txes that spend or create UTXOs for a wallet can be returned.</remarks>
        [Route("getrawtransaction")]
        [HttpGet]
        public RawTxModel GetRawTransaction([FromQuery] string trxid)
        {
            try
            {
                Guard.NotEmpty(trxid, nameof(trxid));

                uint256 txid;
                if (!uint256.TryParse(trxid, out txid))
                {
                    throw new ArgumentException(nameof(trxid));
                }

                Transaction trx = this.blockStore?.GetTransactionById(txid);
                
                if (trx == null)
                {
                    return null;
                }
                
                return new RawTxModel() { Hex = trx.ToHex() };
                
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return null;
            }
        }
        
        /// <summary>
        /// Sends a transaction that has already been built.
        /// Use the /api/Wallet/build-transaction call to create transactions.
        /// </summary>
        /// <param name="request">An object containing the necessary parameters used to a send transaction request.</param>
        /// <param name="cancellationToken">The Cancellation Token</param>
        /// <returns>A JSON object containing information about the sent transaction.</returns>
        /// <response code="200">Returns transaction details</response>
        /// <response code="400">Invalid request, cannot broadcast transaction, or unexpected exception occurred</response>
        /// <response code="403">No connected peers</response>
        /// <response code="500">Request is null</response>
        [Route("send-transaction")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.Forbidden)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> SendTransaction([FromBody] SendTransactionRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await this.walletController.SendTransaction(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates a bech32 or base58 bitcoin address.
        /// </summary>
        /// <param name="address">A Bitcoin address to validate in a string format.</param>
        /// <returns>Json formatted <see cref="ValidatedAddress"/> containing a boolean indicating address validity. Returns <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/> formatted error if fails.</returns>
        /// <exception cref="ArgumentException">Thrown if address provided is empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if network is not provided.</exception>
        [Route("validateaddress")]
        [HttpGet]
        public ValidatedAddress ValidateAddress([FromQuery] string address)
        {
            Guard.NotEmpty(address, nameof(address));

            var result = new ValidatedAddress
            {
                IsValid = false,
                Address = address,
            };

            try
            {
                // P2WPKH
                if (BitcoinWitPubKeyAddress.IsValid(address, this.network, out Exception _))
                {
                    result.IsValid = true;
                }
                // P2WSH
                else if (BitcoinWitScriptAddress.IsValid(address, this.network, out Exception _))
                {
                    result.IsValid = true;
                }
                // P2PKH
                else if (BitcoinPubKeyAddress.IsValid(address, this.network))
                {
                    result.IsValid = true;
                }
                // P2SH
                else if (BitcoinScriptAddress.IsValid(address, this.network))
                {
                    result.IsValid = true;
                    result.IsScript = true;
                }
            }
            catch (Exception e)
            {
                this.logger.LogDebug("Exception occurred: {0}", e.ToString());

                result.IsValid = false;
                return result;
            }

            if (result.IsValid)
            {
                var scriptPubKey = BitcoinAddress.Create(address, this.network).ScriptPubKey;
                result.ScriptPubKey = scriptPubKey.ToHex();
                result.IsWitness = scriptPubKey.IsWitness(this.network);
            }

            return result;
        }


        /// <summary>
        /// Retrieves the block which matches the supplied block hash.
        /// </summary>
        /// <param name="query">An object containing the necessary parameters to search for a block.</param>
        /// <returns><see cref="BlockModel"/> if block is found, <see cref="NotFoundObjectResult"/> if not found. Returns <see cref="IActionResult"/> with error information if exception thrown.</returns>
        /// <response code="200">Returns data about the block or block not found message</response>
        /// <response code="400">Block hash invalid, or an unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetBlock)]
        [HttpGet]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        public BlockModel GetBlock([FromQuery] SearchByHashRequest query)
        {
            if (!this.ModelState.IsValid)
                return null;

            try
            {
                uint256 blockId = uint256.Parse(query.Hash);

                ChainedHeader chainedHeader = this.chainIndexer.GetHeader(blockId);

                if (chainedHeader == null)
                    return null;

                Block block = chainedHeader.Block ?? this.blockStore.GetBlock(blockId);

                // In rare occasions a block that is found in the
                // indexer may not have been pushed to the store yet. 
                if (block == null)
                    return null;

                BlockModel blockModel = query.ShowTransactionDetails
                    ? new BlockTransactionDetailsModel(block, chainedHeader, this.chainIndexer.Tip, this.network)
                    : new BlockModel(block, chainedHeader, this.chainIndexer.Tip, this.network);

                if (this.network.Consensus.IsProofOfStake)
                {
                    var posBlock = block as PosBlock;

                    blockModel.PosBlockSignature = posBlock.BlockSignature.ToHex(this.network);
                    blockModel.PosBlockTrust = new Target(chainedHeader.GetBlockTarget()).ToUInt256().ToString();
                    blockModel.PosChainTrust = chainedHeader.ChainWork.ToString(); // this should be similar to ChainWork

                    if (this.stakeChain != null)
                    {
                        BlockStake blockStake = this.stakeChain.Get(blockId);

                        blockModel.PosModifierv2 = blockStake?.StakeModifierV2.ToString();
                        blockModel.PosFlags = blockStake?.Flags == BlockFlag.BLOCK_PROOF_OF_STAKE ? "proof-of-stake" : "proof-of-work";
                        blockModel.PosHashProof = blockStake?.HashProof?.ToString();
                    }
                }

                return blockModel;
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return null;
            }
        }

        /// <summary>
        /// Retrieves the <see cref="addressIndexer"/>'s tip. 
        /// </summary>
        /// <returns>An instance of <see cref="TipModel"/> containing the tip's hash and height.</returns>
        /// <response code="200">Returns the address indexer tip</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("tip")]
        [HttpGet]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        public TipModel GetTip()
        {
            try
            {
                ChainedHeader addressIndexerTip = this.addressIndexer.IndexerTip;

                if (addressIndexerTip == null)
                    return null;

                return new TipModel() { TipHash = addressIndexerTip.HashBlock.ToString(), TipHeight = addressIndexerTip.Height };
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return null;
            }
        }
        
        /// <summary>
        /// Gets a smart contract transaction receipt. Receipts contain information about how a smart contract transaction was executed.
        /// This includes the value returned from a smart contract call and how much gas was used.  
        /// </summary> 
        /// <param name="txHash">A hash of the smart contract transaction (the transaction ID).</param> 
        /// <returns>The receipt for the smart contract.</returns> 
        /// <response code="200">Returns transaction receipt</response>
        /// <response code="400">Transaction not found</response>
        [Route("receipt")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public ReceiptResponse GetReceiptAPI([FromQuery] string txHash)
        {
            ReceiptResponse receiptResponse;
            uint256 txHashNum = new uint256(txHash);
            Receipt receipt = this.receiptRepository.Retrieve(txHashNum);

            if (receipt == null)
                return null;

            uint160 address = receipt.NewContractAddress ?? receipt.To;

            if (!receipt.Logs.Any())
            {
                receiptResponse = new ReceiptResponse(receipt, new List<LogResponse>(), this.network);
            }
            else
            {
                var deserializer = new ApiLogDeserializer(this.primitiveSerializer, this.network, this.stateRoot, this.contractAssemblyCache);
                List<LogResponse> logResponses = deserializer.MapLogResponses(receipt.Logs);

                receiptResponse = new ReceiptResponse(receipt, logResponses, this.network);
            }
            
            return receiptResponse;
        }
        
        /// <summary>
        /// Makes a local call to a method on a smart contract that has been successfully deployed. A transaction 
        /// is not created as the call is never propagated across the network. All persistent data held by the   
        /// smart contract is copied before the call is made. Only this copy is altered by the call
        /// and the actual data is unaffected. Even if an amount of funds are specified to send with the call,
        /// no funds are in fact sent.
        /// The purpose of this function is to query and test methods. 
        /// </summary> 
        /// <param name="request">An object containing the necessary parameters to build the transaction.</param> 
        /// <results>The result of the local call to the smart contract method.</results>
        /// <response code="200">Returns call response</response>
        /// <response code="400">Invalid request</response>
        /// <response code="500">Unable to deserialize method parameters</response>
        [Route("local-call")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult LocalCallSmartContractTransaction([FromBody] LocalCallContractRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            // Rewrite the method name to a property name
            this.RewritePropertyGetterName(request);

            try
            {
                ContractTxData txData = this.smartContractTransactionService.BuildLocalCallTxData(request);

                ulong height = request.BlockHeight.HasValue ? request.BlockHeight.Value : (ulong)this.chainIndexer.Height;

                ILocalExecutionResult result = this.localExecutor.Execute(
                    height,
                    request.Sender?.ToUint160(this.network) ?? new uint160(),
                    string.IsNullOrWhiteSpace(request.Amount) ? (Money)request.Amount : 0,
                    txData);

                return this.Json(result, new JsonSerializerSettings
                {
                    ContractResolver = new ContractParametersContractResolver(this.network)
                });
            }
            catch (MethodParameterStringSerializerException e)
            {
                return this.Json(ErrorHelpers.BuildErrorResponse(HttpStatusCode.InternalServerError, e.Message,
                    "Error deserializing method parameters"));
            }
        }

        /// <summary>
        /// Searches a smart contract's receipts for those which match a specific event. The SmartContract.Log() function
        /// is capable of storing C# structs, and structs are used to store information about different events occurring 
        /// on the smart contract. For example, a "TransferLog" struct could contain "From" and "To" fields and be used to log
        /// when a smart contract makes a transfer of funds from one wallet to another. The log entries are held inside the smart contract,
        /// indexed using the name of the struct, and are linked to individual transaction receipts.
        /// Therefore, it is possible to return a smart contract's transaction receipts
        /// which match a specific event (as defined by the struct name).  
        /// </summary>
        /// 
        /// <param name="contractAddress">The contract address from which events were raised.</param>
        /// <param name="eventName">The name of the event raised.</param>
        /// <param name="topics">The topics to search. All specified topics must be present.</param>
        /// <param name="fromBlock">The block number from which to start searching.</param>
        /// <param name="toBlock">The block number where searching finishes.</param>
        /// 
        /// <returns>A list of receipts for transactions relating to a specific smart contract and a specific event in that smart contract.</returns>
        [Route("receipt-search")]
        [HttpGet]
        public Task<List<ReceiptResponse>> ReceiptSearchAPI([FromQuery] string contractAddress, [FromQuery] string eventName, [FromQuery] List<string> topics = null, [FromQuery] int fromBlock = 0, [FromQuery] int? toBlock = null)
        {
            List<ReceiptResponse> result = this.smartContractTransactionService.ReceiptSearch(contractAddress, eventName, topics, fromBlock, toBlock);

            return Task.FromResult(result);
        }
        
        [Route("watch-nft-contract")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public void WatchNFTContract([FromQuery] string contractAddress)
        {
            this.NFTTransferIndexer.WatchNFTContract(contractAddress);
        }
        
        [Route("get-watched-nft-contracts")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public List<string> GetWatchedNFTContracts()
        {
            return this.NFTTransferIndexer.GetWatchedNFTContracts();
        }
        
        [Route("get-owned-nfts")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public OwnedNFTsModel GetOwnedNFTs([FromQuery] string ownerAddress)
        {
            return this.NFTTransferIndexer.GetOwnedNFTs(ownerAddress);
        }
        
        [Route("get-all-nft-owners-by-contract-address")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public NFTContractModel GetAllNFTOwnersByContractAddress([FromQuery] string contractAddress)
        {
            return this.NFTTransferIndexer.GetAllNFTOwnersByContractAddress(contractAddress);
        }

        /// <summary>
        /// If the call is to a property, rewrites the method name to the getter method's name.
        /// </summary>
        private void RewritePropertyGetterName(LocalCallContractRequest request)
        {
            // Don't rewrite if there are params
            if (request.Parameters != null && request.Parameters.Any())
                return;

            byte[] contractCode = this.stateRoot.GetCode(request.ContractAddress.ToUint160(this.network));

            string contractType = this.stateRoot.GetContractType(request.ContractAddress.ToUint160(this.network));

            Result<IContractModuleDefinition> readResult = ContractDecompiler.GetModuleDefinition(contractCode);

            if (readResult.IsSuccess)
            {
                IContractModuleDefinition contractModule = readResult.Value;
                string propertyGetterName = contractModule.GetPropertyGetterMethodName(contractType, request.MethodName);

                if (propertyGetterName != null)
                    request.MethodName = propertyGetterName;
            }
        }
    }
}
