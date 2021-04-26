using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface ISystemContractExecutionResult
    {
        IStateRepositoryRoot NewState { get; }
    }

    public class SystemContractExecutionResult : ISystemContractExecutionResult
    {
        public SystemContractExecutionResult(IStateRepositoryRoot newState)
        {
            this.NewState = newState;
        }

        public IStateRepositoryRoot NewState { get; }
    }
}
