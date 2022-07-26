namespace Stratis.Bitcoin.Features.SmartContracts.MetadataTracker
{
    public interface IMetadataTracker
    {
        void Register(MetadataTrackerDefinition trackingDefinition);

        MetadataTrackerDefinition GetTracker(MetadataTableNumber metaDataTrackerEnum);

        void Initialize();

        MetadataTrackerEntry GetEntryByMetadata(MetadataTableNumber tracker, string metadata);
    }
}
