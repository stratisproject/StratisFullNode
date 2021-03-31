using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Contracts;

namespace Stratis.Bitcoin.Features.Interop.EthereumClient
{
    public interface IEthereumClientBase
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
}
