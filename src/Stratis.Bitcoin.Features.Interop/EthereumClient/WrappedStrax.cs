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

namespace Stratis.Bitcoin.Features.Interop.EthereumClient
{
	public class WrappedStraxTokenDeployment : ContractDeploymentMessage
	{
		public static string BYTECODE =
			"0x60806040523480156200001157600080fd5b506040516200243138038062002431833981810160405260208110156200003757600080fd5b81019080805190602001909291905050506040518060400160405280600c81526020017f57726170706564537472617800000000000000000000000000000000000000008152506040518060400160405280600681526020017f57535452415800000000000000000000000000000000000000000000000000008152508160039080519060200190620000cc92919062000442565b508060049080519060200190620000e592919062000442565b506012600560006101000a81548160ff021916908360ff1602179055505050600062000116620001ce60201b60201c565b905080600560016101000a81548173ffffffffffffffffffffffffffffffffffffffff021916908373ffffffffffffffffffffffffffffffffffffffff1602179055508073ffffffffffffffffffffffffffffffffffffffff16600073ffffffffffffffffffffffffffffffffffffffff167f8be0079c531659141344cd1fd0a4f28419497f9722a3daafe3b4186f6b6457e060405160405180910390a350620001c73382620001d660201b60201c565b50620004e8565b600033905090565b600073ffffffffffffffffffffffffffffffffffffffff168273ffffffffffffffffffffffffffffffffffffffff1614156200027a576040517f08c379a000000000000000000000000000000000000000000000000000000000815260040180806020018281038252601f8152602001807f45524332303a206d696e7420746f20746865207a65726f20616464726573730081525060200191505060405180910390fd5b6200028e60008383620003b460201b60201c565b620002aa81600254620003b960201b620012f61790919060201c565b60028190555062000308816000808573ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002054620003b960201b620012f61790919060201c565b6000808473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020819055508173ffffffffffffffffffffffffffffffffffffffff16600073ffffffffffffffffffffffffffffffffffffffff167fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef836040518082815260200191505060405180910390a35050565b505050565b60008082840190508381101562000438576040517f08c379a000000000000000000000000000000000000000000000000000000000815260040180806020018281038252601b8152602001807f536166654d6174683a206164646974696f6e206f766572666c6f77000000000081525060200191505060405180910390fd5b8091505092915050565b828054600181600116156101000203166002900490600052602060002090601f016020900481019282601f106200048557805160ff1916838001178555620004b6565b82800160010185558215620004b6579182015b82811115620004b557825182559160200191906001019062000498565b5b509050620004c59190620004c9565b5090565b5b80821115620004e4576000816000905550600101620004ca565b5090565b611f3980620004f86000396000f3fe608060405234801561001057600080fd5b50600436106101215760003560e01c806370a08231116100ad57806395d89b411161007157806395d89b411461060c578063a457c2d71461068f578063a9059cbb146106f3578063dd62ed3e14610757578063f2fde38b146107cf57610121565b806370a082311461046b578063715018a6146104c357806379cc6790146104cd5780637e0518671461051b5780638da5cb5b146105d857610121565b8063313ce567116100f4578063313ce567146102af57806339509351146102d05780633e572efc1461033457806340c10f19146103ef57806342966c681461043d57610121565b806306fdde0314610126578063095ea7b3146101a957806318160ddd1461020d57806323b872dd1461022b575b600080fd5b61012e610813565b6040518080602001828103825283818151815260200191508051906020019080838360005b8381101561016e578082015181840152602081019050610153565b50505050905090810190601f16801561019b5780820380516001836020036101000a031916815260200191505b509250505060405180910390f35b6101f5600480360360408110156101bf57600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190803590602001909291905050506108b5565b60405180821515815260200191505060405180910390f35b6102156108d3565b6040518082815260200191505060405180910390f35b6102976004803603606081101561024157600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190803573ffffffffffffffffffffffffffffffffffffffff169060200190929190803590602001909291905050506108dd565b60405180821515815260200191505060405180910390f35b6102b76109b6565b604051808260ff16815260200191505060405180910390f35b61031c600480360360408110156102e657600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190803590602001909291905050506109cd565b60405180821515815260200191505060405180910390f35b6103ed6004803603602081101561034a57600080fd5b810190808035906020019064010000000081111561036757600080fd5b82018360208201111561037957600080fd5b8035906020019184600183028401116401000000008311171561039b57600080fd5b91908080601f016020809104026020016040519081016040528093929190818152602001838380828437600081840152601f19601f820116905080830192505050505050509192919290505050610a80565b005b61043b6004803603604081101561040557600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff16906020019092919080359060200190929190505050610ad7565b005b6104696004803603602081101561045357600080fd5b8101908080359060200190929190505050610baf565b005b6104ad6004803603602081101561048157600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190505050610bc3565b6040518082815260200191505060405180910390f35b6104cb610c0b565b005b610519600480360360408110156104e357600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff16906020019092919080359060200190929190505050610d96565b005b61055d6004803603602081101561053157600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190505050610df8565b6040518080602001828103825283818151815260200191508051906020019080838360005b8381101561059d578082015181840152602081019050610582565b50505050905090810190601f1680156105ca5780820380516001836020036101000a031916815260200191505b509250505060405180910390f35b6105e0610ea8565b604051808273ffffffffffffffffffffffffffffffffffffffff16815260200191505060405180910390f35b610614610ed2565b6040518080602001828103825283818151815260200191508051906020019080838360005b83811015610654578082015181840152602081019050610639565b50505050905090810190601f1680156106815780820380516001836020036101000a031916815260200191505b509250505060405180910390f35b6106db600480360360408110156106a557600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff16906020019092919080359060200190929190505050610f74565b60405180821515815260200191505060405180910390f35b61073f6004803603604081101561070957600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff16906020019092919080359060200190929190505050611041565b60405180821515815260200191505060405180910390f35b6107b96004803603604081101561076d57600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190803573ffffffffffffffffffffffffffffffffffffffff16906020019092919050505061105f565b6040518082815260200191505060405180910390f35b610811600480360360208110156107e557600080fd5b81019080803573ffffffffffffffffffffffffffffffffffffffff1690602001909291905050506110e6565b005b606060038054600181600116156101000203166002900480601f0160208091040260200160405190810160405280929190818152602001828054600181600116156101000203166002900480156108ab5780601f10610880576101008083540402835291602001916108ab565b820191906000526020600020905b81548152906001019060200180831161088e57829003601f168201915b5050505050905090565b60006108c96108c261137e565b8484611386565b6001905092915050565b6000600254905090565b60006108ea84848461157d565b6109ab846108f661137e565b6109a685604051806060016040528060288152602001611e2960289139600160008b73ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020600061095c61137e565b73ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1681526020019081526020016000205461183e9092919063ffffffff16565b611386565b600190509392505050565b6000600560009054906101000a900460ff16905090565b6000610a766109da61137e565b84610a7185600160006109eb61137e565b73ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060008973ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020546112f690919063ffffffff16565b611386565b6001905092915050565b80600660003373ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1681526020019081526020016000209080519060200190610ad3929190611cd8565b5050565b610adf61137e565b73ffffffffffffffffffffffffffffffffffffffff16600560019054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1614610ba1576040517f08c379a00000000000000000000000000000000000000000000000000000000081526004018080602001828103825260208152602001807f4f776e61626c653a2063616c6c6572206973206e6f7420746865206f776e657281525060200191505060405180910390fd5b610bab82826118fe565b5050565b610bc0610bba61137e565b82611ac5565b50565b60008060008373ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020549050919050565b610c1361137e565b73ffffffffffffffffffffffffffffffffffffffff16600560019054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1614610cd5576040517f08c379a00000000000000000000000000000000000000000000000000000000081526004018080602001828103825260208152602001807f4f776e61626c653a2063616c6c6572206973206e6f7420746865206f776e657281525060200191505060405180910390fd5b600073ffffffffffffffffffffffffffffffffffffffff16600560019054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff167f8be0079c531659141344cd1fd0a4f28419497f9722a3daafe3b4186f6b6457e060405160405180910390a36000600560016101000a81548173ffffffffffffffffffffffffffffffffffffffff021916908373ffffffffffffffffffffffffffffffffffffffff160217905550565b6000610dd582604051806060016040528060248152602001611e5160249139610dc686610dc161137e565b61105f565b61183e9092919063ffffffff16565b9050610de983610de361137e565b83611386565b610df38383611ac5565b505050565b60066020528060005260406000206000915090508054600181600116156101000203166002900480601f016020809104026020016040519081016040528092919081815260200182805460018160011615610100020316600290048015610ea05780601f10610e7557610100808354040283529160200191610ea0565b820191906000526020600020905b815481529060010190602001808311610e8357829003601f168201915b505050505081565b6000600560019054906101000a900473ffffffffffffffffffffffffffffffffffffffff16905090565b606060048054600181600116156101000203166002900480601f016020809104026020016040519081016040528092919081815260200182805460018160011615610100020316600290048015610f6a5780601f10610f3f57610100808354040283529160200191610f6a565b820191906000526020600020905b815481529060010190602001808311610f4d57829003601f168201915b5050505050905090565b6000611037610f8161137e565b8461103285604051806060016040528060258152602001611edf6025913960016000610fab61137e565b73ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060008a73ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1681526020019081526020016000205461183e9092919063ffffffff16565b611386565b6001905092915050565b600061105561104e61137e565b848461157d565b6001905092915050565b6000600160008473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060008373ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002054905092915050565b6110ee61137e565b73ffffffffffffffffffffffffffffffffffffffff16600560019054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16146111b0576040517f08c379a00000000000000000000000000000000000000000000000000000000081526004018080602001828103825260208152602001807f4f776e61626c653a2063616c6c6572206973206e6f7420746865206f776e657281525060200191505060405180910390fd5b600073ffffffffffffffffffffffffffffffffffffffff168173ffffffffffffffffffffffffffffffffffffffff161415611236576040517f08c379a0000000000000000000000000000000000000000000000000000000008152600401808060200182810382526026815260200180611dbb6026913960400191505060405180910390fd5b8073ffffffffffffffffffffffffffffffffffffffff16600560019054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff167f8be0079c531659141344cd1fd0a4f28419497f9722a3daafe3b4186f6b6457e060405160405180910390a380600560016101000a81548173ffffffffffffffffffffffffffffffffffffffff021916908373ffffffffffffffffffffffffffffffffffffffff16021790555050565b600080828401905083811015611374576040517f08c379a000000000000000000000000000000000000000000000000000000000815260040180806020018281038252601b8152602001807f536166654d6174683a206164646974696f6e206f766572666c6f77000000000081525060200191505060405180910390fd5b8091505092915050565b600033905090565b600073ffffffffffffffffffffffffffffffffffffffff168373ffffffffffffffffffffffffffffffffffffffff16141561140c576040517f08c379a0000000000000000000000000000000000000000000000000000000008152600401808060200182810382526024815260200180611ebb6024913960400191505060405180910390fd5b600073ffffffffffffffffffffffffffffffffffffffff168273ffffffffffffffffffffffffffffffffffffffff161415611492576040517f08c379a0000000000000000000000000000000000000000000000000000000008152600401808060200182810382526022815260200180611de16022913960400191505060405180910390fd5b80600160008573ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060008473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020819055508173ffffffffffffffffffffffffffffffffffffffff168373ffffffffffffffffffffffffffffffffffffffff167f8c5be1e5ebec7d5bd14f71427d1e84f3dd0314c0f7b2291e5b200ac8c7c3b925836040518082815260200191505060405180910390a3505050565b600073ffffffffffffffffffffffffffffffffffffffff168373ffffffffffffffffffffffffffffffffffffffff161415611603576040517f08c379a0000000000000000000000000000000000000000000000000000000008152600401808060200182810382526025815260200180611e966025913960400191505060405180910390fd5b600073ffffffffffffffffffffffffffffffffffffffff168273ffffffffffffffffffffffffffffffffffffffff161415611689576040517f08c379a0000000000000000000000000000000000000000000000000000000008152600401808060200182810382526023815260200180611d766023913960400191505060405180910390fd5b611694838383611c89565b6116ff81604051806060016040528060268152602001611e03602691396000808773ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1681526020019081526020016000205461183e9092919063ffffffff16565b6000808573ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002081905550611792816000808573ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020546112f690919063ffffffff16565b6000808473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020819055508173ffffffffffffffffffffffffffffffffffffffff168373ffffffffffffffffffffffffffffffffffffffff167fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef836040518082815260200191505060405180910390a3505050565b60008383111582906118eb576040517f08c379a00000000000000000000000000000000000000000000000000000000081526004018080602001828103825283818151815260200191508051906020019080838360005b838110156118b0578082015181840152602081019050611895565b50505050905090810190601f1680156118dd5780820380516001836020036101000a031916815260200191505b509250505060405180910390fd5b5060008385039050809150509392505050565b600073ffffffffffffffffffffffffffffffffffffffff168273ffffffffffffffffffffffffffffffffffffffff1614156119a1576040517f08c379a000000000000000000000000000000000000000000000000000000000815260040180806020018281038252601f8152602001807f45524332303a206d696e7420746f20746865207a65726f20616464726573730081525060200191505060405180910390fd5b6119ad60008383611c89565b6119c2816002546112f690919063ffffffff16565b600281905550611a19816000808573ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020546112f690919063ffffffff16565b6000808473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff168152602001908152602001600020819055508173ffffffffffffffffffffffffffffffffffffffff16600073ffffffffffffffffffffffffffffffffffffffff167fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef836040518082815260200191505060405180910390a35050565b600073ffffffffffffffffffffffffffffffffffffffff168273ffffffffffffffffffffffffffffffffffffffff161415611b4b576040517f08c379a0000000000000000000000000000000000000000000000000000000008152600401808060200182810382526021815260200180611e756021913960400191505060405180910390fd5b611b5782600083611c89565b611bc281604051806060016040528060228152602001611d99602291396000808673ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1681526020019081526020016000205461183e9092919063ffffffff16565b6000808473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002081905550611c1981600254611c8e90919063ffffffff16565b600281905550600073ffffffffffffffffffffffffffffffffffffffff168273ffffffffffffffffffffffffffffffffffffffff167fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef836040518082815260200191505060405180910390a35050565b505050565b6000611cd083836040518060400160405280601e81526020017f536166654d6174683a207375627472616374696f6e206f766572666c6f77000081525061183e565b905092915050565b828054600181600116156101000203166002900490600052602060002090601f016020900481019282601f10611d1957805160ff1916838001178555611d47565b82800160010185558215611d47579182015b82811115611d46578251825591602001919060010190611d2b565b5b509050611d549190611d58565b5090565b5b80821115611d71576000816000905550600101611d59565b509056fe45524332303a207472616e7366657220746f20746865207a65726f206164647265737345524332303a206275726e20616d6f756e7420657863656564732062616c616e63654f776e61626c653a206e6577206f776e657220697320746865207a65726f206164647265737345524332303a20617070726f766520746f20746865207a65726f206164647265737345524332303a207472616e7366657220616d6f756e7420657863656564732062616c616e636545524332303a207472616e7366657220616d6f756e74206578636565647320616c6c6f77616e636545524332303a206275726e20616d6f756e74206578636565647320616c6c6f77616e636545524332303a206275726e2066726f6d20746865207a65726f206164647265737345524332303a207472616e736665722066726f6d20746865207a65726f206164647265737345524332303a20617070726f76652066726f6d20746865207a65726f206164647265737345524332303a2064656372656173656420616c6c6f77616e63652062656c6f77207a65726fa26469706673582212202257b13f6fc07c92d64fff3cb5e6c36d507851c21bf32db391d5f7f5019c7dd964736f6c634300060c0033";

