namespace Stratis.Bitcoin.Features.SmartContracts.MetadataTracker
{
    /// <summary>
    /// Indexes contract logs by their unique metadata.
    /// </summary>
    public interface IMetadataTracker
    {
        /// <summary>
        /// Registers a <see cref="MetadataTrackerDefinition"/>.
        /// </summary>
        /// <param name="trackingDefinition">The <see cref="MetadataTrackerDefinition"/> to register.</param>
        void Register(MetadataTrackerDefinition trackingDefinition);

        /// <summary>
        /// Retrieves a previously registered <see cref="MetadataTrackerDefinition".
        /// </summary>
        /// <param name="table">The table of the tracking definition to retrieve.</param>
        /// <returns>See <see cref="MetadataTrackerDefinition"/>.</returns>
        MetadataTrackerDefinition GetTracker(MetadataTableNumber table);

        /// <summary>
        /// Initializes the component. It should be up-to-date with the chain indexer tip following this call.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Retrieves tracking information by specifying a unique metadata value.
        /// </summary>
        /// <param name="table">The table identifying the tracking information to retrieve.</param>
        /// <param name="metadata">The unique metadata value.</param>
        /// <returns>The tracking infomation. See <see cref="MetadataTrackerEntry"/>.</returns>
        MetadataTrackerEntry GetEntryByMetadata(MetadataTableNumber table, string metadata);
    }
}
