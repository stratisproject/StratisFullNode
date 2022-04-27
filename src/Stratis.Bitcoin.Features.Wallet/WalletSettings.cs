using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Settings;

namespace Stratis.Bitcoin.Features.Wallet
{
    /// <summary>
    /// Configuration related to the wallet.
    /// </summary>
    public class WalletSettings : BaseSettings
    {
        /// <summary>
        /// A value indicating whether the transactions hex representations should be saved in the wallet file.
        /// </summary>
        [CommandLineOption("savetrxhex", "Save the hex of transactions in the wallet file.", false)]
        public bool SaveTransactionHex { get; set; }

        /// <summary>
        /// A value indicating whether to unlock the supplied default wallet on startup.
        /// </summary>
        [CommandLineOption("unlockdefaultwallet", "Unlocks the specified default wallet.", false)]
        public bool UnlockDefaultWallet { get; set; }

        /// <summary>
        /// Name for the default wallet.
        /// </summary>
        [CommandLineOption("defaultwalletname", "Loads the specified wallet on startup. If it doesn't exist, it will be created automatically.")]
        public string DefaultWalletName { get; set; }

        /// <summary>
        /// Password for the default wallet if overriding the default.
        /// </summary>
        [CommandLineOption("defaultwalletpassword", "Overrides the default wallet password.", "default", false)]
        public string DefaultWalletPassword { get; set; }

        /// <summary>Size of the buffer of unused addresses maintained in an account.</summary>
        [CommandLineOption("walletaddressbuffer", "Size of the buffer of unused addresses maintained in an account.", 20)]
        public int UnusedAddressesBuffer { get; set; }

        /// <summary>
        /// A value indicating whether the wallet being run is the light wallet or the full wallet.
        /// </summary>
        public bool IsLightWallet { get; set; }        

        public WalletSettings(NodeSettings nodeSettings) : base(nodeSettings)
        {
        }

        /// <summary>
        /// Check if the default wallet is specified.
        /// </summary>
        /// <returns>Returns true if the <see cref="DefaultWalletName"/> is other than empty string.</returns>
        public bool IsDefaultWalletEnabled()
        {
            return !string.IsNullOrWhiteSpace(this.DefaultWalletName);
        }
    }
}
