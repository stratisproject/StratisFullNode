using System;
using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.Interop.Models;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Interop
{
    public class InteropSettings
    {
        public ETHInteropSettings ETHSettings { get; set; }

        public BNBInteropSettings BNBSettings { get; set; }

        /// <summary> If this value is set, enable the override originator logic.</summary>
        public bool OverrideOriginatorEnabled { get; set; }

        /// <summary> If this value is set, override this node as the originator.</summary>
        public bool OverrideOriginator { get; set; }

        /// <summary>This is the URL of the Cirrus node's API, for actioning SRC20 contract calls.</summary>
        public string CirrusClientUrl { get; set; }

        public string CirrusSmartContractActiveAddress { get; set; }

        public string CirrusMultisigContractAddress { get; set; }

        public WalletCredentials WalletCredentials { get; set; }

        public InteropSettings(NodeSettings nodeSettings)
        {
            this.ETHSettings = new ETHInteropSettings(nodeSettings);
            this.BNBSettings = new BNBInteropSettings(nodeSettings);

            this.OverrideOriginatorEnabled = nodeSettings.ConfigReader.GetOrDefault("overrideoriginatorenabled", false);
            this.OverrideOriginator = nodeSettings.ConfigReader.GetOrDefault("overrideoriginator", false);
            this.CirrusClientUrl = nodeSettings.ConfigReader.GetOrDefault("cirrusclienturl", nodeSettings.Network.IsTest() ? "http://localhost:38223" : "http://localhost:37223");
            this.CirrusSmartContractActiveAddress = nodeSettings.ConfigReader.GetOrDefault<string>("cirrussmartcontractactiveaddress", null);
            this.CirrusMultisigContractAddress = nodeSettings.ConfigReader.GetOrDefault<string>("cirrusmultisigcontractaddress", null);
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

        /// <summary>The amount of nodes that needs to agree on conversion transaction before it is released.</summary>
        public int MultisigWalletQuorum { get; set; }
        private const string MultisigWalletContractQuorumKey = "ethereummultisigwalletquorum";

        /// <summary>This should be set to the address of the multisig wallet contract deployed on the Ethereum blockchain.</summary>
        public string MultisigWalletAddress { get; set; }

        /// <summary>This should be set to the address of the Wrapped STRAX ERC-20 contract deployed on the Ethereum blockchain.</summary>
        public string WrappedStraxContractAddress { get; set; }

        /// <summary>This should be set to the address of the Key Value Store contract deployed on the Ethereum blockchain.</summary>
        public string KeyValueStoreContractAddress { get; set; }
        
        /// <summary>This is the RPC address of the geth node running on the local machine. It is normally defaulted to http://localhost:8545</summary>
        public string ClientUrl { get; set; }

        /// <summary>
        /// Address of the account on your geth node. It is the account that will be used for transaction
        /// signing and all interactions with the multisig and wrapped STRAX contracts.
        /// </summary>
        public string Account { get; set; }

        /// <summary>Passphrase for the Ethereum account.</summary>
        public string Passphrase { get; set; }

        /// <summary>The gas limit for Ethereum interoperability transactions.</summary>
        public int GasLimit { get; set; }

        /// <summary>The gas price for Ethereum interoperability transactions (denominated in gwei).</summary>
        public int GasPrice { get; set; }

        /// <summary>A collection of contract addresses for ERC20 tokens that should be monitored for Transfer events
        /// against the federation multisig wallet. These are mapped to their corresponding SRC20 contract.</summary>
        public Dictionary<string, string> WatchedErc20Contracts { get; set; }

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
            string keyValueStoreContractAddressKey = this.GetSettingsPrefix() + "keyvaluestorecontractaddress";

            this.InteropContractCirrusAddress = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "interopcontractcirrusaddress", "");
            this.InteropContractAddress = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "interopcontractaddress", "");
            this.WatchedErc20Contracts = new Dictionary<string, string>();

            string watchErc20Key = this.GetSettingsPrefix() + "watcherc20";

            foreach (string watched in nodeSettings.ConfigReader.GetAll(watchErc20Key))
            {
                if (!watched.Contains(","))
                {
                    throw new Exception($"Value of -{watchErc20Key} invalid, should be -{watchErc20Key}=<ERC20address>,<SRC20address>: {watched}");
                }

                string[] splitWatched = watched.Split(",");

                if (splitWatched.Length != 2)
                {
                    throw new Exception($"Value of -{watchErc20Key} invalid, should be -{watchErc20Key}=<ERC20address>,<SRC20address>: {watched}");
                }

                // Ensure that a valid Cirrus address was provided.
                BitcoinAddress.Create(splitWatched[1], nodeSettings.Network);

                this.WatchedErc20Contracts[splitWatched[0]] = splitWatched[1];
            }

            this.MultisigWalletQuorum = nodeSettings.ConfigReader.GetOrDefault(MultisigWalletContractQuorumKey, 6);
            this.MultisigWalletAddress = nodeSettings.ConfigReader.GetOrDefault(multisigWalletContractAddressKey, "");
            this.WrappedStraxContractAddress = nodeSettings.ConfigReader.GetOrDefault(wrappedStraxContractAddressKey, "");
            this.KeyValueStoreContractAddress = nodeSettings.ConfigReader.GetOrDefault(keyValueStoreContractAddressKey, "");
            this.ClientUrl = nodeSettings.ConfigReader.GetOrDefault(clientUrlKey, "http://localhost:8545");
            this.Account = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "account", "");
            this.Passphrase = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "passphrase", "");

            this.GasLimit = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "gas", 3_000_000);
            this.GasPrice = nodeSettings.ConfigReader.GetOrDefault(this.GetSettingsPrefix() + "gasprice", 100);

            if (string.IsNullOrWhiteSpace(this.MultisigWalletAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{multisigWalletContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.WrappedStraxContractAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{wrappedStraxContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.KeyValueStoreContractAddress))
                throw new Exception($"Cannot initialize interoperability feature without -{keyValueStoreContractAddressKey} specified.");

            if (string.IsNullOrWhiteSpace(this.ClientUrl))
                throw new Exception($"Cannot initialize interoperability feature without -{clientUrlKey} specified.");
        }

        /// <summary>Prefix that determines which chain the setting are for.</summary>
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
