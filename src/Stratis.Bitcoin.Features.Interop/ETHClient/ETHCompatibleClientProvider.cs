using System;
using System.Collections.Generic;
using System.Linq;
using Stratis.Bitcoin.Features.Wallet;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    public interface IETHCompatibleClientProvider
    {
        /// <summary>Provides client for specified chain type.</summary>
        IETHClient GetClientForChain(DestinationChain chain);

        /// <summary>Provides collection of clients for all supported chains.</summary>
        Dictionary<DestinationChain, IETHClient> GetAllSupportedChains(bool excludeDisabled = true);

        /// <summary>Returns <c>true</c> if specified chain is supported, <c>false</c> otherwise.</summary>
        bool IsChainSupported(DestinationChain chain);

        /// <summary>Returns <c>true</c> if specified chain is supported and interop is enabled, <c>false</c> otherwise.</summary>
        bool IsChainSupportedAndEnabled(DestinationChain chain);
    }

    public class ETHCompatibleClientProvider : IETHCompatibleClientProvider
    {
        private readonly Dictionary<DestinationChain, IETHClient> supportedChains;

        private readonly InteropSettings interopSettings;

        public ETHCompatibleClientProvider(IETHClient ethClient, IBNBClient bnbClient, InteropSettings interopSettings)
        {
            this.supportedChains = new Dictionary<DestinationChain, IETHClient>()
            {
                { DestinationChain.ETH, ethClient },
                { DestinationChain.BNB, bnbClient },
            };

            this.interopSettings = interopSettings;
        }

        /// <inheritdoc />
        public IETHClient GetClientForChain(DestinationChain chain)
        {
            if (chain == DestinationChain.CIRRUS)
                return null;

            if (!this.supportedChains.ContainsKey(chain))
                throw new NotImplementedException("Provided chain type not supported: " + chain);

            return this.supportedChains[chain];
        }

        /// <inheritdoc />
        public Dictionary<DestinationChain, IETHClient> GetAllSupportedChains(bool excludeDisabled = true)
        {
            if (!excludeDisabled)
                return this.supportedChains;

            return this.supportedChains.Where(x => this.interopSettings.GetSettingsByChain(x.Key).InteropEnabled).ToDictionary(x => x.Key, x => x.Value);
        }

        /// <inheritdoc />
        public bool IsChainSupported(DestinationChain chain)
        {
            return this.supportedChains.ContainsKey(chain);
        }

        /// <inheritdoc />
        public bool IsChainSupportedAndEnabled(DestinationChain chain)
        {
            bool supported = this.IsChainSupported(chain);

            if (!supported)
                return false;

            return this.interopSettings.GetSettingsByChain(chain).InteropEnabled;
        }
    }
}