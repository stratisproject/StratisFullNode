using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Database;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.Core.Receipts;

namespace Stratis.Bitcoin.Features.SmartContracts.MetadataTracker
{
    /// <summary>Maintains a table that uses as primary key a specified topic found in a specified contract and log type. The rest of the fields identify the block and transaction id.</summary>
    /// <remarks><para>The class can return table rows by primary key (topic) and can also return the row with the greatest topic value.</para><para>The class deals with re-orgs transparently.</para></remarks>
    public class MetadataTracker : IMetadataTracker
    {
        private const byte CommonTableOffset = 0;
        private const byte MetadataTableOffset = 64;
        private const byte IndexTableOffset = 128;

        private static readonly byte[] BlockLocatorKey = new byte[1] { 0 };

        private Dictionary<MetadataTableNumber, MetadataTrackerDefinition> trackingDefinitions;

        private readonly IDb db;
        private readonly ChainIndexer chainIndexer;
        private readonly ILogger logger;
        private readonly ReceiptSearcher receiptSearcher;
        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly ISignals signals;

        private object lockObj = new object();

        private ChainedHeader Tip(MetadataTrackerDefinition trackingDefinition) => this.chainIndexer.FindFork(trackingDefinition.BlockLocator?.Blocks ?? new uint256[] { }.ToList()) ?? this.chainIndexer[0];

        public MetadataTracker(DataFolder dataFolder, ChainIndexer chainIndexer, ILoggerFactory loggerFactory, ReceiptSearcher receiptSearcher, DBreezeSerializer dBreezeSerializer, ISignals signals)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            // The DB records the ExternalId and the HashHeightPair where it was seen.
            this.db = new LevelDb();
            this.db.Open(Path.Combine(dataFolder.RootPath, "metadata"));
            this.chainIndexer = chainIndexer;
            this.receiptSearcher = receiptSearcher;
            this.dBreezeSerializer = dBreezeSerializer;
            this.signals = signals;
            this.trackingDefinitions = new Dictionary<MetadataTableNumber, MetadataTrackerDefinition>();
            this.ReadConfig(dataFolder.RootPath);
        }

        public void Register(MetadataTrackerDefinition trackingDefinition)
        {
            Guard.Assert(!this.trackingDefinitions.ContainsKey(trackingDefinition.TableNumber));

            this.trackingDefinitions[trackingDefinition.TableNumber] = trackingDefinition;
        }

        public MetadataTrackerDefinition GetTracker(MetadataTableNumber metaDataTrackerEnum)
        {
            this.trackingDefinitions.TryGetValue(metaDataTrackerEnum, out MetadataTrackerDefinition metadataTrackingDefinition);

            return metadataTrackingDefinition;
        }

        private void ReadConfig(string rootPath)
        {
            try
            {
                this.trackingDefinitions.Clear();

                string fileName = Path.Combine(rootPath, "metadata.conf");

                if (!File.Exists(fileName))
                {
                    string exampleFile = Path.Combine(rootPath, "metadata (example).conf");
                    if (!File.Exists(exampleFile))
                    {
                        var json2 = JsonSerializer.Serialize(new[] { new MetadataTrackerDefinition() {
                             TableNumber = 0,
                             Contract = "tBHv3YgiSGZiohpEdTcsNbXivrCzxVReeP",
                             LogType = "BurnMetadata",
                             MetadataTopic = 2,
                             FirstBlock = 3200000
                        } }, new JsonSerializerOptions() { WriteIndented = true });

                        json2 = json2.Replace("0,", $"0, // A constant from the {nameof(MetadataTableNumber)} enumeration.");
                        json2 = json2.Replace("P\",", $"P\", // The coin contract address.");
                        json2 = json2.Replace("a\",", $"a\", // The log struct name containing the topic to index.");
                        json2 = json2.Replace("2,", $"2, // The topic position in the log to index.");
                        json2 = json2.Replace("00,", $"00, // The first block to index.");

                        File.WriteAllText(exampleFile, json2);
                    }

                    return;
                }

                string json = File.ReadAllText(fileName);

                foreach (var definition in JsonSerializer.Deserialize<MetadataTrackerDefinition[]>(json))
                {
                    this.Register(definition);
                }
            }
            catch (Exception err)
            {
                this.logger.LogError(err.Message);
            }
        }

        public void Initialize()
        {
            foreach (MetadataTrackerDefinition trackingDefinition in this.trackingDefinitions.Values)
            {
                var commonTable = (byte)(CommonTableOffset + trackingDefinition.TableNumber);

                var blockLocator = this.db.Get(commonTable, BlockLocatorKey);
                trackingDefinition.BlockLocator = (blockLocator != null) ? this.dBreezeSerializer.Deserialize<BlockLocator>(blockLocator) : null;
            }

            Sync();

            this.signals.Subscribe<BlockConnected>(this.BlockConnected);
        }

