﻿using System.Collections.Generic;
using System.Numerics;
using Nethereum.Contracts;
using Stratis.Bitcoin.Features.Interop.Models;

namespace Stratis.Bitcoin.Features.Interop.EthereumClient
{
    public interface IEthereumClientBase
    {
        List<EventLog<TransferEventDTO>> GetTransferEventsForWrappedStrax();

        string GetDestinationAddress(string address);

        BigInteger GetBlockHeight();

        /// <summary>
        /// Submits a transaction to the multisig wallet contract, to enable it to be separately confirmed by a quorum of the multisig wallet owners.
        /// </summary>
        /// <param name="destination">The account that the transaction is being sent to after confirmation. For wSTRAX operations this will typically be the wSTRAX ERC20 contract address.</param>
        /// <param name="value">The amount that is being sent. For wSTRAX operations this is typically zero, as the balance changes are encoded within the additional data.</param>
        /// <param name="data">Additional transaction data. This is encoded in accordance with the applicable contract's ABI.</param>
        /// <returns>Returns the transactionId of the transaction</returns>
        BigInteger SubmitTransaction(string destination, BigInteger value, string data);

        /// <summary>
        /// Confirms a multisig wallet transaction.
        /// </summary>
        /// <remarks>Once a sufficient threshold of confirmations is reached, the contract will automatically execute the saved transaction.</remarks>
        /// <param name="transactionId">The transactionId of an existing transaction stored in the multisig wallet contract.</param>
        /// <returns>The hash of the confirmation transaction.</returns>
        string ConfirmTransaction(BigInteger transactionId);

        BigInteger GetConfirmationCount(BigInteger transactionId);

        string EncodeMintParams(string address, BigInteger amount);

        string EncodeBurnParams(BigInteger amount);

        Dictionary<string, string> InvokeContract(InteropRequest request);

        List<EthereumRequestModel> GetStratisInteropRequests();

        bool TransmitResponse(InteropRequest request);
    }
}
