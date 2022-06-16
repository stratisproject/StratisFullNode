using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Contracts.ContractHandlers;
using Nethereum.Contracts.CQS;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    public class KVStoreDeployment : ContractDeploymentMessage
    {
        public static string BYTECODE =
            "0x608060405234801561001057600080fd5b506105e0806100206000396000f3fe608060405234801561001057600080fd5b50600436106100365760003560e01c8063e942b5161461003b578063fc2525ab1461018d575b600080fd5b61018b6004803603604081101561005157600080fd5b810190808035906020019064010000000081111561006e57600080fd5b82018360208201111561008057600080fd5b803590602001918460018302840111640100000000831117156100a257600080fd5b91908080601f016020809104026020016040519081016040528093929190818152602001838380828437600081840152601f19601f8201169050808301925050505050505091929192908035906020019064010000000081111561010557600080fd5b82018360208201111561011757600080fd5b8035906020019184600183028401116401000000008311171561013957600080fd5b91908080601f016020809104026020016040519081016040528093929190818152602001838380828437600081840152601f19601f8201169050808301925050505050505091929192905050506102e1565b005b610266600480360360408110156101a357600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190803590602001906401000000008111156101e057600080fd5b8201836020820111156101f257600080fd5b8035906020019184600183028401116401000000008311171561021457600080fd5b91908080601f016020809104026020016040519081016040528093929190818152602001838380828437600081840152601f19601f8201169050808301925050505050505091929192905050506103be565b6040518080602001828103825283818151815260200191508051906020019080838360005b838110156102a657808201518184015260208101905061028b565b50505050905090810190601f1680156102d35780820380516001836020036101000a031916815260200191505b509250505060405180910390f35b6103e88251111580156102f757506103e8815111155b61030057600080fd5b806000803373ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020836040518082805190602001908083835b602083106103735780518252602082019150602081019050602083039250610350565b6001836020036101000a038019825116818451168082178552505050505050905001915050908152602001604051809103902090805190602001906103b9929190610506565b505050565b60606000808473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020826040518082805190602001908083835b60208310610432578051825260208201915060208101905060208303925061040f565b6001836020036101000a03801982511681845116808217855250505050505090500191505090815260200160405180910390208054600181600116156101000203166002900480601f0160208091040260200160405190810160405280929190818152602001828054600181600116156101000203166002900480156104f95780601f106104ce576101008083540402835291602001916104f9565b820191906000526020600020905b8154815290600101906020018083116104dc57829003601f168201915b5050505050905092915050565b828054600181600116156101000203166002900490600052602060002090601f016020900481019282601f1061054757805160ff1916838001178555610575565b82800160010185558215610575579182015b82811115610574578251825591602001919060010190610559565b5b5090506105829190610586565b5090565b6105a891905b808211156105a457600081600090555060010161058c565b5090565b9056fea265627a7a7231582011f7d7f06f9b40e37fba1d2867778bdc239953b83de9ecb6fbfedb185bf0550c64736f6c63430005110032";

        public KVStoreDeployment() : base(BYTECODE)
        {
        }
    }

    [Function("get", "string")]
    public class GetFunction : FunctionMessage
    {
        [Parameter("address", "_address", 1)]
        public string Address { get; set; }
        
        [Parameter("string", "_key", 2)]
        public string Key { get; set; }
    }

    [Function("set")]
    public class SetFunction : FunctionMessage
    {
        [Parameter("string", "_key", 1)]
        public string Key { get; set; }

        [Parameter("string", "_value", 2)]
        public string Value { get; set; }
    }
    
    public class KVStore
    {
        public static async Task<string> DeployContractAsync(Web3 web3)
        {
            var deploymentMessage = new KVStoreDeployment()
            {
            };

            IContractDeploymentTransactionHandler<KVStoreDeployment> deploymentHandler = web3.Eth.GetContractDeploymentHandler<KVStoreDeployment>();
            TransactionReceipt transactionReceiptDeployment = await deploymentHandler.SendRequestAndWaitForReceiptAsync(deploymentMessage).ConfigureAwait(false);
            string contractAddress = transactionReceiptDeployment.ContractAddress;

            return contractAddress;
        }

        public static async Task<string> GetAsync(Web3 web3, string contractAddress, string address, string key)
        {
            var getFunctionMessage = new GetFunction()
            {
                Address = address,
                Key = key
            };

            IContractQueryHandler<GetFunction> getHandler = web3.Eth.GetContractQueryHandler<GetFunction>();
            string value = await getHandler.QueryAsync<string>(contractAddress, getFunctionMessage).ConfigureAwait(false);

            return value;
        }

        public static async Task<string> SetAsync(Web3 web3, string contractAddress, string key, string value, BigInteger gas, BigInteger gasPrice)
        {
            IContractTransactionHandler<SetFunction> transferHandler = web3.Eth.GetContractTransactionHandler<SetFunction>();

            var setFunction = new SetFunction()
            {
                Key = key,
                Value = value,
                Gas = gas,
                GasPrice = Web3.Convert.ToWei(gasPrice, UnitConversion.EthUnit.Gwei)
            };

            TransactionReceipt transactionTransferReceipt = await transferHandler.SendRequestAndWaitForReceiptAsync(contractAddress, setFunction).ConfigureAwait(false);

            return transactionTransferReceipt.TransactionHash;
        }

        public static string ABI = @"[
			{
				""constant"": true,
				""inputs"": [
					{
						""internalType"": ""address"",
						""name"": ""_account"",
						""type"": ""address""
					},
					{
						""internalType"": ""string"",
						""name"": ""_key"",
						""type"": ""string""
					}
				],
				""name"": ""get"",
				""outputs"": [
					{
						""internalType"": ""string"",
						""name"": """",
						""type"": ""string""
					}
				],
				""payable"": false,
				""stateMutability"": ""view"",
				""type"": ""function""
			},
			{
				""constant"": false,
				""inputs"": [
					{
						""internalType"": ""string"",
						""name"": ""_key"",
						""type"": ""string""
					},
					{
						""internalType"": ""string"",
						""name"": ""_value"",
						""type"": ""string""
					}
				],
				""name"": ""set"",
				""outputs"": [],
				""payable"": false,
				""stateMutability"": ""nonpayable"",
				""type"": ""function""
			}
		]";
    }
}
