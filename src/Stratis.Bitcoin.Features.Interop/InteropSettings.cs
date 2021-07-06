using System;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.Wallet;

namespace Stratis.Bitcoin.Features.Interop
{
    public class InteropSettings
    {
        public ETHInteropSettings ETHSettings { get; set; }

        public BNBInteropSettings BNBSettings { get; set; }

        public InteropSettings(NodeSettings nodeSettings)
        {
            this.ETHSettings = new ETHInteropSettings(nodeSettings);
            this.BNBSettings = new BNBInteropSettings(nodeSettings);
        }

        public ETHInteropSettings GetSettingsByChain(DestinationChain chain)
        {
            switch (chain)
            {
                case DestinationChain.ETH:
                    {
                        return this.ETHSettings;
                    }
                case DestinationChain.BNB:
                    {
                        return this.BNBSettings;
                    }
            }

            throw new NotImplementedException("Provided chain type not supported: " + chain);
        }
    }

    public class ETHInteropSettings
    {
        public bool InteropEnabled { get; set; }

        /// <summary>This should be set to the address of the multisig wallet contract deployed on the Ethereum blockchain.</summary>
        public string MultisigWalletAddress { get; set; }

        /// <summary>This should be set to the address of the Wrapped STRAX ERC-20 contract deployed on the Ethereum blockchain.</summary>
        public string WrappedStraxContractAddress { get; set; }

        /// <summary>This is the RPC address of the geth node running on the local machine. It is normally defaulted to http://localhost:8545</summary>
        public string ClientUrl { get; set; }

        /// <summary>
        /// Address of the account on your geth node. It is the account that will be used for transaction
        /// signing and all interactions with the multisig and wrapped STRAX contracts.
        /// </summary>
        public string Account { get; set; }

        /// <summary>Passphrase for the ethereum account.</summary>
        public string Passphrase { get; set; }

        /// <summary>The gas limit for Ethereum interoperability transactions.</summary>
        public int GasLimit { get; set; }

        /// <summary>The gas price for Ethereum interoperability transactions (denominated in gwei).</summary>
        public int GasPrice { get; set; }

        #region unused

        /// <summary>This is intended for future functionality and should therefore not be provided/set yet.</summary>
        public string InteropContractCirrusAddress { get; set; }

        /// <summary>This is intended for future functionality and should therefore not be provided/set yet.</summary>
        public string InteropContractAddress { get; set; }

        #endregion

        public ETHInteropSettings(NodeSettings nodeSettings)
        {
            this.InteropEnabled = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "interopenabled", false);

            if (!this.InteropEnabled)
                return;

            string clientUrlKey = this.GetSettingsPrefix() + "clienturl";
            string wrappedStraxContractAddressKey = this.GetSettingsPrefix() + "wrappedstraxcontractaddress";
            string multisigWalletContractAddressKey = this.GetSettingsPrefix() + "multisigwalletcontractaddress";

            this.InteropContractCirrusAddress = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "interopcontractcirrusaddress", "");
            this.InteropContractAddress = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "interopcontractaddress", "");

            this.MultisigWalletAddress = nodeSettings.ConfigReader.GetOrDefault(multisigWalletContractAddressKey, "");
            this.WrappedStraxContractAddress = nodeSettings.ConfigReader.GetOrDefault(wrappedStraxContractAddressKey, "");
            this.ClientUrl = nodeSettings.ConfigReader.GetOrDefault(clientUrlKey, "http://localhost:8545");
            this.Account = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "account", "");
            this.Passphrase = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "passphrase", "");

            this.GasLimit = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "gas", 3_000_000);
            this.GasPrice = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "gasprice", 100);

            if (string.IsNullOrWhiteSpace(this.MultisigWalletAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{multisigWalletContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.WrappedStraxContractAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{wrappedStraxContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.ClientUrl))
                throw new Exception($"Cannot initialize interoperability feature without -{clientUrlKey} specified.");
        }

        protected virtual string GetSettingsPrefix()
        {
            return "eth_";
        }
    }

    public class BNBInteropSettings : ETHInteropSettings
    {
        public BNBInteropSettings(NodeSettings nodeSettings) : base(nodeSettings)
        {
        }

        protected override string GetSettingsPrefix()
        {
            return "bnb_";
        }
    }
}