		public WrappedStraxTokenDeployment() : base(BYTECODE)
		{
		}

		[Parameter("uint256", "totalSupply")]
		public BigInteger TotalSupply { get; set; }
	}

	[Function("balanceOf", "uint256")]
	public class BalanceOfFunction : FunctionMessage
	{
		[Parameter("address", "_owner", 1)]
		public string Owner { get; set; }
	}

	[FunctionOutput]
	public class BalanceOfOutputDTO : IFunctionOutputDTO
	{
		[Parameter("uint256", "balance", 1)]
		public BigInteger Balance { get; set; }
	}

	[Function("transfer", "bool")]
	public class TransferFunction : FunctionMessage
	{
		[Parameter("address", "_to", 1)]
		public string To { get; set; }

		[Parameter("uint256", "_value", 2)]
		public BigInteger TokenAmount { get; set; }
	}

    [Event("Transfer")]
    public class TransferEventDTO : IEventDTO
    {
        [Parameter("address", "_from", 1, true)]
        public string From { get; set; }

        [Parameter("address", "_to", 2, true)]
        public string To { get; set; }

        [Parameter("uint256", "_value", 3, false)]
        public BigInteger Value { get; set; }
    }

	[Function("owner", "address")]
	public class OwnerFunction : FunctionMessage
	{
	}

