using System.Globalization;

namespace Stratis.SmartContracts
{
    /// <summary>
    /// The base object from which all smart contracts must inherit. Provides contextual information and state to a contract during execution.
    /// </summary>
    public abstract class SmartContract
    {
        /// <summary>
        /// The address of the smart contract.
        /// </summary>
        protected Address Address => this.state.Message.ContractAddress;

        /// <summary>
        /// The balance of the smart contract.
        /// </summary>
        public ulong Balance => this.state.GetBalance();

        /// <summary>
        /// Details about the current block.
        /// </summary>
        public IBlock Block => this.state.Block;

        /// <summary>
        /// Details about the current transaction that has been sent.
        /// </summary>
        public IMessage Message => this.state.Message;

        /// <summary>
        ///  Provides functionality for the saving and retrieval of objects inside smart contracts.
        /// </summary>
        public IPersistentState PersistentState => this.state.PersistentState;

        /// <summary>
        /// Provides functionality for the serialization and deserialization of primitives to bytes inside smart contracts.
        /// </summary>
        public ISerializer Serializer => this.state.Serializer;

        /// <summary>
        /// The execution state provided to the contract.
        /// </summary>
        private readonly ISmartContractState state;

        public SmartContract(ISmartContractState state)
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            this.state = state;
        }

        /// <summary>
        /// Sends funds to an address.
        /// 
        /// If address belongs to a contract, will invoke the <see cref="Receive"/> function on this contract.
        /// </summary>
        /// <param name="addressTo">The address to transfer the funds to.</param>
        /// <param name="amountToTransfer">The amount of funds to transfer, in satoshi.</param>
        protected ITransferResult Transfer(Address addressTo, ulong amountToTransfer)
        {
            return this.state.InternalTransactionExecutor.Transfer(this.state, addressTo, amountToTransfer);
        }

        /// <summary>
        /// Calls a method on another contract.
        /// </summary>
        /// <param name="addressTo">The contract on which to call the method.</param>
        /// <param name="amountToTransfer">The amount of funds to transfer, in satoshi.</param>
        /// <param name="methodName">The name of the method to call on the contract.</param>
        /// <param name="parameters">The parameters to inject to the method call.</param>
        /// <param name="gasLimit">The total amount of gas to allow this call to take up. Default is to use all remaining gas.</param>
        protected ITransferResult Call(Address addressTo, ulong amountToTransfer, string methodName, object[] parameters = null, ulong gasLimit = 0)
        {
            return this.state.InternalTransactionExecutor.Call(this.state, addressTo, amountToTransfer, methodName, parameters, gasLimit);
        }

        /// <summary>
        /// Creates a new contract.
        /// </summary>
        /// <typeparam name="T">Contract type to instantiate.</typeparam>
        /// <param name="amountToTransfer">The amount of funds to transfer, in satoshi.</param>
        /// <param name="parameters">The parameters to inject to the constructor.</param>
        /// <param name="gasLimit">The total amount of gas to allow this call to take up. Default is to use all remaining gas.</param>
        protected ICreateResult Create<T>(ulong amountToTransfer = 0, object[] parameters = null, ulong gasLimit = 0) where T : SmartContract
        {
            return this.state.InternalTransactionExecutor.Create<T>(this.state, amountToTransfer, parameters, gasLimit);
        }

        /// <summary>
        /// Returns a 32-byte Keccak256 hash of the given bytes.
        /// </summary>
        /// <param name="toHash"></param>
        /// <returns></returns>
        protected byte[] Keccak256(byte[] toHash)
        {
            return this.state.InternalHashHelper.Keccak256(toHash);
        }

        /// <summary>
        /// Halts contract execution by throwing an exception if the input condition is not met.
        /// </summary>
        protected void Assert(bool condition, string message = "Assert failed.")
        {
            if (!condition)
                throw new SmartContractAssertException(message);
        }

        /// <summary>
        /// Logs an event.
        /// </summary>
        /// <param name="toLog">A struct representing the data to log.</param>
        protected void Log<T>(T toLog) where T : struct
        {
            this.state.ContractLogger.Log(this.state, toLog);
        }

        /// <summary>
        /// The default method used to handle the receipt of funds. Override this method to define behaviour when the contract receives funds and
        /// no method name is provided or the method name in the calling transaction equals <see cref="string.Empty"/>.
        /// <para>
        /// This occurs when a contract sends funds to another contract using <see cref="Transfer"/>.
        /// </para>
        /// </summary>
        public virtual void Receive() {}
    }
}