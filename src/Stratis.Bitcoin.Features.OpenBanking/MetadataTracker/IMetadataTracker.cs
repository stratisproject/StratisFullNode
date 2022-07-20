namespace Stratis.Bitcoin.Features.SmartContracts.MetadataTracker
{
    public interface IMetadataTracker
    {
        void Register(MetadataTrackerDefinition trackingDefinition);

        MetadataTrackerDefinition GetTracker(MetadataTrackerEnum metaDataTrackerEnum);

        void Initialize();

        MetadataTrackerEntry GetEntryByMetadata(MetadataTrackerEnum tracker, string metadata);

        /// <summary>
        /// Gets the last entry.
        /// </summary>
        /// <param name="tracker">See <see cref="MetadataTrackerEnum"/>.</param>
        /// <returns>See <see cref="MetadataTrackerEntry"/>.</returns>
        /// <remarks>
        /// <para>The expectation is that when sorted by the topic/metadata the entries will be in
        /// chronological order with the most recent entry last.</para><para>If necessary the metadata could zero-padded 
        /// or prefixed with a date in the format "yyyy-MM-dd HH:mm:ss".</para>
        /// </remarks>
        MetadataTrackerEntry GetLastEntry(MetadataTrackerEnum tracker);
    }
}