	[Function("transferOwnership")]
	public class TransferOwnershipFunction : FunctionMessage
	{
		[Parameter("address", "newOwner", 1)]
		public string NewOwner { get; set; }
	}

    [Function("withdrawalAddresses")]
    public class WithdrawalAddressesFunction : FunctionMessage
    {
        [Parameter("address", "", 1)]
        public string Address { get; set; }
    }

	[Function("mint")]
	public class MintFunction : FunctionMessage
	{
		[Parameter("address", "account", 1)]
		public string Account { get; set; }

		[Parameter("uint256", "amount", 2)]
		public BigInteger Amount { get; set; }
	}

	[Function("burn")]
	public class BurnFunction : FunctionMessage
	{
		[Parameter("uint256", "amount", 1)]
		public BigInteger Amount { get; set; }
	}

    public class TransferParamsInput
    {
        [Parameter("address", 1)] public string To { get; set; }

        [Parameter("uint256", 2)] public BigInteger Value { get; set; }
    }

	public class MintParamsInput
    {
        [Parameter("address", 1)] public string Account { get; set; }

        [Parameter("uint256", 2)] public BigInteger Amount { get; set; }
    }

    public class BurnParamsInput
    {
        [Parameter("uint256", 1)] public BigInteger Amount { get; set; }
	}

