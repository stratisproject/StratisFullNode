using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Swagger;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.Core.State;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Controllers
{
    /// <summary>
    /// Controller for dynamically generating swagger documents for smart contract assemblies.
    /// </summary>
    [Route("api/swagger/contracts")]
    public class ContractSwaggerController : Controller
    {
        private readonly SwaggerGeneratorOptions options;
        private readonly ILoader loader;
        private readonly IWalletManager walletmanager;
        private readonly IStateRepositoryRoot stateRepository;
        private readonly Network network;
        private readonly SwaggerUIOptions uiOptions;

        public ContractSwaggerController(
            SwaggerGeneratorOptions options,
            SwaggerUIOptions uiOptions,
            ILoader loader,
            IWalletManager walletmanager,
            IStateRepositoryRoot stateRepository,
            Network network)
        {
            this.options = options;
            this.uiOptions = uiOptions;
            this.loader = loader;
            this.walletmanager = walletmanager;
            this.stateRepository = stateRepository;
            this.network = network;
        }

        /// <summary>
        /// Dynamically generates a swagger document for the contract at the given address.
        /// </summary>
        /// <param name="address">The contract's address.</param>
        /// <returns>A <see cref="SwaggerDocument"/> model.</returns>
        /// <exception cref="Exception"></exception>
        [Route("{address}")]
        [HttpGet]
        public async Task<IActionResult> ContractSwaggerDoc(string address)
        {
            var code = this.stateRepository.GetCode(address.ToUint160(this.network));

            if (code == null)
                throw new Exception("Contract does not exist");

            Result<IContractAssembly> assemblyLoadResult = this.loader.Load((ContractByteCode)code);

            if (assemblyLoadResult.IsFailure)
                throw new Exception("Error loading assembly");

            IContractAssembly assembly = assemblyLoadResult.Value;

            // Default wallet is the first wallet as ordered by name.
            string defaultWalletName = this.walletmanager.GetWalletsNames().OrderBy(n => n).First();

            // Default address is the first address with a balance, or string.Empty if no addresses have been created.
            // Ordering this way is consistent with the wallet UI, ie. whatever appears first in the wallet will appear first here.
            string defaultAddress = this.walletmanager.GetAccountAddressesWithBalance(defaultWalletName).FirstOrDefault()?.Address ?? string.Empty;

            var swaggerGen = new ContractSwaggerDocGenerator(this.options, address, assembly, defaultWalletName, defaultAddress);

            OpenApiDocument doc = swaggerGen.GetSwagger("contracts");

            // TODO confirm v2/v3
            var outputString = doc.Serialize(OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Json);

            return Ok(outputString);
        }

        /// <summary>
        /// Add the contract address to the Swagger dropdown
        /// </summary>
        /// <param name="address">The contract's address.</param>
        /// <returns>A success response.</returns>
        [HttpPost]
        public async Task<IActionResult> AddContractToSwagger([FromBody] AddContractRequest address)
        {
            // Check that the contract exists
            var code = this.stateRepository.GetCode(address.Address.ToUint160(this.network));

            if (code == null)
                throw new Exception("Contract does not exist");

            var newUrls = new List<UrlDescriptor>(this.uiOptions.ConfigObject.Urls)
            {
                new UrlDescriptor
                {
                    Name = $"Contract {address}",
                    Url = $"/swagger/contracts/{address}"
                }
            };

            this.uiOptions.ConfigObject.Urls = newUrls;

            return Ok();
        }

        public class AddContractRequest
        {
            /// <summary>
            /// The contract address to add.
            /// </summary>
            public string Address { get; set; }
        }
    }
}
