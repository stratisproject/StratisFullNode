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
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
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
    }

    /// <summary>This component maps addresses to NFT Ids they own.</summary>
    public class NFTTransferIndexer : INFTTransferIndexer
    {
        public ChainedHeader IndexerTip { get; private set; }

        private const string DatabaseFilename = "NFTTransferIndexer.litedb";
        private const string DbOwnedNFTsKey = "OwnedNfts";

        /// <summary>Protects write access to <see cref="NFTContractCollection"/>.</summary>
        private readonly object lockObject;
        private readonly DataFolder dataFolder;
        private readonly ILogger logger;
        private readonly ChainIndexer chainIndexer;
        private readonly IAsyncProvider asyncProvider;

        private LiteDatabase db;
        private LiteCollection<NFTContractModel> NFTContractCollection;
        private CancellationTokenSource cancellation;
        private Task indexingTask;
        
        public NFTTransferIndexer(DataFolder dataFolder, ILoggerFactory loggerFactory, ChainIndexer chainIndexer, IAsyncProvider asyncProvider)
        {
            this.dataFolder = dataFolder;
            this.chainIndexer = chainIndexer;
            this.cancellation = new CancellationTokenSource();
            this.asyncProvider = asyncProvider;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.lockObject = new object();
        }

        /// <inheritdoc />
        public void Initialize()
        {
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
            lock (this.lockObject)
            {
                if (!this.NFTContractCollection.Exists(x => x.ContractAddress == contractAddress))
                {
                    NFTContractModel model = new NFTContractModel()
                    {
                        ContractAddress = contractAddress,
                        LastUpdatedBlock = 0,
                        OwnedIDsByAddress = new Dictionary<string, List<ulong>>()
                    };

                    this.NFTContractCollection.Upsert(model);
                }
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

            OwnedNFTsModel output = new OwnedNFTsModel() { OwnedIDsByContractAddress = new Dictionary<string, List<ulong>>() };

            foreach (NFTContractModel contractModel in NFTContractModels)
            {
                List<ulong> ids = contractModel.OwnedIDsByAddress[address];
                output.OwnedIDsByContractAddress.Add(contractModel.ContractAddress, ids);
            }

            return output;
        }
        
        private async Task IndexNFTsContinuouslyAsync()
        {
            while (!this.cancellation.Token.IsCancellationRequested)
            {
                List<string> contracts = this.NFTContractCollection.FindAll().Select(x => x.ContractAddress).ToList();

                foreach (string contractAddr in contracts)
                {
                    if (this.cancellation.Token.IsCancellationRequested)
                        break;

                    // TODO get receipt
                }



                // TODO
                // go throught all watched contracts and get logs from last updated block till now, get logs and update model based on them

                // if address has no ids then remove it from list
            }
        }

        public void Dispose()
        {
            this.cancellation.Cancel();
            this.indexingTask?.GetAwaiter().GetResult();
            this.db.Dispose();
        }
    }

    public class NFTContractModel
    {
        public string ContractAddress { get; set; }

        // Key is nft owner address, value is list of NFT IDs
        public Dictionary<string, List<ulong>> OwnedIDsByAddress { get; set; }

        public ulong LastUpdatedBlock { get; set; }
    }

    public class OwnedNFTsModel
    {
        public Dictionary<string, List<ulong>> OwnedIDsByContractAddress { get; set; }
    }
}
