using System.Numerics;
using System.Threading.Tasks;
using NBitcoin.DataEncoders;
using Nethereum.ABI;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Contracts.ContractHandlers;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;

namespace Stratis.Bitcoin.Features.Interop.EthereumClient.ContractSource
{
    public class GnosisSafeProxyDeployment : ContractDeploymentMessage
    {
        public static string BYTECODE =
            "0x608060405234801561001057600080fd5b5060405161014d38038061014d8339818101604052602081101561003357600080fd5b50516001600160a01b03811661007a5760405162461bcd60e51b81526004018080602001828103825260248152602001806101296024913960400191505060405180910390fd5b600080546001600160a01b039092166001600160a01b03199092169190911790556080806100a96000396000f3fe60806040526001600160a01b036000541663530ca43760e11b6000351415602a578060005260206000f35b3660008037600080366000845af43d6000803e806046573d6000fd5b3d6000f3fea265627a7a7231582046a8dbddaad1ac182d6cbeb8ec6e0517cdba24140b7b56fee470166efea336c664736f6c63430005110032496e76616c6964206d617374657220636f707920616464726573732070726f7669646564";

        public GnosisSafeProxyDeployment() : base(BYTECODE)
        {
        }

        // We do  not actually need to deploy this contract as it gets created for us by the proxy factory.
        // ...
    }

    // All the functions defined against this contract are actually delegated to be executed by the singleton master copy of the Gnosis Safe contract that is already deployed on-chain.

    [Function("setup", "uint256")]
    public class SetupFunction : FunctionMessage
    {
        [Parameter("address[]", "_owners", 1)]
        public string Owners { get; set; }

        [Parameter("uint256", "threshold", 2)]
        public BigInteger Threshold { get; set; }

        [Parameter("address", "to", 3)]
        public string To { get; set; }

        [Parameter("bytes", "data", 4)]
        public byte[] Data { get; set; }

        [Parameter("address", "fallbackHandler", 5)]
        public string FallbackHandler { get; set; }

        [Parameter("address", "paymentToken", 6)]
        public string PaymentToken { get; set; }

        [Parameter("uint256", "payment", 7)]
        public BigInteger Payment { get; set; }

        [Parameter("address", "paymentReceiver", 8)]
        public string PaymentReceiver { get; set; }
    }

    [Function("execTransaction", "bool")]
    public class ExecTransactionFunction : FunctionMessage
    {
        [Parameter("address", "to", 1)]
        public string To { get; set; }

        [Parameter("uint256", "value", 2)]
        public BigInteger Value { get; set; }
        
        [Parameter("bytes", "data", 3)]
        public byte[] Data { get; set; }

        [Parameter("uint8", "operation", 4)]
        public uint Operation { get; set; }

        [Parameter("uint256", "safeTxGas", 5)]
        public BigInteger SafeTxGas { get; set; }

        [Parameter("uint256", "baseGas", 6)]
        public BigInteger BaseGas { get; set; }

        [Parameter("uint256", "gasPrice", 7)]
        public BigInteger GasPrice { get; set; }

        [Parameter("address", "gasToken", 8)]
        public string GasToken { get; set; }

        [Parameter("address", "refundReceiver", 9)]
        public string RefundReceiver { get; set; }

        [Parameter("bytes", "signatures", 10)]
        public byte[] Signatures { get; set; }
    }

