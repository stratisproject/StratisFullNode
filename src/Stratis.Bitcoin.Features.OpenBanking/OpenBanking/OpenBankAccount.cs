using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public class OpenBankAccount : IOpenBankAccount
    {
        public IOpenBankIdentifier OpenBankIdentifier { get; private set; }

        public MetadataTrackerEnum MetaDataTrackerEnum { get; private set; }

        public OpenBankAccount()
        {
        }

        public OpenBankAccount(IOpenBankIdentifier openBankIdentifier, MetadataTrackerEnum metaDataTrackerEnum)
        {
            this.OpenBankIdentifier = openBankIdentifier;
            this.MetaDataTrackerEnum = metaDataTrackerEnum;
        }
    }
}
