using Microsoft.Extensions.DependencyInjection;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.Bitcoin.Features.SmartContracts.PoS.Rules;
using Stratis.Bitcoin.Features.SmartContracts.Rules;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS
{
    public static class PoSWhitelistedContractExtensions
    {
        /// <summary>
        /// Adds a consensus rule ensuring only contracts with hashes that are on the PoA whitelist are able to be deployed.
        /// The PoA feature must be installed for this to function correctly.
        /// </summary>
        /// <param name="options">The smart contract options.</param>
        /// <returns>The options provided.</returns>
        public static SmartContractOptions UsePoSWhitelistedContracts(this SmartContractOptions options)
        {
            IServiceCollection services = options.Services;

            // These may have been registered by UsePoAMempoolRules already.
            services.AddSingleton<IContractCodeHashingStrategy, Keccak256CodeHashingStrategy>();

            // Registers an additional contract tx validation consensus rule which checks whether the hash of the contract being deployed is whitelisted.
            services.AddSingleton<IWhitelistedHashChecker, PoSWhitelistedHashChecker>();
            services.AddSingleton<IContractTransactionFullValidationRule, PoSAllowedCodeHashLogic>();

            return options;
        }
    }
}