    public class GnosisSafeProxy
    {
        public static string EncodeSetup(string[] owners, BigInteger threshold, string to = "0x0000000000000000000000000000000000000000", byte[] data = null, string fallbackHandler = "0x0000000000000000000000000000000000000000", string paymentToken = "0x0000000000000000000000000000000000000000", BigInteger payment = new BigInteger(), string paymentReceiver = "0x0000000000000000000000000000000000000000")
        {
            if (data == null)
                data = new byte[] { };

            var abiEncode = new ABIEncode();

            // @dev Setup function sets initial storage of contract.
            // @param _owners List of Safe owners.
            // @param _threshold Number of required confirmations for a Safe transaction.
            // @param to Contract address for optional delegate call.
            // @param data Data payload for optional delegate call.
            // @param fallbackHandler Handler for fallback calls to this contract
            // @param paymentToken Token that should be used for the payment (0 is ETH)
            // @param payment Value that should be paid
            // @param paymentReceiver Address that should receive the payment (or 0 if tx.origin)
            return abiEncode.GetABIEncoded(
                new ABIValue("address[]", owners),
                new ABIValue("uint256", threshold),
                new ABIValue("address", to),
                new ABIValue("bytes", data),
                new ABIValue("address", fallbackHandler),
                new ABIValue("address", paymentToken),
                new ABIValue("uint256", payment),
                new ABIValue("address", paymentReceiver)
            ).ToHex();
        }

        /// <summary>
        /// Allows to execute a Safe transaction confirmed by required number of owners and then pays the account that submitted the transaction.
        /// </summary>
        /// <remarks>Note: The fees are always transferred, even if the user transaction fails.</remarks>
        /// <param name="web3">Instance of the web3 client to execute the function against.</param>
        /// <param name="proxyContract">The address of the Gnosis Safe proxy deployment that owns the wrapped STRAX contract.</param>
        /// <param name="wrappedStraxContract">The address of the wrapped STRAX ERC20 contract.</param>
        /// <param name="value">The Ether value of the transaction, if applicable. For ERC20 transfers this is 0.</param>
        /// <param name="data">The ABI-encoded data of the transaction, e.g. if a contract method is being called. For ERC20 transfers this will be set.</param>
        /// <param name="safeTxGas">Gas that should be used for the Safe transaction.</param>
        /// <param name="baseGas">Gas costs that are independent of the transaction execution (e.g. base transaction fee, signature check, payment of the refund).</param>
        /// <param name="gasPrice">Gas price that should be used for the payment calculation.</param>
        /// <param name="signatureCount">The number of packed signatures included.</param>
        /// <param name="signatures">Packed signature data ({bytes32 r}{bytes32 s}{uint8 v}).</param>
        /// <returns>The transaction hash of the execution transaction.</returns>
        public static async Task<string> ExecTransactionAsync(Web3 web3, string proxyContract, string wrappedStraxContract, BigInteger value, string data, BigInteger safeTxGas, BigInteger baseGas, BigInteger gasPrice, int signatureCount, byte[] signatures)
        {
            // These parameters are supplied to the function hardcoded:
            // @param operation Operation type of Safe transaction. The Safe supports CALL (uint8 = 0), DELEGATECALL (uint8 = 1) and CREATE (uint8 = 2).
            // @param gasToken Token address (or 0 if ETH) that is used for the payment.
            // @param refundReceiver Address of receiver of gas payment (or 0 if tx.origin).

            IContractTransactionHandler<ExecTransactionFunction> execHandler = web3.Eth.GetContractTransactionHandler<ExecTransactionFunction>();

            var execTransactionFunctionMessage = new ExecTransactionFunction()
            {
                To = proxyContract,
                Value = value,
                Data = Encoders.Hex.DecodeData(data),
                Operation = 0, // CALL
                SafeTxGas = safeTxGas,
                BaseGas = baseGas,
                GasPrice = Web3.Convert.ToWei(gasPrice, UnitConversion.EthUnit.Gwei),
                GasToken = EthereumClientBase.ZeroAddress,
                RefundReceiver = EthereumClientBase.ZeroAddress,
                Signatures = signatures
            };

            TransactionReceipt execTransactionReceipt = await execHandler.SendRequestAndWaitForReceiptAsync(proxyContract, execTransactionFunctionMessage).ConfigureAwait(false);
            
            return execTransactionReceipt.TransactionHash;
        }

        public static string ABI = @"[
	        {
		        ""inputs"": [
			        {
				        ""internalType"": ""address"",
				        ""name"": ""_masterCopy"",
				        ""type"": ""address""
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
	        }
        ]";
    }
}
