using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.Blocks;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts.Managed;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    public interface IETHClient
    {
        /// <summary>
        /// Creates the filter that the RPC interface uses to listen for events against the desired contract.
        /// In this case the filter is specifically listening for Transfer events emitted by the wrapped strax
        /// contract.
        /// </summary>
        Task CreateTransferEventFilterAsync();

        /// <summary>
        /// Queries the previously created event filter for any new events matching the filter criteria.
        /// </summary>
        /// <returns>A list of event logs.</returns>
        Task<List<EventLog<TransferEventDTO>>> GetTransferEventsForWrappedStraxAsync();

        /// <summary>
        /// Retrieves the STRAX address that was recorded in the wrapped STRAX contract when a given account
        /// burnt funds. The destination address must have been provided as a parameter to the burn() method
        /// invocation and only one address at a time can be associated with each account.
        /// </summary>
        /// <param name="address">The Ethereum account to retrieve the destination STRAX address for.</param>
        /// <returns>The STRAX address associated with the provided Ethereum account.</returns>
        Task<string> GetDestinationAddressAsync(string address);

        /// <summary>
        /// Retrieves the current block height of the Ethereum node.
        /// </summary>
        Task<BigInteger> GetBlockHeightAsync();

        /// <summary>
        /// Submits a transaction to the multisig wallet contract, to enable it to be separately confirmed by a quorum of the multisig wallet owners.
        /// </summary>
        /// <param name="destination">The account that the transaction is being sent to after confirmation. For wSTRAX operations this will typically be the wSTRAX ERC20 contract address.</param>
        /// <param name="value">The amount that is being sent. For wSTRAX operations this is typically zero, as the balance changes are encoded within the additional data.</param>
        /// <param name="data">Additional transaction data. This is encoded in accordance with the applicable contract's ABI.</param>
        /// <returns>Returns the transactionId of the transaction</returns>
        Task<BigInteger> SubmitTransactionAsync(string destination, BigInteger value, string data);

        /// <summary>
        /// Confirms a multisig wallet transaction.
        /// </summary>
        /// <remarks>Once a sufficient threshold of confirmations is reached, the contract will automatically execute the saved transaction.</remarks>
        /// <param name="transactionId">The transactionId of an existing transaction stored in the multisig wallet contract.</param>
        /// <returns>The hash of the confirmation transaction.</returns>
        Task<string> ConfirmTransactionAsync(BigInteger transactionId);

        /// <summary>
        /// Retrieve the number of confirmations a given transaction currently has in the multisig wallet contract.
        /// </summary>
        /// <param name="transactionId">The identifier of the transaction.</param>
        /// <returns>The number of confirmations.</returns>
        Task<BigInteger> GetConfirmationCountAsync(BigInteger transactionId);

        Task<BigInteger> GetErc20BalanceAsync(string addressToQuery);

        /// <summary>
        /// Returns the encoded form of transaction data that calls the transfer(address, uint256) method on the WrappedStrax contract.
        /// This is exactly the contents of the 'data' field in a normal transaction.
        /// This encoded data is required for submitting a transaction to the multisig contract.
        /// </summary>
        /// <param name="address">The address to transfer a quantity of the wSTRAX token to.</param>
        /// <param name="amount">The amount (in wei) of tokens to be transferred.</param>
        /// <returns>The hex data of the encoded parameters.</returns>
        string EncodeTransferParams(string address, BigInteger amount);

        /// <summary>
        /// Constructs the data field for a transaction invoking the mint() method of an ERC20 contract that implements it.
        /// The actual transaction will be sent to the multisig wallet contract as it is the contract that needs to execute the transaction.
        /// </summary>
        /// <param name="address">The account that needs tokens to be minted into it (not the address of the multisig contract or the wrapped STRAX contract)</param>
        /// <param name="amount">The number of tokens to be minted. This is denominated in wei.</param>
        /// <returns>The hex data of the encoded parameters.</returns>
        string EncodeMintParams(string address, BigInteger amount);

        /// <summary>
        /// Constructs the data field for a transaction invoking the burn() method of an ERC20 contract that implements it.
        /// The actual transaction will be sent to the multisig wallet contract as it is the contract that needs to execute the transaction.
        /// </summary>
        /// <param name="amount">The number of tokens to be minted. This is denominated in wei.</param>
        /// <param name="straxAddress">The destination address on the STRAX chain that the equivalent value of the burnt funds will be sent to.</param>
        /// <returns>The hex data of the encoded parameters.</returns>
        string EncodeBurnParams(BigInteger amount, string straxAddress);
    }

    public class ETHClient : IETHClient
    {
        private readonly InteropSettings interopSettings;
        private readonly Web3 web3;
        private Event<TransferEventDTO> transferEventHandler;
        private NewFilterInput filterAllTransferEventsForContract;
        private HexBigInteger filterId;

        public const string ZeroAddress = "0x0000000000000000000000000000000000000000";

        public ETHClient(InteropSettings interopSettings)
        {
            this.interopSettings = interopSettings;

            if (!this.interopSettings.InteropEnabled)
                return;

            var account = new ManagedAccount(interopSettings.ETHAccount, interopSettings.ETHPassphrase);
            
            // TODO: Support loading offline accounts from keystore JSON directly?
            this.web3 = !string.IsNullOrWhiteSpace(interopSettings.ETHClientUrl) ? new Web3(account, interopSettings.ETHClientUrl) : new Web3(account);
        }

        /// <inheritdoc />
        public async Task CreateTransferEventFilterAsync()
        {
            this.transferEventHandler = this.web3.Eth.GetEvent<TransferEventDTO>(this.interopSettings.ETHWrappedStraxContractAddress);
            this.filterAllTransferEventsForContract = this.transferEventHandler.CreateFilterInput();
            this.filterId = await this.transferEventHandler.CreateFilterAsync(this.filterAllTransferEventsForContract).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<EventLog<TransferEventDTO>>> GetTransferEventsForWrappedStraxAsync()
        {
            try
            {
                // Note: this will only return events from after the filter is created.
                return await this.transferEventHandler.GetFilterChanges(this.filterId).ConfigureAwait(false);
            }
            catch (RpcResponseException)
            {
                // If the filter is no longer available it may need to be re-created.
                await this.CreateTransferEventFilterAsync().ConfigureAwait(false);
            }

            return await this.transferEventHandler.GetFilterChanges(this.filterId).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> GetDestinationAddressAsync(string address)
        {
            return await WrappedStrax.GetDestinationAddressAsync(this.web3, this.interopSettings.ETHWrappedStraxContractAddress, address).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BigInteger> GetBlockHeightAsync()
        {
            var blockNumberHandler = new EthBlockNumber(this.web3.Client);
            HexBigInteger block = await blockNumberHandler.SendRequestAsync().ConfigureAwait(false);
            
            return block.Value;
        }

        /// <inheritdoc />
        public async Task<BigInteger> SubmitTransactionAsync(string destination, BigInteger value, string data)
        {
            return await MultisigWallet.SubmitTransactionAsync(this.web3, this.interopSettings.ETHMultisigWalletAddress, destination, value, data, this.interopSettings.ETHGasLimit, this.interopSettings.ETHGasPrice).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> ConfirmTransactionAsync(BigInteger transactionId)
        {
            return await MultisigWallet.ConfirmTransactionAsync(this.web3, this.interopSettings.ETHMultisigWalletAddress, transactionId, this.interopSettings.ETHGasLimit, this.interopSettings.ETHGasPrice).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BigInteger> GetConfirmationCountAsync(BigInteger transactionId)
        {
            return await MultisigWallet.GetConfirmationCountAsync(this.web3, this.interopSettings.ETHMultisigWalletAddress, transactionId).ConfigureAwait(false);
        }

        public async Task<BigInteger> GetErc20BalanceAsync(string addressToQuery)
        {
            return await WrappedStrax.GetErc20BalanceAsync(this.web3, this.interopSettings.ETHWrappedStraxContractAddress, addressToQuery).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public string EncodeTransferParams(string address, BigInteger amount)
        {
            // TODO: Extract this directly from the ABI
            const string TransferMethod = "a9059cbb";

            var abiEncode = new ABIEncode();
            string result = TransferMethod + abiEncode.GetABIEncoded(new ABIValue("address", address), new ABIValue("uint256", amount)).ToHex();

            return result;
        }

        /// <inheritdoc />
        public string EncodeMintParams(string address, BigInteger amount)
        {
            // TODO: Extract this directly from the ABI
            const string MintMethod = "40c10f19";

            var abiEncode = new ABIEncode();
            string result = MintMethod + abiEncode.GetABIEncoded(new ABIValue("address", address), new ABIValue("uint256", amount)).ToHex();

            return result;
        }

        /// <inheritdoc />
        public string EncodeBurnParams(BigInteger amount, string straxAddress)
        {
            // TODO: Extract this directly from the ABI
            const string BurnMethod = "7641e6f3";

            var abiEncode = new ABIEncode();
            string result = BurnMethod + abiEncode.GetABIEncoded(new ABIValue("uint256", amount), new ABIValue("string", straxAddress)).ToHex();

            return result;
        }
    }
}
