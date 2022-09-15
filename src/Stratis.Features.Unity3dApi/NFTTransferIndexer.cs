using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.CLR;
using FileMode = LiteDB.FileMode;

namespace Stratis.Features.Unity3dApi
{
    public interface INFTTransferIndexer : IDisposable
    {
        /// <summary>Initialized NFT indexer.</summary>
        void Initialize();

        /// <summary>Provides a list of all nft contract addresses that are being tracked.</summary>
        List<string> GetWatchedNFTContracts();

        /// <summary>Provides collection of NFT ids that belong to provided user's address for watched contracts.</summary>
        OwnedNFTsModel GetOwnedNFTs(string address);

        /// <summary>Returns collection of all users that own nft.</summary>
        NFTContractModel GetAllNFTOwnersByContractAddress(string contractAddress);

        /// <summary>Reindexes all tracked contracts.</summary>
        void ReindexAllContracts();

        /// <summary>Retrieves all indexed data.</summary>
        public List<NFTContractModel> GetEntireState();
    }

    /// <summary>This component maps addresses to NFT Ids they own.</summary>
    public class NFTTransferIndexer : INFTTransferIndexer
    {
        private const string NftTransferEventName = "TransferLog";
        private const string DatabaseFilename = "NFTTransferIndexer.litedb";
        private const string DbOwnedNFTsKey = "OwnedNfts";
        private const string IndexerStateKey = "IndexerState";

        private readonly DataFolder dataFolder;
        private readonly ILogger logger;
        private readonly ChainIndexer chainIndexer;
        private readonly IAsyncProvider asyncProvider;
        private readonly INodeLifetime nodeLifetime;
        private readonly ISmartContractTransactionService smartContractTransactionService;
        private readonly Network network;
        private readonly NftContractLocalClient nftContractLocalClient;

        private LiteDatabase db;
        private LiteCollection<NFTContractModel> NFTContractCollection;
        private LiteCollection<IndexerStateModel> indexerState;
        private HashSet<string> knownContracts;
        private CancellationTokenSource cancellation;
        private IAsyncLoop indexingLoop;

