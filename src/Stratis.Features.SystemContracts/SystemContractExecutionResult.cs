using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface ISystemContractExecutionResult
    {
        IStateRepository NewState { get; }
    }

    public class SystemContractExecutionResult : ISystemContractExecutionResult
    {
        public SystemContractExecutionResult(IStateRepository newState)
        {
            this.NewState = newState;
        }

        public IStateRepository NewState { get; }
    }
}
