using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public class OpenBankAccount : IOpenBankAccount
    {
        public IOpenBankIdentifier OpenBankIdentifier { get; private set; }

        public string OpenBankAccountNumber { get; private set; }

        public string Currency { get; private set; }

        public MetadataTrackerEnum MetaDataTrackerEnum { get; private set; }

        public OpenBankAccount()
        {
        }

        public OpenBankAccount(IOpenBankIdentifier openBankIdentifier, string openBankAccountNumber, MetadataTrackerEnum metaDataTrackerEnum, string currency)
        {
            this.OpenBankIdentifier = openBankIdentifier;
            this.OpenBankAccountNumber = openBankAccountNumber;
            this.MetaDataTrackerEnum = metaDataTrackerEnum;
            this.Currency = currency;
        }
    }
}
