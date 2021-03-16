using Microsoft.Extensions.DependencyInjection;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.Bitcoin.Features.SmartContracts.PoA.Rules;
using Stratis.Bitcoin.Features.SmartContracts.Rules;

namespace Stratis.Bitcoin.Features.SmartContracts.PoA
{
    public static class WhitelistedContractExtensions
    {
        /// <summary>
        /// Adds a consensus rule ensuring only contracts with hashes that are on the PoA whitelist are able to be deployed.
        /// The PoA feature must be installed for this to function correctly.
        /// </summary>
        /// <param name="options">The smart contract options.</param>
        /// <param name="devMode">Disables whitelisting and contract hash checking when the node is in dev mode.</param>
        /// <returns>The options provided.</returns>
        public static SmartContractOptions UsePoAWhitelistedContracts(this SmartContractOptions options, bool devMode = false)
        {
            IServiceCollection services = options.Services;

            services.AddSingleton<IContractCodeHashingStrategy, Keccak256CodeHashingStrategy>();

            if (!devMode)
            {
                // These may have been registered by UsePoAMempoolRules already.
                services.AddSingleton<IWhitelistedHashChecker, WhitelistedHashChecker>();

                // Registers an additional contract tx validation consensus rule which checks whether the hash of the contract being deployed is whitelisted.
                services.AddSingleton<IContractTransactionFullValidationRule, AllowedCodeHashLogic>();
            }

            return options;
        }
    }
}