using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using NBitcoin.DataEncoders;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Contracts.ContractHandlers;
using Nethereum.Contracts.CQS;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    [Function("getOwners", "address[]")]
    public class GetOwnersFunction : FunctionMessage
    {
    }

    public class MultisigWalletDeployment : ContractDeploymentMessage
    {
        public static string BYTECODE =
            "0x60806040523480156200001157600080fd5b50604051620024c8380380620024c883398101806040528101908080518201929190602001805190602001909291905050506000825182603282111580156200005a5750818111155b801562000068575060008114155b801562000076575060008214155b15156200008257600080fd5b600092505b8451831015620001bd57600260008685815181101515620000a457fe5b9060200190602002015173ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff16158015620001335750600085848151811015156200011057fe5b9060200190602002015173ffffffffffffffffffffffffffffffffffffffff1614155b15156200013f57600080fd5b60016002600087868151811015156200015457fe5b9060200190602002015173ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060006101000a81548160ff021916908315150217905550828060010193505062000087565b8460039080519060200190620001d5929190620001e8565b50836004819055505050505050620002bd565b82805482825590600052602060002090810192821562000264579160200282015b82811115620002635782518260006101000a81548173ffffffffffffffffffffffffffffffffffffffff021916908373ffffffffffffffffffffffffffffffffffffffff1602179055509160200191906001019062000209565b5b50905062000273919062000277565b5090565b620002ba91905b80821115620002b657600081816101000a81549073ffffffffffffffffffffffffffffffffffffffff0219169055506001016200027e565b5090565b90565b6121fb80620002cd6000396000f30060806040526004361061011d576000357c0100000000000000000000000000000000000000000000000000000000900463ffffffff168063025e7c2714610177578063173825d9146101e457806320ea8d86146102275780632f54bf6e146102545780633411c81c146102af57806354741525146103145780637065cb4814610363578063784547a7146103a65780638b51d13f146103eb5780639ace38c21461042c578063a0e67e2b14610517578063a8abe69a14610583578063b5dc40c314610627578063b77bf600146106a9578063ba51a6df146106d4578063c01a8c8414610701578063c64274741461072e578063d74f8edd146107d5578063dc8452cd14610800578063e20056e61461082b578063ee22610b1461088e575b6000341115610175573373ffffffffffffffffffffffffffffffffffffffff167fe1fffcc4923d04b559f4d29a8bfc6cda04eb5b0d3c460751c2402c5c5cc9109c346040518082815260200191505060405180910390a25b005b34801561018357600080fd5b506101a2600480360381019080803590602001909291905050506108bb565b604051808273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200191505060405180910390f35b3480156101f057600080fd5b50610225600480360381019080803573ffffffffffffffffffffffffffffffffffffffff1690602001909291905050506108f9565b005b34801561023357600080fd5b5061025260048036038101908080359060200190929190505050610b92565b005b34801561026057600080fd5b50610295600480360381019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190505050610d3a565b604051808215151515815260200191505060405180910390f35b3480156102bb57600080fd5b506102fa60048036038101908080359060200190929190803573ffffffffffffffffffffffffffffffffffffffff169060200190929190505050610d5a565b604051808215151515815260200191505060405180910390f35b34801561032057600080fd5b5061034d600480360381019080803515159060200190929190803515159060200190929190505050610d89565b6040518082815260200191505060405180910390f35b34801561036f57600080fd5b506103a4600480360381019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190505050610e1b565b005b3480156103b257600080fd5b506103d160048036038101908080359060200190929190505050611020565b604051808215151515815260200191505060405180910390f35b3480156103f757600080fd5b5061041660048036038101908080359060200190929190505050611105565b6040518082815260200191505060405180910390f35b34801561043857600080fd5b50610457600480360381019080803590602001909291905050506111d0565b604051808573ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1681526020018481526020018060200183151515158152602001828103825284818151815260200191508051906020019080838360005b838110156104d95780820151818401526020810190506104be565b50505050905090810190601f1680156105065780820380516001836020036101000a031916815260200191505b509550505050505060405180910390f35b34801561052357600080fd5b5061052c6112c5565b6040518080602001828103825283818151815260200191508051906020019060200280838360005b8381101561056f578082015181840152602081019050610554565b505050509050019250505060405180910390f35b34801561058f57600080fd5b506105d06004803603810190808035906020019092919080359060200190929190803515159060200190929190803515159060200190929190505050611353565b6040518080602001828103825283818151815260200191508051906020019060200280838360005b838110156106135780820151818401526020810190506105f8565b505050509050019250505060405180910390f35b34801561063357600080fd5b50610652600480360381019080803590602001909291905050506114c4565b6040518080602001828103825283818151815260200191508051906020019060200280838360005b8381101561069557808201518184015260208101905061067a565b505050509050019250505060405180910390f35b3480156106b557600080fd5b506106be611701565b6040518082815260200191505060405180910390f35b3480156106e057600080fd5b506106ff60048036038101908080359060200190929190505050611707565b005b34801561070d57600080fd5b5061072c600480360381019080803590602001909291905050506117c1565b005b34801561073a57600080fd5b506107bf600480360381019080803573ffffffffffffffffffffffffffffffffffffffff16906020019092919080359060200190929190803590602001908201803590602001908080601f016020809104026020016040519081016040528093929190818152602001838380828437820191505050505050919291929050505061199e565b6040518082815260200191505060405180910390f35b3480156107e157600080fd5b506107ea6119bd565b6040518082815260200191505060405180910390f35b34801561080c57600080fd5b506108156119c2565b6040518082815260200191505060405180910390f35b34801561083757600080fd5b5061088c600480360381019080803573ffffffffffffffffffffffffffffffffffffffff169060200190929190803573ffffffffffffffffffffffffffffffffffffffff1690602001909291905050506119c8565b005b34801561089a57600080fd5b506108b960048036038101908080359060200190929190505050611cdd565b005b6003818154811015156108ca57fe5b906000526020600020016000915054906101000a900473ffffffffffffffffffffffffffffffffffffffff1681565b60003073ffffffffffffffffffffffffffffffffffffffff163373ffffffffffffffffffffffffffffffffffffffff1614151561093557600080fd5b81600260008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff16151561098e57600080fd5b6000600260008573ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060006101000a81548160ff021916908315150217905550600091505b600160038054905003821015610b13578273ffffffffffffffffffffffffffffffffffffffff16600383815481101515610a2157fe5b9060005260206000200160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff161415610b06576003600160038054905003815481101515610a7f57fe5b9060005260206000200160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff16600383815481101515610ab957fe5b9060005260206000200160006101000a81548173ffffffffffffffffffffffffffffffffffffffff021916908373ffffffffffffffffffffffffffffffffffffffff160217905550610b13565b81806001019250506109eb565b6001600381818054905003915081610b2b91906120fe565b506003805490506004541115610b4a57610b49600380549050611707565b5b8273ffffffffffffffffffffffffffffffffffffffff167f8001553a916ef2f495d26a907cc54d96ed840d7bda71e73194bf5a9df7a76b9060405160405180910390a2505050565b33600260008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff161515610beb57600080fd5b81336001600083815260200190815260200160002060008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff161515610c5657600080fd5b8360008082815260200190815260200160002060030160009054906101000a900460ff16151515610c8657600080fd5b60006001600087815260200190815260200160002060003373ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060006101000a81548160ff021916908315150217905550843373ffffffffffffffffffffffffffffffffffffffff167ff6a317157440607f36269043eb55f1287a5a19ba2216afeab88cd46cbcfb88e960405160405180910390a35050505050565b60026020528060005260406000206000915054906101000a900460ff1681565b60016020528160005260406000206020528060005260406000206000915091509054906101000a900460ff1681565b600080600090505b600554811015610e1457838015610dc8575060008082815260200190815260200160002060030160009054906101000a900460ff16155b80610dfb5750828015610dfa575060008082815260200190815260200160002060030160009054906101000a900460ff165b5b15610e07576001820191505b8080600101915050610d91565b5092915050565b3073ffffffffffffffffffffffffffffffffffffffff163373ffffffffffffffffffffffffffffffffffffffff16141515610e5557600080fd5b80600260008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff16151515610eaf57600080fd5b8160008173ffffffffffffffffffffffffffffffffffffffff1614151515610ed657600080fd5b60016003805490500160045460328211158015610ef35750818111155b8015610f00575060008114155b8015610f0d575060008214155b1515610f1857600080fd5b6001600260008773ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060006101000a81548160ff02191690831515021790555060038590806001815401808255809150509060018203906000526020600020016000909192909190916101000a81548173ffffffffffffffffffffffffffffffffffffffff021916908373ffffffffffffffffffffffffffffffffffffffff160217905550508473ffffffffffffffffffffffffffffffffffffffff167ff39e6e1eb0edcf53c221607b54b00cd28f3196fed0a24994dc308b8f611b682d60405160405180910390a25050505050565b6000806000809150600090505b6003805490508110156110fd5760016000858152602001908152602001600020600060038381548110151561105e57fe5b9060005260206000200160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff16156110dd576001820191505b6004548214156110f057600192506110fe565b808060010191505061102d565b5b5050919050565b600080600090505b6003805490508110156111ca5760016000848152602001908152602001600020600060038381548110151561113e57fe5b9060005260206000200160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff16156111bd576001820191505b808060010191505061110d565b50919050565b60006020528060005260406000206000915090508060000160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff1690806001015490806002018054600181600116156101000203166002900480601f0160208091040260200160405190810160405280929190818152602001828054600181600116156101000203166002900480156112a85780601f1061127d576101008083540402835291602001916112a8565b820191906000526020600020905b81548152906001019060200180831161128b57829003601f168201915b5050505050908060030160009054906101000a900460ff16905084565b6060600380548060200260200160405190810160405280929190818152602001828054801561134957602002820191906000526020600020905b8160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190600101908083116112ff575b5050505050905090565b60608060008060055460405190808252806020026020018201604052801561138a5781602001602082028038833980820191505090505b50925060009150600090505b600554811015611436578580156113cd575060008082815260200190815260200160002060030160009054906101000a900460ff16155b8061140057508480156113ff575060008082815260200190815260200160002060030160009054906101000a900460ff165b5b156114295780838381518110151561141457fe5b90602001906020020181815250506001820191505b8080600101915050611396565b8787036040519080825280602002602001820160405280156114675781602001602082028038833980820191505090505b5093508790505b868110156114b957828181518110151561148457fe5b906020019060200201518489830381518110151561149e57fe5b9060200190602002018181525050808060010191505061146e565b505050949350505050565b6060806000806003805490506040519080825280602002602001820160405280156114fe5781602001602082028038833980820191505090505b50925060009150600090505b60038054905081101561164b5760016000868152602001908152602001600020600060038381548110151561153b57fe5b9060005260206000200160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff161561163e576003818154811015156115c257fe5b9060005260206000200160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff1683838151811015156115fb57fe5b9060200190602002019073ffffffffffffffffffffffffffffffffffffffff16908173ffffffffffffffffffffffffffffffffffffffff16815250506001820191505b808060010191505061150a565b8160405190808252806020026020018201604052801561167a5781602001602082028038833980820191505090505b509350600090505b818110156116f957828181518110151561169857fe5b9060200190602002015184828151811015156116b057fe5b9060200190602002019073ffffffffffffffffffffffffffffffffffffffff16908173ffffffffffffffffffffffffffffffffffffffff16815250508080600101915050611682565b505050919050565b60055481565b3073ffffffffffffffffffffffffffffffffffffffff163373ffffffffffffffffffffffffffffffffffffffff1614151561174157600080fd5b60038054905081603282111580156117595750818111155b8015611766575060008114155b8015611773575060008214155b151561177e57600080fd5b826004819055507fa3f1ee9126a074d9326c682f561767f710e927faa811f7a99829d49dc421797a836040518082815260200191505060405180910390a1505050565b33600260008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff16151561181a57600080fd5b81600080600083815260200190815260200160002060000160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff161415151561187657600080fd5b82336001600083815260200190815260200160002060008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff161515156118e257600080fd5b600180600087815260200190815260200160002060003373ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060006101000a81548160ff021916908315150217905550843373ffffffffffffffffffffffffffffffffffffffff167f4a504a94899432a9846e1aa406dceb1bcfd538bb839071d49d1e5e23f5be30ef60405160405180910390a361199785611cdd565b5050505050565b60006119ab848484611f85565b90506119b6816117c1565b9392505050565b603281565b60045481565b60003073ffffffffffffffffffffffffffffffffffffffff163373ffffffffffffffffffffffffffffffffffffffff16141515611a0457600080fd5b82600260008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff161515611a5d57600080fd5b82600260008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff16151515611ab757600080fd5b600092505b600380549050831015611ba0578473ffffffffffffffffffffffffffffffffffffffff16600384815481101515611aef57fe5b9060005260206000200160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff161415611b935783600384815481101515611b4657fe5b9060005260206000200160006101000a81548173ffffffffffffffffffffffffffffffffffffffff021916908373ffffffffffffffffffffffffffffffffffffffff160217905550611ba0565b8280600101935050611abc565b6000600260008773ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060006101000a81548160ff0219169083151502179055506001600260008673ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060006101000a81548160ff0219169083151502179055508473ffffffffffffffffffffffffffffffffffffffff167f8001553a916ef2f495d26a907cc54d96ed840d7bda71e73194bf5a9df7a76b9060405160405180910390a28373ffffffffffffffffffffffffffffffffffffffff167ff39e6e1eb0edcf53c221607b54b00cd28f3196fed0a24994dc308b8f611b682d60405160405180910390a25050505050565b600033600260008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff161515611d3857600080fd5b82336001600083815260200190815260200160002060008273ffffffffffffffffffffffffffffffffffffffff1673ffffffffffffffffffffffffffffffffffffffff16815260200190815260200160002060009054906101000a900460ff161515611da357600080fd5b8460008082815260200190815260200160002060030160009054906101000a900460ff16151515611dd357600080fd5b611ddc86611020565b15611f7d57600080878152602001908152602001600020945060018560030160006101000a81548160ff021916908315150217905550611efa8560000160009054906101000a900473ffffffffffffffffffffffffffffffffffffffff16866001015487600201805460018160011615610100020316600290049050886002018054600181600116156101000203166002900480601f016020809104026020016040519081016040528092919081815260200182805460018160011615610100020316600290048015611ef05780601f10611ec557610100808354040283529160200191611ef0565b820191906000526020600020905b815481529060010190602001808311611ed357829003601f168201915b50505050506120d7565b15611f3157857f33e13ecb54c3076d8e8bb8c2881800a4d972b792045ffae98fdf46df365fed7560405160405180910390a2611f7c565b857f526441bb6c1aba3c9a4a6ca1d6545da9c2333c8c48343ef398eb858d72b7923660405160405180910390a260008560030160006101000a81548160ff0219169083151502179055505b5b505050505050565b60008360008173ffffffffffffffffffffffffffffffffffffffff1614151515611fae57600080fd5b60055491506080604051908101604052808673ffffffffffffffffffffffffffffffffffffffff1681526020018581526020018481526020016000151581525060008084815260200190815260200160002060008201518160000160006101000a81548173ffffffffffffffffffffffffffffffffffffffff021916908373ffffffffffffffffffffffffffffffffffffffff16021790555060208201518160010155604082015181600201908051906020019061206d92919061212a565b5060608201518160030160006101000a81548160ff0219169083151502179055509050506001600560008282540192505081905550817fc0ba8fe4b176c1714197d43b9cc6bcf797a4a7461c5fe8d0ef6e184ae7601e5160405160405180910390a2509392505050565b6000806040516020840160008287838a8c6187965a03f19250505080915050949350505050565b8154818355818111156121255781836000526020600020918201910161212491906121aa565b5b505050565b828054600181600116156101000203166002900490600052602060002090601f016020900481019282601f1061216b57805160ff1916838001178555612199565b82800160010185558215612199579182015b8281111561219857825182559160200191906001019061217d565b5b5090506121a691906121aa565b5090565b6121cc91905b808211156121c85760008160009055506001016121b0565b5090565b905600a165627a7a72305820d75e9eb36c60c222630447163984f892585447a8fa4f29e28a13557ecf25c51b0029";

        public MultisigWalletDeployment() : base(BYTECODE)
        {
        }

        [Parameter("address[]", "_owners", 1)]
        public string[] Owners { get; set; }

        [Parameter("uint", "_required", 2)]
        public uint Required { get; set; }
    }

    [Function("submitTransaction", "uint256")]
    public class SubmitTransactionFunction : FunctionMessage
    {
        [Parameter("address", "destination", 1)]
        public string Destination { get; set; }

        [Parameter("uint", "value", 2)]
        public BigInteger Value { get; set; }

        [Parameter("bytes", "data", 3)]
        public byte[] Data { get; set; }
    }

    [Event("Submission")]
    public class SubmissionEventDTO : IEventDTO
    {
        [Parameter("uint256", "transactionId", 1, true)]
        public BigInteger TransactionId { get; set; }
    }

    [Function("confirmTransaction")]
    public class ConfirmTransactionFunction : FunctionMessage
    {
        [Parameter("uint256", "transactionId", 1)]
        public BigInteger TransactionId { get; set; }
    }

    [Function("executeTransaction")]
    public class ExecuteTransactionFunction : FunctionMessage
    {
        [Parameter("uint256", "transactionId", 1)]
        public BigInteger TransactionId { get; set; }
    }

    [Function("getConfirmationCount", "uint256")]
    public class GetConfirmationCountFunction : FunctionMessage
    {
        [Parameter("uint256", "transactionId", 1)]
        public BigInteger TransactionId { get; set; }
    }

    public class MultisigTransactionIdentifiers
    {
        /// <summary>
        /// The hash of the Ethereum transaction containing the multisig contract call.
        /// </summary>
        public string TransactionHash { get; set; }

        /// <summary>
        /// The related multisig contract transaction ID.
        /// </summary>
        public BigInteger TransactionId { get; set; }
    }

    public class MultisigWallet
    {
        public static async Task<string> DeployContractAsync(Web3 web3, string[] owners, uint required)
        {
            var deploymentMessage = new MultisigWalletDeployment()
            {
                Owners = owners,
                Required = required
            };

            IContractDeploymentTransactionHandler<MultisigWalletDeployment> deploymentHandler = web3.Eth.GetContractDeploymentHandler<MultisigWalletDeployment>();
            TransactionReceipt transactionReceiptDeployment = await deploymentHandler.SendRequestAndWaitForReceiptAsync(deploymentMessage).ConfigureAwait(false);
            string contractAddress = transactionReceiptDeployment.ContractAddress;

            return contractAddress;
        }

        public static async Task<List<string>> GetOwnersAsync(Web3 web3, string contractAddress)
        {
            var getOwnersFunctionMessage = new GetOwnersFunction()
            {
            };

            IContractQueryHandler<GetOwnersFunction> ownerHandler = web3.Eth.GetContractQueryHandler<GetOwnersFunction>();
            List<string> owners = await ownerHandler.QueryAsync<List<string>>(contractAddress, getOwnersFunctionMessage).ConfigureAwait(false);

            return owners;
        }

        public static async Task<MultisigTransactionIdentifiers> SubmitTransactionAsync(Web3 web3, string contractAddress, string destination, BigInteger value, string data, BigInteger gas, BigInteger gasPrice)
        {
            IContractTransactionHandler<SubmitTransactionFunction> submitHandler = web3.Eth.GetContractTransactionHandler<SubmitTransactionFunction>();
            
            var submitTransactionFunctionMessage = new SubmitTransactionFunction()
            {
                Destination = destination,
                Value = value,
                Data = Encoders.Hex.DecodeData(data),
                Gas = gas,
                GasPrice = Web3.Convert.ToWei(gasPrice, UnitConversion.EthUnit.Gwei)
            };

            TransactionReceipt submitTransactionReceipt = await submitHandler.SendRequestAndWaitForReceiptAsync(contractAddress, submitTransactionFunctionMessage).ConfigureAwait(false);
            EventLog<SubmissionEventDTO> submission = submitTransactionReceipt.DecodeAllEvents<SubmissionEventDTO>().FirstOrDefault();

            // Use -1 as an error indicator.
            if (submission == null)
                return new MultisigTransactionIdentifiers() { TransactionId = BigInteger.MinusOne };

            return new MultisigTransactionIdentifiers() { TransactionHash = submitTransactionReceipt.TransactionHash, TransactionId = submission.Event.TransactionId };
        }

        public static async Task<string> ConfirmTransactionAsync(Web3 web3, string contractAddress, BigInteger transactionId, BigInteger gas, BigInteger gasPrice)
        {
            IContractTransactionHandler<ConfirmTransactionFunction> confirmationHandler = web3.Eth.GetContractTransactionHandler<ConfirmTransactionFunction>();

            var confirmTransactionFunctionMessage = new ConfirmTransactionFunction()
            {
                TransactionId = transactionId,
                Gas = gas,
                GasPrice = Web3.Convert.ToWei(gasPrice, UnitConversion.EthUnit.Gwei)
            };

            TransactionReceipt confirmTransactionReceipt = await confirmationHandler.SendRequestAndWaitForReceiptAsync(contractAddress, confirmTransactionFunctionMessage).ConfigureAwait(false);

            return confirmTransactionReceipt.TransactionHash;
        }

        /// <summary>
        /// Normally the final mandatory confirmation will automatically call the execute.
        /// This is provided in case it has to be called again due to an error condition.
        /// </summary>
        public static async Task<string> ExecuteTransactionAsync(Web3 web3, string contractAddress, BigInteger transactionId, BigInteger gas, BigInteger gasPrice)
        {
            IContractTransactionHandler<ExecuteTransactionFunction> executionHandler = web3.Eth.GetContractTransactionHandler<ExecuteTransactionFunction>();

            var executeTransactionFunctionMessage = new ExecuteTransactionFunction()
            {
                TransactionId = transactionId,
                Gas = gas,
                GasPrice = Web3.Convert.ToWei(gasPrice, UnitConversion.EthUnit.Gwei)
            };

            TransactionReceipt executeTransactionReceipt = await executionHandler.SendRequestAndWaitForReceiptAsync(contractAddress, executeTransactionFunctionMessage).ConfigureAwait(false);

            return executeTransactionReceipt.TransactionHash;
        }

        public static async Task<BigInteger> GetConfirmationCountAsync(Web3 web3, string contractAddress, BigInteger transactionId)
        {
            var getConfirmationCountFunctionMessage = new GetConfirmationCountFunction()
            {
                TransactionId = transactionId
            };

            IContractQueryHandler<GetConfirmationCountFunction> confirmationHandler = web3.Eth.GetContractQueryHandler<GetConfirmationCountFunction>();
            BigInteger confirmations = await confirmationHandler.QueryAsync<BigInteger>(contractAddress, getConfirmationCountFunctionMessage).ConfigureAwait(false);

            return confirmations;
        }

        public static string ABI = @"[
            {
                ""constant"": false,
                ""inputs"": [
                    {
                        ""name"": ""owner"",
                        ""type"": ""address""
                    }
                ],
                ""name"": ""addOwner"",
                ""outputs"": [],
                ""payable"": false,
                ""stateMutability"": ""nonpayable"",
                ""type"": ""function""
            },
            {
                ""constant"": false,
                ""inputs"": [
                    {
                        ""name"": ""_required"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""changeRequirement"",
                ""outputs"": [],
                ""payable"": false,
                ""stateMutability"": ""nonpayable"",
                ""type"": ""function""
            },
            {
                ""constant"": false,
                ""inputs"": [
                    {
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""confirmTransaction"",
                ""outputs"": [],
                ""payable"": false,
                ""stateMutability"": ""nonpayable"",
                ""type"": ""function""
            },
            {
                ""constant"": false,
                ""inputs"": [
                    {
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""executeTransaction"",
                ""outputs"": [],
                ""payable"": false,
                ""stateMutability"": ""nonpayable"",
                ""type"": ""function""
            },
            {
                ""constant"": false,
                ""inputs"": [
                    {
                        ""name"": ""owner"",
                        ""type"": ""address""
                    }
                ],
                ""name"": ""removeOwner"",
                ""outputs"": [],
                ""payable"": false,
                ""stateMutability"": ""nonpayable"",
                ""type"": ""function""
            },
            {
                ""constant"": false,
                ""inputs"": [
                    {
                        ""name"": ""owner"",
                        ""type"": ""address""
                    },
                    {
                        ""name"": ""newOwner"",
                        ""type"": ""address""
                    }
                ],
                ""name"": ""replaceOwner"",
                ""outputs"": [],
                ""payable"": false,
                ""stateMutability"": ""nonpayable"",
                ""type"": ""function""
            },
            {
                ""constant"": false,
                ""inputs"": [
                    {
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""revokeConfirmation"",
                ""outputs"": [],
                ""payable"": false,
                ""stateMutability"": ""nonpayable"",
                ""type"": ""function""
            },
            {
                ""constant"": false,
                ""inputs"": [
                    {
                        ""name"": ""destination"",
                        ""type"": ""address""
                    },
                    {
                        ""name"": ""value"",
                        ""type"": ""uint256""
                    },
                    {
                        ""name"": ""data"",
                        ""type"": ""bytes""
                    }
                ],
                ""name"": ""submitTransaction"",
                ""outputs"": [
                    {
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""nonpayable"",
                ""type"": ""function""
            },
            {
                ""inputs"": [
                    {
                        ""name"": ""_owners"",
                        ""type"": ""address[]""
                    },
                    {
                        ""name"": ""_required"",
                        ""type"": ""uint256""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""nonpayable"",
                ""type"": ""constructor""
            },
            {
                ""payable"": true,
                ""stateMutability"": ""payable"",
                ""type"": ""fallback""
            },
            {
                ""anonymous"": false,
                ""inputs"": [
                    {
                        ""indexed"": true,
                        ""name"": ""sender"",
                        ""type"": ""address""
                    },
                    {
                        ""indexed"": true,
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""Confirmation"",
                ""type"": ""event""
            },
            {
                ""anonymous"": false,
                ""inputs"": [
                    {
                        ""indexed"": true,
                        ""name"": ""sender"",
                        ""type"": ""address""
                    },
                    {
                        ""indexed"": true,
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""Revocation"",
                ""type"": ""event""
            },
            {
                ""anonymous"": false,
                ""inputs"": [
                    {
                        ""indexed"": true,
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""Submission"",
                ""type"": ""event""
            },
            {
                ""anonymous"": false,
                ""inputs"": [
                    {
                        ""indexed"": true,
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""Execution"",
                ""type"": ""event""
            },
            {
                ""anonymous"": false,
                ""inputs"": [
                    {
                        ""indexed"": true,
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""ExecutionFailure"",
                ""type"": ""event""
            },
            {
                ""anonymous"": false,
                ""inputs"": [
                    {
                        ""indexed"": true,
                        ""name"": ""sender"",
                        ""type"": ""address""
                    },
                    {
                        ""indexed"": false,
                        ""name"": ""value"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""Deposit"",
                ""type"": ""event""
            },
            {
                ""anonymous"": false,
                ""inputs"": [
                    {
                        ""indexed"": true,
                        ""name"": ""owner"",
                        ""type"": ""address""
                    }
                ],
                ""name"": ""OwnerAddition"",
                ""type"": ""event""
            },
            {
                ""anonymous"": false,
                ""inputs"": [
                    {
                        ""indexed"": true,
                        ""name"": ""owner"",
                        ""type"": ""address""
                    }
                ],
                ""name"": ""OwnerRemoval"",
                ""type"": ""event""
            },
            {
                ""anonymous"": false,
                ""inputs"": [
                    {
                        ""indexed"": false,
                        ""name"": ""required"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""RequirementChange"",
                ""type"": ""event""
            },
            {
                ""constant"": true,
                ""inputs"": [
                    {
                        ""name"": """",
                        ""type"": ""uint256""
                    },
                    {
                        ""name"": """",
                        ""type"": ""address""
                    }
                ],
                ""name"": ""confirmations"",
                ""outputs"": [
                    {
                        ""name"": """",
                        ""type"": ""bool""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [
                    {
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""getConfirmationCount"",
                ""outputs"": [
                    {
                        ""name"": ""count"",
                        ""type"": ""uint256""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [
                    {
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""getConfirmations"",
                ""outputs"": [
                    {
                        ""name"": ""_confirmations"",
                        ""type"": ""address[]""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [],
                ""name"": ""getOwners"",
                ""outputs"": [
                    {
                        ""name"": """",
                        ""type"": ""address[]""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [
                    {
                        ""name"": ""pending"",
                        ""type"": ""bool""
                    },
                    {
                        ""name"": ""executed"",
                        ""type"": ""bool""
                    }
                ],
                ""name"": ""getTransactionCount"",
                ""outputs"": [
                    {
                        ""name"": ""count"",
                        ""type"": ""uint256""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [
                    {
                        ""name"": ""from"",
                        ""type"": ""uint256""
                    },
                    {
                        ""name"": ""to"",
                        ""type"": ""uint256""
                    },
                    {
                        ""name"": ""pending"",
                        ""type"": ""bool""
                    },
                    {
                        ""name"": ""executed"",
                        ""type"": ""bool""
                    }
                ],
                ""name"": ""getTransactionIds"",
                ""outputs"": [
                    {
                        ""name"": ""_transactionIds"",
                        ""type"": ""uint256[]""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [
                    {
                        ""name"": ""transactionId"",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""isConfirmed"",
                ""outputs"": [
                    {
                        ""name"": """",
                        ""type"": ""bool""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [
                    {
                        ""name"": """",
                        ""type"": ""address""
                    }
                ],
                ""name"": ""isOwner"",
                ""outputs"": [
                    {
                        ""name"": """",
                        ""type"": ""bool""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [],
                ""name"": ""MAX_OWNER_COUNT"",
                ""outputs"": [
                    {
                        ""name"": """",
                        ""type"": ""uint256""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [
                    {
                        ""name"": """",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""owners"",
                ""outputs"": [
                    {
                        ""name"": """",
                        ""type"": ""address""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [],
                ""name"": ""required"",
                ""outputs"": [
                    {
                        ""name"": """",
                        ""type"": ""uint256""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [],
                ""name"": ""transactionCount"",
                ""outputs"": [
                    {
                        ""name"": """",
                        ""type"": ""uint256""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            },
            {
                ""constant"": true,
                ""inputs"": [
                    {
                        ""name"": """",
                        ""type"": ""uint256""
                    }
                ],
                ""name"": ""transactions"",
                ""outputs"": [
                    {
                        ""name"": ""destination"",
                        ""type"": ""address""
                    },
                    {
                        ""name"": ""value"",
                        ""type"": ""uint256""
                    },
                    {
                        ""name"": ""data"",
                        ""type"": ""bytes""
                    },
                    {
                        ""name"": ""executed"",
                        ""type"": ""bool""
                    }
                ],
                ""payable"": false,
                ""stateMutability"": ""view"",
                ""type"": ""function""
            }
        ]";
    }
}
