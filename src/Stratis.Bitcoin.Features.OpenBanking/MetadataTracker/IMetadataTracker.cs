namespace Stratis.Bitcoin.Features.SmartContracts.MetadataTracker
{
    public interface IMetadataTracker
    {
        void Register(MetadataTrackerDefinition trackingDefinition);

        MetadataTrackerDefinition GetTracker(MetadataTrackerEnum metaDataTrackerEnum);

        void Initialize();

        MetadataTrackerEntry GetEntryByMetadata(MetadataTrackerEnum tracker, string metadata);
    }
}