	public class WrappedStrax
	{
		public static async Task<string> DeployContract(Web3 web3, BigInteger totalSupply)
		{
			var deploymentMessage = new WrappedStraxTokenDeployment()
			{
				TotalSupply = totalSupply
			};

			IContractDeploymentTransactionHandler<WrappedStraxTokenDeployment> deploymentHandler = web3.Eth.GetContractDeploymentHandler<WrappedStraxTokenDeployment>();
			TransactionReceipt transactionReceiptDeployment = await deploymentHandler.SendRequestAndWaitForReceiptAsync(deploymentMessage);
			string contractAddress = transactionReceiptDeployment.ContractAddress;

			return contractAddress;
		}

		public static async Task<BigInteger> GetErc20Balance(Web3 web3, string contractAddress, string addressToQuery)
		{
			var balanceOfFunctionMessage = new BalanceOfFunction()
			{
				Owner = addressToQuery
			};

			IContractQueryHandler<BalanceOfFunction> balanceHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
			BigInteger balance = await balanceHandler.QueryAsync<BigInteger>(contractAddress, balanceOfFunctionMessage);

			return balance;
		}

		public static async Task<string> Transfer(Web3 web3, string contractAddress, string recipient, BigInteger amount, BigInteger gas, BigInteger gasPrice)
		{
			IContractTransactionHandler<TransferFunction> transferHandler = web3.Eth.GetContractTransactionHandler<TransferFunction>();

			var transfer = new TransferFunction()
			{
				To = recipient,
				TokenAmount = amount,
                Gas = gas,
                GasPrice = Web3.Convert.ToWei(gasPrice, UnitConversion.EthUnit.Gwei)
            };

			TransactionReceipt transactionTransferReceipt = await transferHandler.SendRequestAndWaitForReceiptAsync(contractAddress, transfer);

			return transactionTransferReceipt.TransactionHash;
		}

