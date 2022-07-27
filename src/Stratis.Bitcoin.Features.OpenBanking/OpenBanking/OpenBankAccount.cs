using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public class OpenBankAccount : IOpenBankAccount
    {
        /// <inheritdoc/>
        public OpenBankConfiguration OpenBankConfiguration { get; set; }

        /// <inheritdoc/>
        public string OpenBankAccountNumber { get; set; }

        /// <inheritdoc/>
        public string Currency { get; set; }

        /// <inheritdoc/>
        public MetadataTableNumber MetaDataTable { get; set; }

        /// <inheritdoc/>
        public string Contract { get; set; }

        /// <inheritdoc/>
        public int FirstBlock { get; set; }

        /// <summary>
        /// Parameterless constructor.
        /// </summary>
        public OpenBankAccount()
        {
        }

        /// <summary>
        /// Constructs the class.
        /// </summary>
        /// <param name="openBankConfiguration">Connection details for the open banking account.</param>
        /// <param name="openBankAccountNumber">The open banking account number.</param>
        /// <param name="metaDataTable">Identifies the tracking table to use.</param>
        /// <param name="currency">The expected open banking currency associated with deposit amounts.</param>
        /// <param name="contract">Identifies the stablecoin contract that ties up with the bank account.</param>
        /// <param name="firstBlock">Identifies the first block to monitor to confirm stable coin minting calls.</param>
        public OpenBankAccount(OpenBankConfiguration openBankConfiguration, string openBankAccountNumber, MetadataTableNumber metaDataTable, string currency, string contract, int firstBlock)
        {
            this.OpenBankConfiguration = openBankConfiguration;
            this.OpenBankAccountNumber = openBankAccountNumber;
            this.MetaDataTable = metaDataTable;
            this.Currency = currency;
            this.Contract = contract;
            this.FirstBlock = firstBlock;
        }
    }
}
