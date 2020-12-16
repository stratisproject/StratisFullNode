namespace Stratis.SmartContracts
{
    /// <summary>
    /// Handles the execution of transactions that happen internally to a smart contract execution, eg. when a contract transfers funds
    /// to another contract.
    /// </summary>
    public interface IInternalTransactionExecutor
    {
        /// <summary>
        /// Transfers funds to another address. If address is a contract, will call the receive function.
        /// </summary>
        /// <param name="smartContractState">State representing existing contract's context.</param>
        /// <param name="addressTo">Where the funds will be transferred to.</param>
        /// <param name="amountToTransfer">The amount to send, in satoshi.</param>
        ITransferResult Transfer(ISmartContractState smartContractState, Address addressTo, ulong amountToTransfer);

        /// <summary>
        /// Calls a method on another contract.
        /// </summary>
        ITransferResult Call(ISmartContractState smartContractState, Address addressTo, ulong amountToTransfer, string methodName, object[] parameters, ulong gasLimit = 0);

        /// <summary>
        /// Creates a new contract.
        /// </summary>
        /// <typeparam name="T">Type of contract to create.</typeparam>
        /// <param name="smartContractState">State repository to track and persist changes to the contract.</param>
        /// <param name="amountToTransfer">Amount to send, in satoshi.</param>
        ICreateResult Create<T>(ISmartContractState smartContractState, ulong amountToTransfer, object[] parameters, ulong gasLimit = 0);
    }
}