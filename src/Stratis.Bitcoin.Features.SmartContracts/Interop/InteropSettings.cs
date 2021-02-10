using System;
using Stratis.Bitcoin.Configuration;

namespace Stratis.Bitcoin.Features.SmartContracts.Interop
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

        public bool Enabled { get; set; }

        public string InteropContractCirrusAddress { get; set; }

        public string InteropContractEthereumAddress { get; set; }

        public string MultisigWalletAddress { get; set; }

        public string WrappedStraxAddress { get; set; }

        public string EthereumClientUrl { get; set; }

        public string EthereumAccount { get; set; }

        public string EthereumPassphrase { get; set; }

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
