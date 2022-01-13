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
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using FileMode = LiteDB.FileMode;

namespace Stratis.Features.Unity3dApi
{
    public interface INFTTransferIndexer : IDisposable
    {
        /// <summary>Initialized NFT indexer.</summary>
        void Initialize();

        /// <summary>Adds NFT contract to watch list. Only contracts from the watch list are being indexed.</summary>
        void WatchNFTContract(string contractAddress);

        /// <summary>Provides a list of all nft contract addresses that are being tracked.</summary>
        List<string> GetWatchedNFTContracts();

        /// <summary>Provides collection of NFT ids that belong to provided user's address for watched contracts.</summary>
        OwnedNFTsModel GetOwnedNFTs(string address);

        /// <summary>Returns collection of all users that own nft.</summary>
        NFTContractModel GetAllNFTOwnersByContractAddress(string contractAddress);
    }

    /// <summary>This component maps addresses to NFT Ids they own.</summary>
    public class NFTTransferIndexer : INFTTransferIndexer
    {
        public ChainedHeader IndexerTip { get; private set; }

        private const string DatabaseFilename = "NFTTransferIndexer.litedb";
        private const string DbOwnedNFTsKey = "OwnedNfts";
        private const int SyncBufferBlocks = 50;
        
        private readonly DataFolder dataFolder;
        private readonly ILogger logger;
        private readonly ChainIndexer chainIndexer;
        private readonly IAsyncProvider asyncProvider;
        private readonly ISmartContractTransactionService smartContractTransactionService;

        private LiteDatabase db;
        private LiteCollection<NFTContractModel> NFTContractCollection;
        private CancellationTokenSource cancellation;
        private Task indexingTask;
        
        public NFTTransferIndexer(DataFolder dataFolder, ILoggerFactory loggerFactory, IAsyncProvider asyncProvider, ChainIndexer chainIndexer, ISmartContractTransactionService smartContractTransactionService = null)
        {
            this.dataFolder = dataFolder;
            this.cancellation = new CancellationTokenSource();
            this.asyncProvider = asyncProvider;
            this.chainIndexer = chainIndexer;
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

            this.indexingTask = Task.Run(async () => await this.IndexNFTsContinuouslyAsync().ConfigureAwait(false));
            this.asyncProvider.RegisterTask($"{nameof(AddressIndexer)}.{nameof(this.indexingTask)}", this.indexingTask);
        }

        /// <inheritdoc />
        public void WatchNFTContract(string contractAddress)
        {
            if (!this.NFTContractCollection.Exists(x => x.ContractAddress == contractAddress))
            {
                NFTContractModel model = new NFTContractModel()
                {
                    ContractAddress = contractAddress,
                    LastUpdatedBlock = 0,
                    OwnedIDsByAddress = new Dictionary<string, List<long>>()
                };

                this.NFTContractCollection.Upsert(model);
            }
        }

        /// <inheritdoc />
        public List<string> GetWatchedNFTContracts()
        {
            return this.NFTContractCollection.FindAll().Select(x => x.ContractAddress).ToList();
        }

        /// <inheritdoc />
        public OwnedNFTsModel GetOwnedNFTs(string address)
        {
            List<NFTContractModel> NFTContractModels = this.NFTContractCollection.FindAll().Where(x => x.OwnedIDsByAddress.ContainsKey(address)).ToList();

            OwnedNFTsModel output = new OwnedNFTsModel() { OwnedIDsByContractAddress = new Dictionary<string, List<long>>() };

            foreach (NFTContractModel contractModel in NFTContractModels)
            {
                List<long> ids = contractModel.OwnedIDsByAddress[address];
                output.OwnedIDsByContractAddress.Add(contractModel.ContractAddress, ids);
            }

            return output;
        }

        public NFTContractModel GetAllNFTOwnersByContractAddress(string contractAddress)
        {
            NFTContractModel currentContract = this.NFTContractCollection.FindOne(x => x.ContractAddress == contractAddress);
            return currentContract;
        }

        private async Task IndexNFTsContinuouslyAsync()
        {
            await Task.Delay(1);

            try
            {
                while (!this.cancellation.Token.IsCancellationRequested)
                {
                    List<string> contracts = this.NFTContractCollection.FindAll().Select(x => x.ContractAddress).ToList();

                    foreach (string contractAddr in contracts)
                    {
                        if (this.cancellation.Token.IsCancellationRequested)
                            break;

                        NFTContractModel currentContract = this.NFTContractCollection.FindOne(x => x.ContractAddress == contractAddr);

                        ChainedHeader chainTip = this.chainIndexer.Tip;

                        List<ReceiptResponse> receipts = this.smartContractTransactionService.ReceiptSearch(
                            contractAddr, "TransferLog", null, currentContract.LastUpdatedBlock + 1, null);

                        if ((receipts == null) || (receipts.Count == 0))
                            continue;

                        int lastReceiptHeight = 0;
                        if (receipts.Any())
                            lastReceiptHeight = (int)receipts.Last().BlockNumber.Value;

                        currentContract.LastUpdatedBlock = new List<int>() { chainTip.Height, lastReceiptHeight }.Max();

                        List<TransferLog> transferLogs = new List<TransferLog>(receipts.Count);

                        foreach (ReceiptResponse receiptRes in receipts)
                        {
                            LogData log = receiptRes.Logs.First().Log;
                            string jsonLog = JsonConvert.SerializeObject(log);

                            TransferLogRoot infoObj = JsonConvert.DeserializeObject<TransferLogRoot>(jsonLog);
                            transferLogs.Add(infoObj.Data);
                        }
                    
                        foreach (TransferLog transferInfo in transferLogs)
                        {
                            if ((transferInfo.From != null) && currentContract.OwnedIDsByAddress.ContainsKey(transferInfo.From))
                            {
                                currentContract.OwnedIDsByAddress[transferInfo.From].Remove(transferInfo.TokenId);

                                if (currentContract.OwnedIDsByAddress[transferInfo.From].Count == 0)
                                    currentContract.OwnedIDsByAddress.Remove(transferInfo.From);
                            }

                            if (!currentContract.OwnedIDsByAddress.ContainsKey(transferInfo.To))
                                currentContract.OwnedIDsByAddress.Add(transferInfo.To, new List<long>());

                            currentContract.OwnedIDsByAddress[transferInfo.To].Add(transferInfo.TokenId);
                        }

                        this.NFTContractCollection.Upsert(currentContract);
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(6), this.cancellation.Token);
                    }
                    catch (TaskCanceledException)
                    {
                    }
                }
            }
            catch (Exception e)
            {
                this.logger.LogError(e.ToString());
            }
        }

        public void Dispose()
        {
            this.cancellation.Cancel();
            this.indexingTask?.GetAwaiter().GetResult();
            this.db?.Dispose();
        }
    }

    public class NFTContractModel
    {
        public int Id { get; set; }

        public string ContractAddress { get; set; }

        // Key is nft owner address, value is list of NFT IDs
        public Dictionary<string, List<long>> OwnedIDsByAddress { get; set; }

        public int LastUpdatedBlock { get; set; }
    }

    public class OwnedNFTsModel
    {
        public Dictionary<string, List<long>> OwnedIDsByContractAddress { get; set; }
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
