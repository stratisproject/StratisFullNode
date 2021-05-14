using System.IO;
using System.Linq;
using System.Net;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.P2P;

namespace Stratis.Bitcoin.Configuration
{
    /// <summary>
    /// Contains path locations to folders and files on disk.
    /// Used by various components of the full node.
    /// </summary>
    /// <remarks>
    /// Location name should describe if its a file or a folder.
    /// File location names end with "File" (i.e AddrMan[File]).
    /// Folder location names end with "Path" (i.e CoinView[Path]).
    /// </remarks>
    public class DataFolder
    {
        /// <summary>
        /// Initializes the path locations.
        /// </summary>
        /// <param name="rootPath">The data directory root path.</param>
        public DataFolder(string rootPath, DbType dbType = DbType.Leveldb)
        {
            string databasePath = rootPath;
            if (dbType != DbType.Leveldb)
            {
                databasePath = Path.Combine(rootPath, dbType.ToString().ToLowerInvariant());
                Directory.CreateDirectory(databasePath);
            }

            this.CoindbPath = Path.Combine(databasePath, "coindb");
            this.AddressManagerFilePath = rootPath;
            this.ChainPath = Path.Combine(databasePath, "chain");
            this.KeyValueRepositoryPath = Path.Combine(databasePath, "common");
            this.InteropRepositoryPath = Path.Combine(rootPath, "interop");
            this.ConversionRepositoryPath = Path.Combine(rootPath, "conversion");
            this.BlockPath = Path.Combine(databasePath, "blocks");
            this.PollsPath = Path.Combine(rootPath, "polls");
            this.IndexPath = Path.Combine(rootPath, "index");
            this.RpcCookieFile = Path.Combine(rootPath, ".cookie");
            this.WalletPath = Path.Combine(rootPath);
            this.LogPath = Path.Combine(rootPath, "logs");
            this.DnsMasterFilePath = rootPath;
            this.SmartContractStatePath = Path.Combine(rootPath, "contracts");
            this.ProvenBlockHeaderPath = Path.Combine(databasePath, "provenheaders");
            this.RootPath = rootPath;
        }

        /// <summary>
        /// The DataFolder's path.
        /// </summary>
        public string RootPath { get; }

        /// <summary>Address manager's database of peers.</summary>
        /// <seealso cref="PeerAddressManager.SavePeers(string, string)"/>
        public string AddressManagerFilePath { get; private set; }

        /// <summary>Path to the folder with coinview database files.</summary>
        public string CoindbPath { get; set; }

        /// <summary>Path to the folder with node's chain repository database files.</summary>
        /// <seealso cref="Base.BaseFeature.StartChain"/>
        public string ChainPath { get; internal set; }

        /// <summary>Path to the folder with separated key-value items managed by <see cref="IKeyValueRepository"/>.</summary>
        public string KeyValueRepositoryPath { get; internal set; }

        public string InteropRepositoryPath { get; internal set; }

        public string ConversionRepositoryPath { get; internal set; }

        /// <summary>Path to the folder with block repository database files.</summary>
        /// <seealso cref="Features.BlockStore.BlockRepository.BlockRepository"/>
        public string BlockPath { get; internal set; }

        /// <summary>Path to the folder with polls.</summary>
        public string PollsPath { get; internal set; }

        /// <summary>Path to the folder with block repository database files.</summary>
        /// <seealso cref="Features.IndexStore.IndexRepository.IndexRepository"/>
        public string IndexPath { get; internal set; }

        /// <summary>File to store RPC authorization cookie.</summary>
        /// <seealso cref="Features.RPC.Startup.Configure"/>
        public string RpcCookieFile { get; internal set; }

        /// <summary>Path to wallet files.</summary>
        /// <seealso cref="Features.Wallet.WalletManager.LoadWallet"/>
        public string WalletPath { get; internal set; }

        /// <summary>Path to log files.</summary>
        /// <seealso cref="Logging.LoggingConfiguration"/>
        public string LogPath { get; internal set; }

        /// <summary>Path to DNS masterfile.</summary>
        /// <seealso cref="Dns.IMasterFile.Save"/>
        public string DnsMasterFilePath { get; internal set; }

        /// <summary>Path to the folder with smart contract state database files.</summary>
        public string SmartContractStatePath { get; set; }

        /// <summary>Path to the folder for <see cref="ProvenBlockHeader"/> items database files.</summary>
        public string ProvenBlockHeaderPath { get; set; }

        /// <summary>True if the chain state directories are scheduled for deletion on the next graceful shutdown.</summary>
        public bool ScheduledChainDeletion { get; private set; }

        /// <summary>
        /// Schedule the data folders storing chain state for deletion on the next graceful shutdown.
        /// </summary>
        public void ScheduleChainDeletion()
        {
            this.ScheduledChainDeletion = true;
        }

        /// <summary>
        /// Deletes all directories that may be problematic for syncing. Will not delete a directory if it contains any files with a *.db extension.
        /// This should not be called while the node is running.
        /// </summary>
        public void DeleteChainDirectories()
        {
            var dirsForDeletion = new string[] { BlockPath, ChainPath, CoindbPath, KeyValueRepositoryPath, SmartContractStatePath, ProvenBlockHeaderPath };

            foreach (var dir in dirsForDeletion.Select(dir => new DirectoryInfo(dir)))
            {
                if (!dir.Exists)
                    continue;

                // Ignore any directories that contain suspected wallets.
                if (dir.EnumerateFiles("*.db").Any())
                    continue;

                dir.Delete(true);
            }
        }
    }
}
