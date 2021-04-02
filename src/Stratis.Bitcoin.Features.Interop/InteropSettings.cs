using System;
using Stratis.Bitcoin.Configuration;

namespace Stratis.Bitcoin.Features.Interop
{
    public class InteropSettings
    {
        public const string InteropKey = "interop";
        public const string InteropContractCirrusAddressKey = "interopcontractcirrusaddress";
        public const string InteropContractEthereumAddressKey = "interopcontractethereumaddress";
        public const string MultisigWalletContractAddressKey = "multisigwalletcontractaddress";
        public const string WrappedStraxContractAddressKey = "wrappedstraxcontractaddress";
        public const string EthereumClientUrlKey = "ethereumclienturl";
        public const string EthereumAccountKey = "ethereumaccount";
        public const string EthereumPassphraseKey = "ethereumpassphrase";
        public const string EthereumGasKey = "ethereumgas";
        public const string EthereumGasPriceKey = "ethereumgasprice";

        public bool Enabled { get; set; }

        /// <summary>This is intended for future functionality and should therefore not be provided/set yet.</summary>
        public string InteropContractCirrusAddress { get; set; }

        /// <summary>This is intended for future functionality and should therefore not be provided/set yet.</summary>
        public string InteropContractEthereumAddress { get; set; }

        /// <summary>This should be set to the address of the multisig wallet contract deployed on the Ethereum blockchain.</summary>
        public string MultisigWalletAddress { get; set; }

        /// <summary>This should be set to the address of the Wrapped STRAX ERC-20 contract deployed on the Ethereum blockchain.</summary>
        public string WrappedStraxAddress { get; set; }

        /// <summary>This is the RPC address of the geth node running on the local machine. It is normally defaulted to http://localhost:8545</summary>
        public string EthereumClientUrl { get; set; }

        /// <summary>
        /// Address of the account on your geth node. It is the account that will be used for transaction
        /// signing and all interactions with the multisig and wrapped STRAX contracts.
        /// </summary>
        public string EthereumAccount { get; set; }

        /// <summary>Passphras for the ethereum account.</summary>
        public string EthereumPassphrase { get; set; }

        /// <summary>The gas limit for Ethereum interoperability transactions.</summary>
        public int EthereumGasLimit { get; set; }

        /// <summary>
        /// The gas price for Ethereum interoperability transactions (denominated in gwei).
        /// </summary>
        public int EthereumGasPrice { get; set; }

        public InteropSettings(NodeSettings nodeSettings)
        {
            this.Enabled = nodeSettings.ConfigReader.GetOrDefault(InteropKey, false);

            this.InteropContractCirrusAddress = nodeSettings.ConfigReader.GetOrDefault(InteropContractCirrusAddressKey, "");
            this.InteropContractEthereumAddress = nodeSettings.ConfigReader.GetOrDefault(InteropContractEthereumAddressKey, "");

            this.MultisigWalletAddress = nodeSettings.ConfigReader.GetOrDefault(MultisigWalletContractAddressKey, "");
            this.WrappedStraxAddress = nodeSettings.ConfigReader.GetOrDefault(WrappedStraxContractAddressKey, "");
            this.EthereumClientUrl = nodeSettings.ConfigReader.GetOrDefault(EthereumClientUrlKey, "http://localhost:8545");
            this.EthereumAccount = nodeSettings.ConfigReader.GetOrDefault(EthereumAccountKey, "");
            this.EthereumPassphrase = nodeSettings.ConfigReader.GetOrDefault(EthereumPassphraseKey, "");

            this.EthereumGasLimit = nodeSettings.ConfigReader.GetOrDefault(EthereumGasKey, 3_000_000);
            this.EthereumGasPrice = nodeSettings.ConfigReader.GetOrDefault(EthereumGasPriceKey, 100);

            if (!this.Enabled)
                return;

            if (string.IsNullOrWhiteSpace(this.MultisigWalletAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{MultisigWalletContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.WrappedStraxAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{WrappedStraxContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.EthereumClientUrl))
                throw new Exception($"Cannot initialize interoperability feature without -{EthereumClientUrlKey} specified.");
        }
    }
}