        public MetadataTrackerEntry GetEntryByMetadata(MetadataTableNumber tracker, string metadata)
        {
            lock (this.lockObj)
            {
                MetadataTrackerDefinition trackingDefinition = this.trackingDefinitions[tracker];
                var metadataTable = (byte)(MetadataTableOffset + trackingDefinition.TableNumber);
                byte[] metaDataKey = ASCIIEncoding.ASCII.GetBytes(metadata);

                byte[] bytes = this.db.Get(metadataTable, metaDataKey);
                if (bytes == null)
                    return null;

                return this.dBreezeSerializer.Deserialize<MetadataTrackerEntry>(bytes);
            }
        }

        private void BlockConnected(BlockConnected blockConnected)
        {
            Sync();
        }

        private IEnumerable<MetadataTrackerEntry> GetMetadata(string address, string logType, int from, int? to, int metadataTopic)
        {
            foreach (Receipt receipt in this.receiptSearcher.SearchReceipts(address, from, to, new byte[][] { ASCIIEncoding.ASCII.GetBytes(logType) }))
            {
                foreach (Log log in receipt.Logs)
                {
                    if (ASCIIEncoding.ASCII.GetString(log.Topics[0]) != logType)
                        continue;

                    yield return new MetadataTrackerEntry()
                    {
                        Block = new HashHeightPair(receipt.BlockHash, (int)receipt.BlockNumber),
                        TxId = receipt.TransactionHash,
                        Metadata = ASCIIEncoding.ASCII.GetString(log.Topics[metadataTopic])
                    };
                }
            }
        }

        private void Sync()
        {
            foreach (MetadataTrackerDefinition trackingDefinition in this.trackingDefinitions.Values)
            {
                int minHeight = Math.Max(Tip(trackingDefinition).Height, trackingDefinition.FirstBlock);
                int maxHeight = this.chainIndexer.Tip.Height;

                if (minHeight == maxHeight && this.Tip(trackingDefinition).HashBlock == trackingDefinition.BlockLocator?.Blocks[0])
                    return;

                MetadataTrackerEntry[] res = GetMetadata(trackingDefinition.Contract, trackingDefinition.LogType, minHeight, maxHeight, trackingDefinition.MetadataTopic).ToArray();

                lock (this.lockObj)
                {
                    using (var batch = this.db.GetWriteBatch())
                    {
                        var indexTable = (byte)(IndexTableOffset + trackingDefinition.TableNumber);
                        var metadataTable = (byte)(MetadataTableOffset + trackingDefinition.TableNumber);
                        var commonTable = (byte)(CommonTableOffset + trackingDefinition.TableNumber);

                        // If these don't match then there has been a re-org.
                        if (this.Tip(trackingDefinition).HashBlock != trackingDefinition.BlockLocator?.Blocks[0])
                        {
                            using (var iterator = this.db.GetIterator(indexTable))
                            {
                                // Remove everything above the last valid height. The purpose of the index table is to identify the entries to remove.
                                foreach ((byte[] key, _) in iterator.GetAll(keysOnly: true, firstKey: BitConverter.GetBytes(this.Tip(trackingDefinition).Height).Reverse().ToArray(), includeFirstKey: false))
                                {
                                    batch.Delete(indexTable, key);
                                    batch.Delete(metadataTable, key.Skip(4).ToArray());
                                }
                            }
                        }

                        foreach (MetadataTrackerEntry entry in res)
                        {
                            this.logger.LogInformation("Indexing '{0}' with '{1}' for contract address '{2}' at height {3}.", trackingDefinition.LogType, entry.Metadata, trackingDefinition.Contract, entry.Block.Height);

                            byte[] metaDataKey = ASCIIEncoding.ASCII.GetBytes(entry.Metadata);
                            batch.Put(metadataTable, metaDataKey, this.dBreezeSerializer.Serialize(entry));
                            batch.Put(indexTable, BitConverter.GetBytes(entry.Block.Height).Reverse().Concat(metaDataKey).ToArray(), new byte[0]);
                        }

                        trackingDefinition.BlockLocator = this.chainIndexer[maxHeight].GetLocator();

                        batch.Put(commonTable, BlockLocatorKey, this.dBreezeSerializer.Serialize(trackingDefinition.BlockLocator));

                        batch.Write();
                    }
                }
            }
        }
    }
}
