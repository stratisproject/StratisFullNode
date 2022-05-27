using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Controllers
{
    /// <summary>
    /// Execute dynamically generated smart contract calls
    /// </summary>
    public class DynamicContractController : Controller
    {
        private readonly SmartContractWalletController smartContractWalletController;
        private readonly SmartContractsController localCallController;
        private readonly IStateRepositoryRoot stateRoot;
        private readonly ILoader loader;
        private readonly Network network;

        /// <summary>
        /// Creates a new DynamicContractController instance.
        /// </summary>
        /// <param name="smartContractWalletController"></param>
        /// <param name="localCallController"></param>
        /// <param name="stateRoot"></param>
        /// <param name="loader"></param>
        /// <param name="network"></param>
        public DynamicContractController(
            SmartContractWalletController smartContractWalletController,
            SmartContractsController localCallController,
            IStateRepositoryRoot stateRoot,
            ILoader loader,
            Network network)
        {
            this.smartContractWalletController = smartContractWalletController;
            this.localCallController = localCallController;
            this.stateRoot = stateRoot;
            this.loader = loader;
            this.network = network;
        }

        /// <summary>
        /// Call a method on the contract by broadcasting a call transaction to the network.
        /// </summary>
        /// <param name="address">The address of the contract to call.</param>
        /// <param name="method">The name of the method on the contract being called.</param>
        /// <returns>A model of the transaction data, if created and broadcast successfully.</returns>
        /// <response code="200">Returns build transaction response</response>
        /// <response code="400">Invalid request, failed to build transaction, or could not broadcast transaction</response>
        /// <response code="500">Method does not exist, parameters do not match signature, or something unexpected happened</response>
        [Route("api/contract/{address}/method/{method}")]
        [HttpPost]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(typeof(BuildCallContractTransactionResponse), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorResponse), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public Task<IActionResult> CallMethod([FromRoute] string address, [FromRoute] string method, [FromBody] JObject requestData)
        {
            var contractCode = this.stateRoot.GetCode(address.ToUint160(this.network));

            Result<IContractAssembly> loadResult = this.loader.Load((ContractByteCode)contractCode);

            IContractAssembly assembly = loadResult.Value;

            Type type = assembly.DeployedType;

            MethodInfo methodInfo = type.GetMethod(method);

            if (methodInfo == null)
                throw new Exception("Method does not exist on contract.");

            ParameterInfo[] parameters = methodInfo.GetParameters();

            if (!this.ValidateParams(requestData, parameters))
                throw new Exception("Parameters don't match method signature.");

            // Map the JObject to the parameter + types expected by the call.
            string[] methodParams = parameters.Map(requestData);

            BuildCallContractTransactionRequest request = this.MapCallRequest(address, method, methodParams, this.Request.Headers);

            // Proxy to the actual SC controller.
            return Task.FromResult<IActionResult>(this.smartContractWalletController.Call(request));
        }

        /// <summary>
        /// Query the value of a property on the contract using a local call.
        /// </summary>
        /// <param name="address">The address of the contract to query.</param>
        /// <param name="property">The name of the property to query.</param>
        /// <returns>A model of the query result.</returns>
        /// <response code="200">Returns call response</response>
        /// <response code="400">Invalid request</response>
        /// <response code="500">Unable to deserialize method parameters</response>
        [Route("api/contract/{address}/property/{property}")]
        [HttpGet]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(typeof(LocalExecutionResponse), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorResponse), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult LocalCallProperty([FromRoute] string address, [FromRoute] string property)
        {
            LocalCallContractRequest request = this.MapLocalCallRequest(address, property, this.Request.Headers);

            // Proxy to the actual SC controller.
            return this.localCallController.LocalCallSmartContractTransaction(request);
        }

        private bool ValidateParams(JObject requestData, ParameterInfo[] parameters)
        {
            foreach (ParameterInfo param in parameters)
            {
                if (requestData[param.Name] == null)
                    return false;
            }

            return true;
        }

        private BuildCallContractTransactionRequest MapCallRequest(string address, string method, string[] parameters, IHeaderDictionary headers)
        {
            var call = new BuildCallContractTransactionRequest
            {
                GasPrice = ulong.Parse(headers["GasPrice"]),
                GasLimit = ulong.Parse(headers["GasLimit"]),
                Amount = headers["Amount"],
                FeeAmount = headers["FeeAmount"],
                WalletName = headers["WalletName"],
                Password = headers["WalletPassword"],
                Sender = headers["Sender"],
                AccountName = "account 0",
                ContractAddress = address,
                MethodName = method,
                Parameters = parameters,
                Outpoints = new List<OutpointRequest>()
            };

            return call;
        }

        private LocalCallContractRequest MapLocalCallRequest(string address, string property, IHeaderDictionary headers)
        {
            return new LocalCallContractRequest
            {
                GasPrice = ulong.Parse(headers["GasPrice"]),
                GasLimit = ulong.Parse(headers["GasLimit"]),
                Amount = headers["Amount"],
                Sender = headers["Sender"],
                ContractAddress = address,
                MethodName = property
            };
        }
    }
}