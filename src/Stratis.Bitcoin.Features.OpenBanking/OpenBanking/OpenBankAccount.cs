using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public class OpenBankAccount : IOpenBankAccount
    {
        public OpenBankConfiguration OpenBankConfiguration { get; set; }

        public string OpenBankAccountNumber { get; set; }

        public string Currency { get; set; }

        public MetadataTableNumber MetaDataTable { get; set; }

        public string Contract { get; set; }

        public int FirstBlock { get; set; }

        public OpenBankAccount()
        {
        }

        public OpenBankAccount(OpenBankConfiguration openBankConfiguration, string openBankAccountNumber, MetadataTableNumber metaDataTrackerEnum, string currency, string contract, int firstBlock)
        {
            this.OpenBankConfiguration = openBankConfiguration;
            this.OpenBankAccountNumber = openBankAccountNumber;
            this.MetaDataTable = metaDataTrackerEnum;
            this.Currency = currency;
            this.Contract = contract;
            this.FirstBlock = firstBlock;
        }
    }
}
