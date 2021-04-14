using System;
using Stratis.Bitcoin.Configuration;

namespace Stratis.Bitcoin.Features.Interop
{
    public class InteropSettings
    {
        public bool InteropEnabled { get; set; }

        private const string InteropEnabledKey = "interop";

        #region unused

        /// <summary>This is intended for future functionality and should therefore not be provided/set yet.</summary>
        public string InteropContractCirrusAddress { get; set; }

        private const string InteropContractCirrusAddressKey = "interopcontractcirrusaddress";
        
        /// <summary>This is intended for future functionality and should therefore not be provided/set yet.</summary>
        public string InteropContractETHAddress { get; set; }

        private const string InteropContractETHAddressKey = "interopcontractethereumaddress";
        #endregion

        #region ETH settings

        /// <summary>This should be set to the address of the multisig wallet contract deployed on the Ethereum blockchain.</summary>
        public string ETHMultisigWalletAddress { get; set; }

        private const string MultisigWalletContractAddressKey = "multisigwalletcontractaddress";

        /// <summary>This should be set to the address of the Wrapped STRAX ERC-20 contract deployed on the Ethereum blockchain.</summary>
        public string ETHWrappedStraxContractAddress { get; set; }

        private const string WrappedStraxContractAddressKey = "wrappedstraxcontractaddress";

        /// <summary>This is the RPC address of the geth node running on the local machine. It is normally defaulted to http://localhost:8545</summary>
        public string ETHClientUrl { get; set; }

        private const string ETHClientUrlKey = "ethereumclienturl";

        /// <summary>
        /// Address of the account on your geth node. It is the account that will be used for transaction
        /// signing and all interactions with the multisig and wrapped STRAX contracts.
        /// </summary>
        public string ETHAccount { get; set; }

        private const string ETHAccountKey = "ethereumaccount";

        /// <summary>Passphrase for the ethereum account.</summary>
        public string ETHPassphrase { get; set; }

        private const string ETHPassphraseKey = "ethereumpassphrase";

        /// <summary>The gas limit for Ethereum interoperability transactions.</summary>
        public int ETHGasLimit { get; set; }

        private const string ETHGasKey = "ethereumgas";

        /// <summary>The gas price for Ethereum interoperability transactions (denominated in gwei).</summary>
        public int ETHGasPrice { get; set; }

        private const string ETHGasPriceKey = "ethereumgasprice";

        public int ETHMultisigWalletQuorum { get; set; }

        private const string ETHMultisigWalletContractQuorumKey = "ethereummultisigwalletquorum";

        #endregion

        public InteropSettings(NodeSettings nodeSettings)
        {
            this.InteropEnabled = nodeSettings.ConfigReader.GetOrDefault(InteropEnabledKey, false);

            // ETH
            this.InteropContractCirrusAddress = nodeSettings.ConfigReader.GetOrDefault(InteropContractCirrusAddressKey, "");
            this.InteropContractETHAddress = nodeSettings.ConfigReader.GetOrDefault(InteropContractETHAddressKey, "");

            this.ETHMultisigWalletQuorum = nodeSettings.ConfigReader.GetOrDefault(ETHMultisigWalletContractQuorumKey, 6);
            this.ETHMultisigWalletAddress = nodeSettings.ConfigReader.GetOrDefault(MultisigWalletContractAddressKey, "");
            this.ETHWrappedStraxContractAddress = nodeSettings.ConfigReader.GetOrDefault(WrappedStraxContractAddressKey, "");
            this.ETHClientUrl = nodeSettings.ConfigReader.GetOrDefault(ETHClientUrlKey, "http://localhost:8545");
            this.ETHAccount = nodeSettings.ConfigReader.GetOrDefault(ETHAccountKey, "");
            this.ETHPassphrase = nodeSettings.ConfigReader.GetOrDefault(ETHPassphraseKey, "");

            this.ETHGasLimit = nodeSettings.ConfigReader.GetOrDefault(ETHGasKey, 3_000_000);
            this.ETHGasPrice = nodeSettings.ConfigReader.GetOrDefault(ETHGasPriceKey, 100);

            if (!this.InteropEnabled)
                return;

            if (string.IsNullOrWhiteSpace(this.ETHMultisigWalletAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{MultisigWalletContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.ETHWrappedStraxContractAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{WrappedStraxContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.ETHClientUrl))
                throw new Exception($"Cannot initialize interoperability feature without -{ETHClientUrlKey} specified.");
        }
    }
}
