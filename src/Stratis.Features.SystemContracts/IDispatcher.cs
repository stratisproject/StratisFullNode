using CSharpFunctionalExtensions;

namespace Stratis.Features.SystemContracts
{
    public interface IDispatcher<T> : IDispatcher
    {
        T GetInstance(SystemContractTransactionContext context);
    }

    public interface IDispatcher
    {
        Result Dispatch(SystemContractTransactionContext context);
    }
}