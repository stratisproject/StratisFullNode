namespace Stratis.SmartContracts
{
    public interface IContractLogger
    {
        /// <summary>
        /// Logs an event that occurred during execution of this contract.
        /// </summary>
        void Log<T>(ISmartContractState smartContractState, T toLog) where T : struct;
    }
}
