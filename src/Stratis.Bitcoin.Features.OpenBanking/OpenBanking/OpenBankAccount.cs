using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public class OpenBankAccount : IOpenBankAccount
    {
        public OpenBankConfiguration OpenBankConfiguration { get; set; }

        public string OpenBankAccountNumber { get; set; }

        public string Currency { get; set; }

        public MetadataTrackerEnum MetaDataTrackerEnum { get; set; }

        public OpenBankAccount()
        {
        }

        public OpenBankAccount(OpenBankConfiguration openBankConfiguration, string openBankAccountNumber, MetadataTrackerEnum metaDataTrackerEnum, string currency)
        {
            this.OpenBankConfiguration = openBankConfiguration;
            this.OpenBankAccountNumber = openBankAccountNumber;
            this.MetaDataTrackerEnum = metaDataTrackerEnum;
            this.Currency = currency;
        }
    }
}
