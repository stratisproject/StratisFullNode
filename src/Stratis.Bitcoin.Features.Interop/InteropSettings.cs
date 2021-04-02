using System;
using Stratis.Bitcoin.Configuration;

namespace Stratis.Bitcoin.Features.Interop
{
    public class InteropSettings
    {
        public const string InteropKey = "interop";
        public const string InteropContractCirrusAddressKey = "interopcontractcirrusaddress";
        public const string InteropContractETHAddressKey = "interopcontractethereumaddress";
        public const string MultisigWalletContractAddressKey = "multisigwalletcontractaddress";
        public const string WrappedStraxContractAddressKey = "wrappedstraxcontractaddress";
        public const string ETHClientUrlKey = "ethereumclienturl";
        public const string ETHAccountKey = "ethereumaccount";
        public const string ETHPassphraseKey = "ethereumpassphrase";
        public const string ETHGasKey = "ethereumgas";
        public const string ETHGasPriceKey = "ethereumgasprice";

        public bool Enabled { get; set; }

        /// <summary>This is intended for future functionality and should therefore not be provided/set yet.</summary>
        public string InteropContractCirrusAddress { get; set; }

        /// <summary>This is intended for future functionality and should therefore not be provided/set yet.</summary>
        public string InteropContractETHAddress { get; set; }

        /// <summary>This should be set to the address of the multisig wallet contract deployed on the Ethereum blockchain.</summary>
        public string MultisigWalletAddress { get; set; }

        /// <summary>This should be set to the address of the Wrapped STRAX ERC-20 contract deployed on the Ethereum blockchain.</summary>
        public string WrappedStraxAddress { get; set; }

        /// <summary>This is the RPC address of the geth node running on the local machine. It is normally defaulted to http://localhost:8545</summary>
        public string ETHClientUrl { get; set; }

        /// <summary>
        /// Address of the account on your geth node. It is the account that will be used for transaction
        /// signing and all interactions with the multisig and wrapped STRAX contracts.
        /// </summary>
        public string ETHAccount { get; set; }

        /// <summary>Passphras for the ethereum account.</summary>
        public string ETHPassphrase { get; set; }

        /// <summary>The gas limit for Ethereum interoperability transactions.</summary>
        public int ETHGasLimit { get; set; }

        /// <summary>The gas price for Ethereum interoperability transactions (denominated in gwei).</summary>
        public int ETHGasPrice { get; set; }

        public InteropSettings(NodeSettings nodeSettings)
        {
            this.Enabled = nodeSettings.ConfigReader.GetOrDefault(InteropKey, false);

            this.InteropContractCirrusAddress = nodeSettings.ConfigReader.GetOrDefault(InteropContractCirrusAddressKey, "");
            this.InteropContractETHAddress = nodeSettings.ConfigReader.GetOrDefault(InteropContractETHAddressKey, "");

            this.MultisigWalletAddress = nodeSettings.ConfigReader.GetOrDefault(MultisigWalletContractAddressKey, "");
            this.WrappedStraxAddress = nodeSettings.ConfigReader.GetOrDefault(WrappedStraxContractAddressKey, "");
            this.ETHClientUrl = nodeSettings.ConfigReader.GetOrDefault(ETHClientUrlKey, "http://localhost:8545");
            this.ETHAccount = nodeSettings.ConfigReader.GetOrDefault(ETHAccountKey, "");
            this.ETHPassphrase = nodeSettings.ConfigReader.GetOrDefault(ETHPassphraseKey, "");

            this.ETHGasLimit = nodeSettings.ConfigReader.GetOrDefault(ETHGasKey, 3_000_000);
            this.ETHGasPrice = nodeSettings.ConfigReader.GetOrDefault(ETHGasPriceKey, 100);

            if (!this.Enabled)
                return;

            if (string.IsNullOrWhiteSpace(this.MultisigWalletAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{MultisigWalletContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.WrappedStraxAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{WrappedStraxContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.ETHClientUrl))
                throw new Exception($"Cannot initialize interoperability feature without -{ETHClientUrlKey} specified.");
        }
    }
}
