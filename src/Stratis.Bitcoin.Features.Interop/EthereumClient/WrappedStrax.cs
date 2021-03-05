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
            "0x60806040523480156200001157600080fd5b50604051620016ba380380620016ba833981810160405260208110156200003757600080fd5b5051604080518082018252600c81526b0aee4c2e0e0cac8a6e8e4c2f60a31b6020828101918252835180850190945260068452650aea6a8a482b60d31b9084015281519192916200008b91600391620002a0565b508051620000a1906004906020840190620002a0565b50506005805460ff19166012179055506000620000bd62000126565b60058054610100600160a81b0319166101006001600160a01b03841690810291909117909155604051919250906000907f8be0079c531659141344cd1fd0a4f28419497f9722a3daafe3b4186f6b6457e0908290a3506200011f33826200012a565b506200033c565b3390565b6001600160a01b03821662000186576040805162461bcd60e51b815260206004820152601f60248201527f45524332303a206d696e7420746f20746865207a65726f206164647265737300604482015290519081900360640190fd5b620001946000838362000239565b620001b0816002546200023e60201b62000ba61790919060201c565b6002556001600160a01b03821660009081526020818152604090912054620001e391839062000ba66200023e821b17901c565b6001600160a01b0383166000818152602081815260408083209490945583518581529351929391927fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef9281900390910190a35050565b505050565b60008282018381101562000299576040805162461bcd60e51b815260206004820152601b60248201527f536166654d6174683a206164646974696f6e206f766572666c6f770000000000604482015290519081900360640190fd5b9392505050565b828054600181600116156101000203166002900490600052602060002090601f016020900481019282601f10620002e357805160ff191683800117855562000313565b8280016001018555821562000313579182015b8281111562000313578251825591602001919060010190620002f6565b506200032192915062000325565b5090565b5b8082111562000321576000815560010162000326565b61136e806200034c6000396000f3fe608060405234801561001057600080fd5b50600436106101165760003560e01c80637641e6f3116100a2578063979430d211610071578063979430d2146103cd578063a457c2d714610488578063a9059cbb146104b4578063dd62ed3e146104e0578063f2fde38b1461050e57610116565b80637641e6f3146102ce5780637e0518671461037b5780638da5cb5b146103a157806395d89b41146103c557610116565b8063313ce567116100e9578063313ce56714610228578063395093511461024657806340c10f191461027257806370a08231146102a0578063715018a6146102c657610116565b806306fdde031461011b578063095ea7b31461019857806318160ddd146101d857806323b872dd146101f2575b600080fd5b610123610534565b6040805160208082528351818301528351919283929083019185019080838360005b8381101561015d578181015183820152602001610145565b50505050905090810190601f16801561018a5780820380516001836020036101000a031916815260200191505b509250505060405180910390f35b6101c4600480360360408110156101ae57600080fd5b506001600160a01b0381351690602001356105ca565b604080519115158252519081900360200190f35b6101e06105e7565b60408051918252519081900360200190f35b6101c46004803603606081101561020857600080fd5b506001600160a01b038135811691602081013590911690604001356105ed565b610230610674565b6040805160ff9092168252519081900360200190f35b6101c46004803603604081101561025c57600080fd5b506001600160a01b03813516906020013561067d565b61029e6004803603604081101561028857600080fd5b506001600160a01b0381351690602001356106cb565b005b6101e0600480360360208110156102b657600080fd5b50356001600160a01b0316610748565b61029e610763565b61029e600480360360408110156102e457600080fd5b8135919081019060408101602082013564010000000081111561030657600080fd5b82018360208201111561031857600080fd5b8035906020019184600183028401116401000000008311171561033a57600080fd5b91908080601f016020809104026020016040519081016040528093929190818152602001838380828437600092019190915250929550610822945050505050565b6101236004803603602081101561039157600080fd5b50356001600160a01b0316610858565b6103a96108f3565b604080516001600160a01b039092168252519081900360200190f35b610123610907565b61029e600480360360608110156103e357600080fd5b6001600160a01b038235169160208101359181019060608101604082013564010000000081111561041357600080fd5b82018360208201111561042557600080fd5b8035906020019184600183028401116401000000008311171561044757600080fd5b91908080601f016020809104026020016040519081016040528093929190818152602001838380828437600092019190915250929550610968945050505050565b6101c46004803603604081101561049e57600080fd5b506001600160a01b0381351690602001356109e4565b6101c4600480360360408110156104ca57600080fd5b506001600160a01b038135169060200135610a4c565b6101e0600480360360408110156104f657600080fd5b506001600160a01b0381358116916020013516610a60565b61029e6004803603602081101561052457600080fd5b50356001600160a01b0316610a8b565b60038054604080516020601f60026000196101006001881615020190951694909404938401819004810282018101909252828152606093909290918301828280156105c05780601f10610595576101008083540402835291602001916105c0565b820191906000526020600020905b8154815290600101906020018083116105a357829003601f168201915b5050505050905090565b60006105de6105d7610c07565b8484610c0b565b50600192915050565b60025490565b60006105fa848484610cf7565b61066a84610606610c07565b6106658560405180606001604052806028815260200161125e602891396001600160a01b038a16600090815260016020526040812090610644610c07565b6001600160a01b031681526020810191909152604001600020549190610e52565b610c0b565b5060019392505050565b60055460ff1690565b60006105de61068a610c07565b84610665856001600061069b610c07565b6001600160a01b03908116825260208083019390935260409182016000908120918c168152925290205490610ba6565b6106d3610c07565b60055461010090046001600160a01b0390811691161461073a576040805162461bcd60e51b815260206004820181905260248201527f4f776e61626c653a2063616c6c6572206973206e6f7420746865206f776e6572604482015290519081900360640190fd5b6107448282610ee9565b5050565b6001600160a01b031660009081526020819052604090205490565b61076b610c07565b60055461010090046001600160a01b039081169116146107d2576040805162461bcd60e51b815260206004820181905260248201527f4f776e61626c653a2063616c6c6572206973206e6f7420746865206f776e6572604482015290519081900360640190fd5b60055460405160009161010090046001600160a01b0316907f8be0079c531659141344cd1fd0a4f28419497f9722a3daafe3b4186f6b6457e0908390a360058054610100600160a81b0319169055565b61083361082d610c07565b83610fd9565b336000908152600660209081526040909120825161085392840190611117565b505050565b60066020908152600091825260409182902080548351601f6002600019610100600186161502019093169290920491820184900484028101840190945280845290918301828280156108eb5780601f106108c0576101008083540402835291602001916108eb565b820191906000526020600020905b8154815290600101906020018083116108ce57829003601f168201915b505050505081565b60055461010090046001600160a01b031690565b60048054604080516020601f60026000196101006001881615020190951694909404938401819004810282018101909252828152606093909290918301828280156105c05780601f10610595576101008083540402835291602001916105c0565b600061099f836040518060600160405280602481526020016112866024913961099887610993610c07565b610a60565b9190610e52565b90506109b3846109ad610c07565b83610c0b565b6109bd8484610fd9565b33600090815260066020908152604090912083516109dd92850190611117565b5050505050565b60006105de6109f1610c07565b84610665856040518060600160405280602581526020016113146025913960016000610a1b610c07565b6001600160a01b03908116825260208083019390935260409182016000908120918d16815292529020549190610e52565b60006105de610a59610c07565b8484610cf7565b6001600160a01b03918216600090815260016020908152604080832093909416825291909152205490565b610a93610c07565b60055461010090046001600160a01b03908116911614610afa576040805162461bcd60e51b815260206004820181905260248201527f4f776e61626c653a2063616c6c6572206973206e6f7420746865206f776e6572604482015290519081900360640190fd5b6001600160a01b038116610b3f5760405162461bcd60e51b81526004018080602001828103825260268152602001806111f06026913960400191505060405180910390fd5b6005546040516001600160a01b0380841692610100900416907f8be0079c531659141344cd1fd0a4f28419497f9722a3daafe3b4186f6b6457e090600090a3600580546001600160a01b0390921661010002610100600160a81b0319909216919091179055565b600082820183811015610c00576040805162461bcd60e51b815260206004820152601b60248201527f536166654d6174683a206164646974696f6e206f766572666c6f770000000000604482015290519081900360640190fd5b9392505050565b3390565b6001600160a01b038316610c505760405162461bcd60e51b81526004018080602001828103825260248152602001806112f06024913960400191505060405180910390fd5b6001600160a01b038216610c955760405162461bcd60e51b81526004018080602001828103825260228152602001806112166022913960400191505060405180910390fd5b6001600160a01b03808416600081815260016020908152604080832094871680845294825291829020859055815185815291517f8c5be1e5ebec7d5bd14f71427d1e84f3dd0314c0f7b2291e5b200ac8c7c3b9259281900390910190a3505050565b6001600160a01b038316610d3c5760405162461bcd60e51b81526004018080602001828103825260258152602001806112cb6025913960400191505060405180910390fd5b6001600160a01b038216610d815760405162461bcd60e51b81526004018080602001828103825260238152602001806111ab6023913960400191505060405180910390fd5b610d8c838383610853565b610dc981604051806060016040528060268152602001611238602691396001600160a01b0386166000908152602081905260409020549190610e52565b6001600160a01b038085166000908152602081905260408082209390935590841681522054610df89082610ba6565b6001600160a01b038084166000818152602081815260409182902094909455805185815290519193928716927fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef92918290030190a3505050565b60008184841115610ee15760405162461bcd60e51b81526004018080602001828103825283818151815260200191508051906020019080838360005b83811015610ea6578181015183820152602001610e8e565b50505050905090810190601f168015610ed35780820380516001836020036101000a031916815260200191505b509250505060405180910390fd5b505050900390565b6001600160a01b038216610f44576040805162461bcd60e51b815260206004820152601f60248201527f45524332303a206d696e7420746f20746865207a65726f206164647265737300604482015290519081900360640190fd5b610f5060008383610853565b600254610f5d9082610ba6565b6002556001600160a01b038216600090815260208190526040902054610f839082610ba6565b6001600160a01b0383166000818152602081815260408083209490945583518581529351929391927fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef9281900390910190a35050565b6001600160a01b03821661101e5760405162461bcd60e51b81526004018080602001828103825260218152602001806112aa6021913960400191505060405180910390fd5b61102a82600083610853565b611067816040518060600160405280602281526020016111ce602291396001600160a01b0385166000908152602081905260409020549190610e52565b6001600160a01b03831660009081526020819052604090205560025461108d90826110d5565b6002556040805182815290516000916001600160a01b038516917fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef9181900360200190a35050565b6000610c0083836040518060400160405280601e81526020017f536166654d6174683a207375627472616374696f6e206f766572666c6f770000815250610e52565b828054600181600116156101000203166002900490600052602060002090601f016020900481019282601f1061115857805160ff1916838001178555611185565b82800160010185558215611185579182015b8281111561118557825182559160200191906001019061116a565b50611191929150611195565b5090565b5b80821115611191576000815560010161119656fe45524332303a207472616e7366657220746f20746865207a65726f206164647265737345524332303a206275726e20616d6f756e7420657863656564732062616c616e63654f776e61626c653a206e6577206f776e657220697320746865207a65726f206164647265737345524332303a20617070726f766520746f20746865207a65726f206164647265737345524332303a207472616e7366657220616d6f756e7420657863656564732062616c616e636545524332303a207472616e7366657220616d6f756e74206578636565647320616c6c6f77616e636545524332303a206275726e20616d6f756e74206578636565647320616c6c6f77616e636545524332303a206275726e2066726f6d20746865207a65726f206164647265737345524332303a207472616e736665722066726f6d20746865207a65726f206164647265737345524332303a20617070726f76652066726f6d20746865207a65726f206164647265737345524332303a2064656372656173656420616c6c6f77616e63652062656c6f77207a65726fa2646970667358221220cad3aac9a515bf8c31e395c1cb60e9ec8ba2edb3e0e92408c5f7db093befc84664736f6c634300060c0033";

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

        [Parameter("string", "straxAddress", 2)]
        public string StraxAddress { get; set; }
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
                    },
                    {
                        ""internalType"": ""string"",
                        ""name"": ""straxAddress"",
                        ""type"": ""string""
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
                    },
                    {
                        ""internalType"": ""string"",
                        ""name"": ""straxAddress"",
                        ""type"": ""string""
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
