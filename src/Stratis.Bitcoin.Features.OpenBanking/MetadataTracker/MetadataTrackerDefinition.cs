using NBitcoin;

namespace Stratis.Bitcoin.Features.SmartContracts.MetadataTracker
{
    public enum MetadataTableNumber
    {
        // Don't exceed 63.
        GBPT = 0
    }

    /// <summary>
    /// Holds information related to tracking a particular topic for a particular contract and log type.
    /// </summary>
    public class MetadataTrackerDefinition
    {
        /// <summary>
        /// Identifies the indexing table for this definition.
        /// </summary>
        public MetadataTableNumber TableNumber { get; set; }

        /// <summary>
        /// Identifies the contract to monitor logs for.
        /// </summary>
        public string Contract { get; set; }

        /// <summary>
        /// Identfies the log type to index.
        /// </summary>
        public string LogType { get; set; }

        /// <summary>
        /// Identifies the metadata/topic to index by its position in the "Topics" array.
        /// </summary>
        public int MetadataTopic { get; set; }

        /// <summary>
        /// The first block to scan for logs.
        /// </summary>
        public int FirstBlock { get; set; }

        /// <summary>
        /// The last block that was scanned for logs.
        /// </summary>
        public BlockLocator BlockLocator;
    }
}