        public NFTTransferIndexer(DataFolder dataFolder, ILoggerFactory loggerFactory, IAsyncProvider asyncProvider, INodeLifetime nodeLifetime,
            ChainIndexer chainIndexer, Network network, ILocalExecutor localExecutor, Unity3dApiSettings apiSettings, ISmartContractTransactionService smartContractTransactionService = null)
        {
            this.network = network;
            this.dataFolder = dataFolder;
            this.cancellation = new CancellationTokenSource();
            this.asyncProvider = asyncProvider;
            this.nodeLifetime = nodeLifetime;
            this.chainIndexer = chainIndexer;

            var localCallContract = new LocalCallContract(network, smartContractTransactionService, chainIndexer, localExecutor);

            this.nftContractLocalClient = new NftContractLocalClient(localCallContract, apiSettings.LocalCallSenderAddress);
            this.smartContractTransactionService = smartContractTransactionService;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public void Initialize()
        {
            if (this.db != null)
                throw new Exception("NFTTransferIndexer already initialized!");

            string dbPath = Path.Combine(this.dataFolder.RootPath, DatabaseFilename);

            FileMode fileMode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? FileMode.Exclusive : FileMode.Shared;
            this.db = new LiteDatabase(new ConnectionString() { Filename = dbPath, Mode = fileMode });
            this.NFTContractCollection = this.db.GetCollection<NFTContractModel>(DbOwnedNFTsKey);

            this.indexerState = this.db.GetCollection<IndexerStateModel>(IndexerStateKey);

            if (this.indexerState.Count() == 0)
            {
                this.logger.LogInformation("NFT indexer state not found, restarting index.");

                // Will automatically add the state model once it is finished.
                this.ReindexAllContracts();
            }

            this.logger.LogInformation("Building cache of known contract addresses.");

            this.knownContracts = new HashSet<string>();

            foreach (NFTContractModel model in this.NFTContractCollection.FindAll())
            {
                this.knownContracts.Add(model.ContractAddress);
            }

            this.logger.LogInformation("Finished building cache of known contract addresses.");

            this.indexingLoop = this.asyncProvider.CreateAndRunAsyncLoop(nameof(IndexNFTsContinuouslyAsync), async (cancellationTokenSource) =>
                {
                    try
                    {
                        await this.IndexNFTsContinuouslyAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        this.logger.LogWarning("Exception raised while indexing NFTs. {0}", e);
                    }
                },
                this.nodeLifetime.ApplicationStopping,
                repeatEvery: TimeSpans.TenSeconds,
                startAfter: TimeSpans.Second);

            this.logger.LogDebug("NFTTransferIndexer initialized.");
        }

        private int GetWatchFromHeight()
        {
            int watchFromHeight = this.network.IsTest() ? 3000000 : 3400000;

            return watchFromHeight;
        }

        private IndexerStateModel GetIndexerState()
        {
            IndexerStateModel indexerStateModel = this.indexerState.FindAll().FirstOrDefault();

            if (indexerStateModel == null)
            {
                indexerStateModel = new IndexerStateModel() { LastProcessedHeight = GetWatchFromHeight() };
            }

            return indexerStateModel;
        }

        private void UpdateLastUpdatedBlock(int blockHeight)
        {
            IndexerStateModel indexerStateModel = this.indexerState.FindAll().FirstOrDefault();

            if (indexerStateModel == null)
            {
                indexerStateModel = new IndexerStateModel();
            }

            indexerStateModel.LastProcessedHeight = blockHeight;

            this.indexerState.Upsert(indexerStateModel);
        }

        /// <inheritdoc />
        public List<string> GetWatchedNFTContracts()
        {
            return this.NFTContractCollection.FindAll().Select(x => x.ContractAddress).ToList();
        }

        /// <inheritdoc />
        public OwnedNFTsModel GetOwnedNFTs(string address)
        {
            this.logger.LogDebug("Retrieving owned nfts for address " + address);

            List<NFTContractModel> NFTContractModels = this.NFTContractCollection.FindAll().Where(x => x.OwnedIDsByAddress.ContainsKey(address)).ToList();

            var output = new OwnedNFTsModel() { OwnedIDsByContractAddress = new Dictionary<string, List<long>>() };

            foreach (NFTContractModel contractModel in NFTContractModels)
            {
                HashSet<long> ids = contractModel.OwnedIDsByAddress[address];
                output.OwnedIDsByContractAddress.Add(contractModel.ContractAddress, ids.ToList());
            }

            return output;
        }

        public NFTContractModel GetAllNFTOwnersByContractAddress(string contractAddress)
        {
            this.logger.LogDebug("Retrieving all owned nfts by contract address " + contractAddress);

            NFTContractModel currentContract = this.NFTContractCollection.FindOne(x => x.ContractAddress == contractAddress);
            return currentContract;
        }

        /// <inheritdoc />
        public void ReindexAllContracts()
        {
            var updated = new List<NFTContractModel>();

            foreach (NFTContractModel contractModel in this.NFTContractCollection.FindAll().ToList())
            {
                contractModel.OwnedIDsByAddress = new Dictionary<string, HashSet<long>>();

                updated.Add(contractModel);
            }

            this.NFTContractCollection.Upsert(updated);

            this.UpdateLastUpdatedBlock(GetWatchFromHeight());

            this.logger.LogInformation($"A re-index of all contracts will be triggered from block {GetWatchFromHeight()}.");
        }

        /// <inheritdoc />
        public List<NFTContractModel> GetEntireState()
        {
            List<NFTContractModel> state = this.NFTContractCollection.FindAll().ToList();

            return state;
        }

        private async Task IndexNFTsContinuouslyAsync()
        {
            IndexerStateModel currentIndexerState = GetIndexerState();

            if (this.chainIndexer.Tip.Height < GetWatchFromHeight())
            {
                await Task.Delay(5000);
                return;
            }

            ChainedHeader chainTip = this.chainIndexer.Tip;

            if (chainTip.Height == currentIndexerState.LastProcessedHeight)
            {
                this.logger.LogInformation("No need to update, already up to tip.");
                return;
            }

            // Return TransferLog receipts for any contract (we won't know definitively if they're NFT contracts until we check the cache or query the contract's supported interfaces).
            this.logger.LogInformation($"Initiating receipt search from block {currentIndexerState.LastProcessedHeight + 1} to {chainTip.Height}.");
            List<ReceiptResponse> receipts = this.smartContractTransactionService.ReceiptSearch((List<string>)null, NftTransferEventName, null, currentIndexerState.LastProcessedHeight + 1, chainTip.Height);

            if ((receipts == null) || (receipts.Count == 0))
            {
                this.logger.LogInformation($"No receipts found, updated to height {chainTip.Height}.");
                this.UpdateLastUpdatedBlock(chainTip.Height);

                return;
            }

            int processedCount = 0;

            var changedContracts = new HashSet<NFTContractModel>();

            this.logger.LogInformation($"{receipts.Count} receipts found for indexing.");

            var app = receipts.SelectMany(r => r.Logs).Where(f => f.Address == "tMfqmeRReLQ1FpYUi7X2pL4bLjui21H64p").ToList();

            foreach (ReceiptResponse receiptRes in receipts)
            {
                foreach (LogResponse logResponse in receiptRes.Logs)
                {
                    if (logResponse.Log.Event != NftTransferEventName)
                        continue;

                    // Now check if this is an NFT contract. As this is more expensive than retrieving the receipts we check it second.
                    if (!this.knownContracts.Contains(logResponse.Address))
                    {
                        if (!this.nftContractLocalClient.SupportsInterface((ulong)chainTip.Height, logResponse.Address, TokenInterface.INonFungibleToken))
                        {
                            this.logger.LogTrace("Found TransferLog for non-NFT contract: " + logResponse.Address);

                            break;
                        }

                        this.logger.LogInformation($"Found new NFT contract '{logResponse.Address}'");

                        this.knownContracts.Add(logResponse.Address);

                        this.NFTContractCollection.Insert(new NFTContractModel
                        {
                            ContractAddress = logResponse.Address,
                            OwnedIDsByAddress = new Dictionary<string, HashSet<long>>()
                        });
                    }

                    string jsonLog = JsonConvert.SerializeObject(logResponse.Log);

                    TransferLogRoot infoObj = JsonConvert.DeserializeObject<TransferLogRoot>(jsonLog);

                    TransferLog transferInfo = infoObj.Data;

                    this.logger.LogDebug("Log from: {0}, to: {1}, ID: {2}", transferInfo.From, transferInfo.To, transferInfo.TokenId);

                    // Check if the contract already had modifications and if so, use that one.
                    NFTContractModel currentContract = changedContracts.FirstOrDefault(c => c.ContractAddress == logResponse.Address);
                    if (currentContract == null)
                        currentContract = this.NFTContractCollection.FindOne(c => c.ContractAddress == logResponse.Address);

                    if ((transferInfo.From != null) && currentContract.OwnedIDsByAddress.ContainsKey(transferInfo.From))
                    {
                        bool fromExists = currentContract.OwnedIDsByAddress.ContainsKey(transferInfo.From);

                        this.logger.LogDebug("FromExists: {0} ", fromExists);

                        currentContract.OwnedIDsByAddress[transferInfo.From].Remove(transferInfo.TokenId);

                        if (currentContract.OwnedIDsByAddress[transferInfo.From].Count == 0)
                            currentContract.OwnedIDsByAddress.Remove(transferInfo.From);
                    }

                    if (!currentContract.OwnedIDsByAddress.ContainsKey(transferInfo.To))
                    {
                        this.logger.LogDebug("Added ID to To");
                        currentContract.OwnedIDsByAddress.Add(transferInfo.To, new HashSet<long>());
                    }
                    else
                        this.logger.LogDebug("Already added!");

                    currentContract.OwnedIDsByAddress[transferInfo.To].Add(transferInfo.TokenId);

                    processedCount++;

                    changedContracts.Add(currentContract);
                }
            }

            this.NFTContractCollection.Upsert(changedContracts);

            this.UpdateLastUpdatedBlock(chainTip.Height);

            this.logger.LogInformation("Found " + processedCount + " transfer logs. Last updated block: " + chainTip.Height);
        }

        public void Dispose()
        {
            this.logger.LogDebug("Dispose()");

            this.cancellation.Cancel();
            this.indexingLoop?.Dispose();
            this.db?.Dispose();

            this.logger.LogDebug("Dispose(-)");
        }
    }

    public class NFTContractModel
    {
        public int Id { get; set; }

        public string ContractAddress { get; set; }

        // Key is nft owner address, value is list of NFT IDs
        public Dictionary<string, HashSet<long>> OwnedIDsByAddress { get; set; }
    }

    public class OwnedNFTsModel
    {
        public Dictionary<string, List<long>> OwnedIDsByContractAddress { get; set; }
    }

    public class IndexerStateModel
    {
        public int Id { get; set; }

        public int LastProcessedHeight { get; set; }
    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "10.4.3.0 (Newtonsoft.Json v11.0.0.0)")]
    public partial class TransferLogRoot
    {
        public string Event { get; set; }
        public TransferLog Data { get; set; }
    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "10.4.3.0 (Newtonsoft.Json v11.0.0.0)")]
    public partial class TransferLog
    {
        [JsonProperty("From", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string From { get; set; }

        [JsonProperty("To", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string To { get; set; }

        [JsonProperty("TokenId", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public long TokenId { get; set; }
    }
}
