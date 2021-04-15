using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NBitcoin;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Controllers
{
    /// <summary>
    /// Controller for dynamically generating swagger documents for smart contract assemblies.
    /// </summary>
    [Route("swagger/contracts")]
    public class ContractSwaggerController
    {
        private readonly ILoader loader;
        private readonly IWalletManager walletmanager;
        private readonly IStateRepositoryRoot stateRepository;
        private readonly Network network;

        public ContractSwaggerController(
            ILoader loader,
            IWalletManager walletmanager,
            IStateRepositoryRoot stateRepository,
            Network network)
        {
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
            return new BadRequestResult();
        }
    }
}
