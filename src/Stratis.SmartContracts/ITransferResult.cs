namespace Stratis.SmartContracts
{
    /// <summary>
    /// The result of executing a contract transfer or call.
    /// </summary>
    public interface ITransferResult
    {
        /// <summary>
        /// The return value of the method called.
        /// </summary>
        object ReturnValue { get; }

        /// <summary>
        /// The result of execution.
        /// </summary>
        bool Success { get; }
    }
}