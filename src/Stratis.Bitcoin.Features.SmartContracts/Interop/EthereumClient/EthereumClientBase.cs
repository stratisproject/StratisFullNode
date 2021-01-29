using System.Collections.Generic;
using System.Numerics;
using Nethereum.ABI;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Web3;
using Nethereum.Web3.Accounts.Managed;
using Stratis.Bitcoin.Features.SmartContracts.Interop.Models;

namespace Stratis.Bitcoin.Features.SmartContracts.Interop.EthereumClient
{
    public class EthereumClientBase : IEthereumClientBase
    {
        private readonly InteropSettings interopSettings;
        private readonly Web3 web3;

        public EthereumClientBase(InteropSettings interopSettings)
        {
            this.interopSettings = interopSettings;

            var account = new ManagedAccount(interopSettings.EthereumAccount, interopSettings.EthereumPassphrase);

            // TODO: Support loading offline accounts from keystore JSON directly?
            if (!string.IsNullOrWhiteSpace(interopSettings.EthereumClientUrl))
                this.web3 = new Web3(account, interopSettings.EthereumClientUrl);
            else
                this.web3 = new Web3(account);
        }

        /// <inheritdoc />
        public BigInteger SubmitTransaction(string destination, BigInteger value, string data)
        {
            return MultisigWallet.SubmitTransaction(this.web3, this.interopSettings.MultisigWalletAddress, destination, value, data).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public string ConfirmTransaction(BigInteger transactionId)
        {
            return MultisigWallet.ConfirmTransaction(this.web3, this.interopSettings.MultisigWalletAddress, transactionId).GetAwaiter().GetResult();
        }

        public BigInteger GetConfirmationCount(BigInteger transactionId)
        {
            return MultisigWallet.GetConfirmationCount(this.web3, this.interopSettings.MultisigWalletAddress, transactionId).GetAwaiter().GetResult();
        }

        public string EncodeMintParams(string address, BigInteger amount)
        {
            // TODO: Extract this directly from the ABI
            const string MintMethod = "40c10f19";

            var abiEncode = new ABIEncode();
            string result = MintMethod + abiEncode.GetABIEncoded(new ABIValue("address", address), new ABIValue("uint256", amount)).ToHex();

            return result;
        }

        public string EncodeBurnParams(BigInteger amount)
        {
            // TODO: Extract this directly from the ABI
            const string BurnMethod = "42966c68";

            var abiEncode = new ABIEncode();
            string result = BurnMethod + abiEncode.GetABIEncoded(new ABIValue("uint256", amount)).ToHex();

            return result;
        }

        public Dictionary<string, string> InvokeContract(InteropRequest request)
        {
            // TODO: Create an Ethereum transaction to invoke the specified method 
            return new Dictionary<string, string>();
        }

        public List<EthereumRequestModel> GetStratisInteropRequests()
        {
            // TODO: Filter the receipt logs for the interop contract and extract
            return new List<EthereumRequestModel>();
        }

        public bool TransmitResponse(InteropRequest request)
        {
            // TODO: Send a transaction to the interop contract recording the results of the execution on the Cirrus network
            return true;
        }
    }
}