		public static async Task<string> TransferOffline(Web3 web3, string contractAddress, string recipient, BigInteger amount, HexBigInteger nonce, BigInteger gas, BigInteger gasPrice, string fromAddress = null)
		{
			IContractTransactionHandler<TransferFunction> transferHandler = web3.Eth.GetContractTransactionHandler<TransferFunction>();

			var transfer = new TransferFunction()
			{
				To = recipient,
				TokenAmount = amount
			};

			// Nethereum internally calls the Ethereum client to set the GasPrice, Nonce and estimate the Gas, 
			// so if we want to sign the transaction for the contract completely offline we will need to set those values beforehand.
			transfer.Nonce = nonce.Value;
			transfer.Gas = gas;
			transfer.GasPrice = Web3.Convert.ToWei(gasPrice, UnitConversion.EthUnit.Gwei);

			if (fromAddress != null)
				transfer.FromAddress = fromAddress;

			string result = await transferHandler.SignTransactionAsync(contractAddress, transfer);

			return result;
		}

		public static async Task<string> TransferOwnership(Web3 web3, string contractAddress, string newOwner)
		{
			IContractTransactionHandler<TransferOwnershipFunction> transferHandler = web3.Eth.GetContractTransactionHandler<TransferOwnershipFunction>();

			var transfer = new TransferOwnershipFunction()
			{
				NewOwner = newOwner
			};

			TransactionReceipt transactionTransferReceipt = await transferHandler.SendRequestAndWaitForReceiptAsync(contractAddress, transfer);

			return transactionTransferReceipt.TransactionHash;
		}

