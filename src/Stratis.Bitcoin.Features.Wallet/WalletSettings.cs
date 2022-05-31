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
        [CommandLineOption("savetrxhex", "Save the hex of transactions in the wallet file.")]
        public bool SaveTransactionHex { get; private set; } = false;

        /// <summary>
        /// A value indicating whether to unlock the supplied default wallet on startup.
        /// </summary>
        [CommandLineOption("unlockdefaultwallet", "Unlocks the specified default wallet.")]
        public bool UnlockDefaultWallet { get; private set; } = false;

        /// <summary>
        /// Name for the default wallet.
        /// </summary>
        [CommandLineOption("defaultwalletname", "Loads the specified wallet on startup. If it doesn't exist, it will be created automatically.")]
        public string DefaultWalletName { get; private set; } = null;

        /// <summary>
        /// Password for the default wallet if overriding the default.
        /// </summary>
        [CommandLineOption("defaultwalletpassword", "Overrides the default wallet password.", false)]
        public string DefaultWalletPassword { get; private set; } = "default";

        /// <summary>
        /// A value indicating whether the wallet being run is the light wallet or the full wallet.
        /// </summary>
        public bool IsLightWallet { get; set; }

        /// <summary>Size of the buffer of unused addresses maintained in an account.</summary>
        [CommandLineOption("walletaddressbuffer", "Size of the buffer of unused addresses maintained in an account.")]
        public int UnusedAddressesBuffer { get; private set; } = 20;

        /// <summary>
        /// Initializes an instance of the object from the node configuration.
        /// </summary>
        /// <param name="nodeSettings">The node configuration.</param>
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
