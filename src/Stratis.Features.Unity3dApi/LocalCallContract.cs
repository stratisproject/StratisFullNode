using System.Collections.Generic;
using System;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Local;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts;

namespace Stratis.Features.Unity3dApi
{
    // TODO: Move this to a more central point once 1.5.0.0 stabilises
    public interface ILocalCallContract
    {
        LocalExecutionResponse LocalCallSmartContract(LocalCallContractRequest request);
        T LocalCallSmartContract<T>(ulong? blockHeight, string sender, string contractAddress, string methodName, params object[] arguments);
    }

    public class LocalCallContract : ILocalCallContract
    {
        private readonly Network network;
        private readonly ChainIndexer chainIndexer;
        private readonly ISmartContractTransactionService smartContractTransactionService;
        private readonly ILocalExecutor localExecutor;
        private readonly IContractPrimitiveSerializer primitiveSerializer;
        private readonly IContractAssemblyCache contractAssemblyCache;

        public LocalCallContract(Network network, ISmartContractTransactionService smartContractTransactionService, ChainIndexer chainIndexer, ILocalExecutor localExecutor)
        {
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.smartContractTransactionService = smartContractTransactionService;
            this.localExecutor = localExecutor;
        }

        public LocalExecutionResponse LocalCallSmartContract(LocalCallContractRequest request)
        {
            ContractTxData txData = this.smartContractTransactionService.BuildLocalCallTxData(request);

            var height = request.BlockHeight ?? (ulong)this.chainIndexer.Height;

            ILocalExecutionResult result = this.localExecutor.Execute(
                height,
                request.Sender?.ToUint160(this.network) ?? new uint160(),
                !string.IsNullOrWhiteSpace(request.Amount) ? (Money)request.Amount : 0,
                txData);

            var deserializer = new ApiLogDeserializer(this.primitiveSerializer, this.network, result.StateRoot, this.contractAssemblyCache);

            var response = new LocalExecutionResponse
            {
                InternalTransfers = deserializer.MapTransferInfo(result.InternalTransfers.ToArray()),
                Logs = deserializer.MapLogResponses(result.Logs.ToArray()),
                GasConsumed = result.GasConsumed,
                Revert = result.Revert,
                ErrorMessage = result.ErrorMessage,
                Return = result.Return // All return values should be primitives, let default serializer handle.
            };

            return response;
        }

        private IEnumerable<string> EncodeParameters(params object[] arguments)
        {
            foreach (var parameter in arguments)
            {
                switch (parameter)
                {
                    case bool boolVal:
                        yield return $"1#{boolVal}";
                        break;

                    case byte byteVal:
                        yield return $"2#{byteVal}";
                        break;

                    case char charVal:
                        yield return $"3#{charVal}";
                        break;

                    case string stringVal:
                        yield return $"4#{stringVal}";
                        break;

                    case uint uint32:
                        yield return $"5#{uint32}";
                        break;

                    case int int32:
                        yield return $"6#{int32}";
                        break;

                    case ulong uint64:
                        yield return $"7#{uint64}";
                        break;

                    case long int64:
                        yield return $"8#{int64}";
                        break;

                    case Address address:
                        yield return $"9#{address}";
                        break;

                    case byte[] byteArr:
                        yield return $"10#{BitConverter.ToString(byteArr).Replace("-", "")}";
                        break;

                    case UInt128 uInt128:
                        yield return $"11#{uInt128}";
                        break;

                    case UInt256 uInt256:
                        yield return $"12#{uInt256}";
                        break;

                    default:
                        throw new Exception($"Currently unsupported argument type: '{parameter.GetType().Name}'");
                }
            }
        }

        public T LocalCallSmartContract<T>(ulong? blockHeight, string sender, string contractAddress, string methodName, params object[] arguments)
        {
            var request = new LocalCallContractRequest
            {
                BlockHeight = blockHeight,
                Amount = "0",
                ContractAddress = contractAddress,
                GasLimit = 250_000,
                GasPrice = 100,
                MethodName = methodName,
                Parameters = EncodeParameters(arguments).ToArray(),
                Sender = sender
            };

            try
            {
                LocalExecutionResponse result = this.LocalCallSmartContract(request);

                if (result.Return == null)
                    return default(T);

                return (T)result.Return;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