		public static async Task<string> GetOwner(Web3 web3, string contractAddress)
		{
			var ownerFunctionMessage = new OwnerFunction()
			{
			};

			IContractQueryHandler<OwnerFunction> ownerHandler = web3.Eth.GetContractQueryHandler<OwnerFunction>();
			string owner = await ownerHandler.QueryAsync<string>(contractAddress, ownerFunctionMessage);

			return owner;
		}

        public static async Task<string> GetDestinationAddress(Web3 web3, string contractAddress, string addressToQuery)
        {
            var withdrawalAddressesFunctionMessage = new WithdrawalAddressesFunction()
            {
				Address = addressToQuery
            };

            IContractQueryHandler<WithdrawalAddressesFunction> queryHandler = web3.Eth.GetContractQueryHandler<WithdrawalAddressesFunction>();
            string destinationAddress = await queryHandler.QueryAsync<string>(contractAddress, withdrawalAddressesFunctionMessage);

            return destinationAddress;
        }

		public static string ABI = @"[
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""uint256"",
				        ""name"": ""initialSupply"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""constructor""
	        },
	        {
		        ""anonymous"": false,
		        ""inputs"": [
			        {
				        ""indexed"": true,
				        ""internalType"": ""address"",
				        ""name"": ""owner"",
				        ""type"": ""address""
			        },
			        {
				        ""indexed"": true,
				        ""internalType"": ""address"",
				        ""name"": ""spender"",
				        ""type"": ""address""
			        },
			        {
				        ""indexed"": false,
				        ""internalType"": ""uint256"",
				        ""name"": ""value"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""Approval"",
		        ""type"": ""event""
	        },
	        {
		        ""anonymous"": false,
		        ""inputs"": [
			        {
				        ""indexed"": true,
				        ""internalType"": ""address"",
				        ""name"": ""previousOwner"",
				        ""type"": ""address""
			        },
			        {
				        ""indexed"": true,
				        ""internalType"": ""address"",
				        ""name"": ""newOwner"",
				        ""type"": ""address""
			        }
		        ],
		        ""name"": ""OwnershipTransferred"",
		        ""type"": ""event""
	        },
	        {
		        ""anonymous"": false,
		        ""inputs"": [
			        {
				        ""indexed"": true,
				        ""internalType"": ""address"",
				        ""name"": ""from"",
				        ""type"": ""address""
			        },
			        {
				        ""indexed"": true,
				        ""internalType"": ""address"",
				        ""name"": ""to"",
				        ""type"": ""address""
			        },
			        {
				        ""indexed"": false,
				        ""internalType"": ""uint256"",
				        ""name"": ""value"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""Transfer"",
		        ""type"": ""event""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""owner"",
				        ""type"": ""address""
			        },
			        {
				        ""internalType"": ""address"",
				        ""name"": ""spender"",
				        ""type"": ""address""
			        }
		        ],
		        ""name"": ""allowance"",
		        ""outputs"": [
			        {
				        ""internalType"": ""uint256"",
				        ""name"": """",
				        ""type"": ""uint256""
			        }
		        ],
		        ""stateMutability"": ""view"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""spender"",
				        ""type"": ""address""
			        },
			        {
				        ""internalType"": ""uint256"",
				        ""name"": ""amount"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""approve"",
		        ""outputs"": [
			        {
				        ""internalType"": ""bool"",
				        ""name"": """",
				        ""type"": ""bool""
			        }
		        ],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""account"",
				        ""type"": ""address""
			        }
		        ],
		        ""name"": ""balanceOf"",
		        ""outputs"": [
			        {
				        ""internalType"": ""uint256"",
				        ""name"": """",
				        ""type"": ""uint256""
			        }
		        ],
		        ""stateMutability"": ""view"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""uint256"",
				        ""name"": ""amount"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""burn"",
		        ""outputs"": [],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""account"",
				        ""type"": ""address""
			        },
			        {
				        ""internalType"": ""uint256"",
				        ""name"": ""amount"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""burnFrom"",
		        ""outputs"": [],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [],
		        ""name"": ""decimals"",
		        ""outputs"": [
			        {
				        ""internalType"": ""uint8"",
				        ""name"": """",
				        ""type"": ""uint8""
			        }
		        ],
		        ""stateMutability"": ""view"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""spender"",
				        ""type"": ""address""
			        },
			        {
				        ""internalType"": ""uint256"",
				        ""name"": ""subtractedValue"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""decreaseAllowance"",
		        ""outputs"": [
			        {
				        ""internalType"": ""bool"",
				        ""name"": """",
				        ""type"": ""bool""
			        }
		        ],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""spender"",
				        ""type"": ""address""
			        },
			        {
				        ""internalType"": ""uint256"",
				        ""name"": ""addedValue"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""increaseAllowance"",
		        ""outputs"": [
			        {
				        ""internalType"": ""bool"",
				        ""name"": """",
				        ""type"": ""bool""
			        }
		        ],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""account"",
				        ""type"": ""address""
			        },
			        {
				        ""internalType"": ""uint256"",
				        ""name"": ""amount"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""mint"",
		        ""outputs"": [],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [],
		        ""name"": ""name"",
		        ""outputs"": [
			        {
				        ""internalType"": ""string"",
				        ""name"": """",
				        ""type"": ""string""
			        }
		        ],
		        ""stateMutability"": ""view"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [],
		        ""name"": ""owner"",
		        ""outputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": """",
				        ""type"": ""address""
			        }
		        ],
		        ""stateMutability"": ""view"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""string"",
				        ""name"": ""straxAddress"",
				        ""type"": ""string""
			        }
		        ],
		        ""name"": ""registerUnwrapAddress"",
		        ""outputs"": [],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [],
		        ""name"": ""renounceOwnership"",
		        ""outputs"": [],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [],
		        ""name"": ""symbol"",
		        ""outputs"": [
			        {
				        ""internalType"": ""string"",
				        ""name"": """",
				        ""type"": ""string""
			        }
		        ],
		        ""stateMutability"": ""view"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [],
		        ""name"": ""totalSupply"",
		        ""outputs"": [
			        {
				        ""internalType"": ""uint256"",
				        ""name"": """",
				        ""type"": ""uint256""
			        }
		        ],
		        ""stateMutability"": ""view"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""recipient"",
				        ""type"": ""address""
			        },
			        {
				        ""internalType"": ""uint256"",
				        ""name"": ""amount"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""transfer"",
		        ""outputs"": [
			        {
				        ""internalType"": ""bool"",
				        ""name"": """",
				        ""type"": ""bool""
			        }
		        ],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""sender"",
				        ""type"": ""address""
			        },
			        {
				        ""internalType"": ""address"",
				        ""name"": ""recipient"",
				        ""type"": ""address""
			        },
			        {
				        ""internalType"": ""uint256"",
				        ""name"": ""amount"",
				        ""type"": ""uint256""
			        }
		        ],
		        ""name"": ""transferFrom"",
		        ""outputs"": [
			        {
				        ""internalType"": ""bool"",
				        ""name"": """",
				        ""type"": ""bool""
			        }
		        ],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""newOwner"",
				        ""type"": ""address""
			        }
		        ],
		        ""name"": ""transferOwnership"",
		        ""outputs"": [],
		        ""stateMutability"": ""nonpayable"",
		        ""type"": ""function""
	        },
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": """",
				        ""type"": ""address""
			        }
		        ],
		        ""name"": ""withdrawalAddresses"",
		        ""outputs"": [
			        {
				        ""internalType"": ""string"",
				        ""name"": """",
				        ""type"": ""string""
			        }
		        ],
		        ""stateMutability"": ""view"",
		        ""type"": ""function""
	        }
        ]";
	}
}
