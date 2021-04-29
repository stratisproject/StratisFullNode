using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface ISystemContractRunnerResult
    {
        IStateRepositoryRoot NewState { get; }

        object Result { get; }
    }

    public class SystemContractRunnerResult : ISystemContractRunnerResult
    {
        public SystemContractRunnerResult(IStateRepositoryRoot newState)
        {
            this.NewState = newState;
        }

        public SystemContractRunnerResult(IStateRepositoryRoot newState, object result)
        {
            this.NewState = newState;
            this.Result = result;
        }

        public IStateRepositoryRoot NewState { get; }

        public object Result { get; }
    }
}
