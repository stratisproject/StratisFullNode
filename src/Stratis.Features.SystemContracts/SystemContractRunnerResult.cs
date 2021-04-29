using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface ISystemContractRunnerResult
    {
        IStateRepositoryRoot NewState { get; }
    }

    public class SystemContractRunnerResult : ISystemContractRunnerResult
    {
        public SystemContractRunnerResult(IStateRepositoryRoot newState)
        {
            this.NewState = newState;
        }

        public IStateRepositoryRoot NewState { get; }
    }
}
