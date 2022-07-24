using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Settings;

namespace Stratis.Bitcoin.Features.OpenBanking
{
    /// <summary>
    /// Configuration related to the OpenBanking feature.
    /// </summary>
    public class OpenBankingSettings : BaseSettings
    {
        /// <summary>The minter's wallet name.</summary>
        [CommandLineOption("minterwallet", "The minter's wallet name.")]
        public string WalletName { get; set; } = "default";

        [CommandLineOption("minterpassword", "The minter's wallet password.", false)]
        public string WalletPassword { get; set; } = null;

        [CommandLineOption("minteraccount", "The minter's wallet account.")]
        public string WalletAccount { get; set; } = "account 0";

        [CommandLineOption("minteraddress", "The minter's wallet address.")]
        public string WalletAddress { get; set; } = null;

        /// <summary>
        /// Initializes an instance of the object from the node configuration.
        /// </summary>
        /// <param name="nodeSettings">The node configuration.</param>
        public OpenBankingSettings(NodeSettings nodeSettings) : base(nodeSettings)
        {
        }
    }
}
